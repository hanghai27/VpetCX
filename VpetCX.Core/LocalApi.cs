using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VpetCX.Core
{
    internal sealed class LocalApi
    {
        public const string Address = "127.0.0.1:52815";
        private const int MaximumRequestBytes = 16384;
        private const int MaximumTextLength = 2000;
        private const int MaximumConcurrentRequests = 8;
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpListener _listener = new();
        private readonly IMainWindow _mainWindow;
        private readonly Action<string> _log;
        private readonly CoreLoader _core;
        private readonly Action _openManager;
        private readonly Action _openWeather;
        private readonly Action _openDiary;
        private readonly Action _openReminders;
        private readonly CancellationTokenSource _stopTokenSource = new();
        private readonly SemaphoreSlim _requestSlots = new(MaximumConcurrentRequests);
        private int _stopped;

        private static readonly HttpClient Client = new()
        {
            BaseAddress = new Uri($"http://{Address}/"),
            Timeout = TimeSpan.FromSeconds(50)
        };

        public LocalApi(
            IMainWindow mainWindow,
            Action<string> log,
            CoreLoader core,
            Action openManager,
            Action openWeather,
            Action openDiary,
            Action openReminders)
        {
            _mainWindow = mainWindow;
            _log = log;
            _core = core;
            _openManager = openManager;
            _openWeather = openWeather;
            _openDiary = openDiary;
            _openReminders = openReminders;
            _listener.Prefixes.Add($"http://{Address}/");
        }

        public void Start()
        {
            try
            {
                _listener.Start();
            }
            catch
            {
                _listener.Close();
                throw;
            }

            _ = Task.Run(ListenAsync);
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0)
                return;

            _stopTokenSource.Cancel();
            if (_listener.IsListening)
                _listener.Close();
        }

        public static async Task<TResponse> PostAsync<TRequest, TResponse>(
            string endpoint,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            using var response = await Client.PostAsJsonAsync(endpoint, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
            return result ?? throw new JsonException($"The local API returned an empty response for '{endpoint}'.");
        }

        public static Task<LocalApiResponse> SayAsync(string text, CancellationToken cancellationToken = default) =>
            PostAsync<object, LocalApiResponse>("say", new { text }, cancellationToken);

        private async Task ListenAsync()
        {
            try
            {
                while (!_stopTokenSource.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (HttpListenerException) when (_stopTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (ObjectDisposedException) when (_stopTokenSource.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!_requestSlots.Wait(0))
                    {
                        try
                        {
                            await WriteResponseAsync(context.Response, 503,
                                new { error = "The local server is busy. Try again shortly." });
                        }
                        catch (Exception ex) when (IsConnectionException(ex))
                        {
                            _log($"Could not return the busy response: {ex.Message}");
                        }
                        finally
                        {
                            CloseResponse(context.Response);
                        }

                        continue;
                    }

                    _ = HandleAndReleaseAsync(context);
                }
            }
            catch (Exception ex)
            {
                _log($"Local HTTP server stopped unexpectedly: {ex}");
                Stop();
            }
        }

        private async Task HandleAndReleaseAsync(HttpListenerContext context)
        {
            try
            {
                await HandleRequestAsync(context);
            }
            finally
            {
                _requestSlots.Release();
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var remoteEndPoint = context.Request.RemoteEndPoint;
                if (remoteEndPoint == null || !IPAddress.IsLoopback(remoteEndPoint.Address))
                {
                    await WriteResponseAsync(context.Response, 403, new { error = "Loopback requests only." });
                    return;
                }

                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (!IsSupportedPostPath(path))
                {
                    await WriteResponseAsync(context.Response, 404, new { error = "Endpoint not found." });
                    return;
                }

                if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.Headers["Allow"] = "POST";
                    await WriteResponseAsync(context.Response, 405, new { error = "POST is required." });
                    return;
                }

                using var requestTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    _stopTokenSource.Token);
                requestTokenSource.CancelAfter(RequestTimeout);
                var token = requestTokenSource.Token;

                if (context.Request.ContentType?.StartsWith(
                        "application/json", StringComparison.OrdinalIgnoreCase) != true)
                {
                    await WriteResponseAsync(context.Response, 415,
                        new { error = "Content-Type must be application/json." });
                    return;
                }

                var requestBody = await ReadRequestBodyAsync(context.Request, token);
                if (requestBody == null)
                {
                    await WriteResponseAsync(context.Response, 413, new { error = "Request body is too large." });
                    return;
                }

                PluginApiRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<PluginApiRequest>(requestBody, JsonOptions);
                }
                catch (JsonException)
                {
                    await WriteResponseAsync(context.Response, 400, new { error = "Invalid JSON request." });
                    return;
                }

                if (request == null)
                {
                    await WriteResponseAsync(context.Response, 400, new { error = "Invalid JSON request." });
                    return;
                }

                if (string.Equals(path, "/say", StringComparison.OrdinalIgnoreCase))
                {
                    var text = request.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength)
                    {
                        await WriteResponseAsync(context.Response, 400,
                            new { error = $"Text must contain 1 to {MaximumTextLength} characters." });
                        return;
                    }

                    await _mainWindow.Dispatcher.InvokeAsync(
                        () => _mainWindow.Main.Say(text),
                        DispatcherPriority.Normal,
                        token);
                    await WriteResponseAsync(context.Response, 200, new { status = "spoken" });
                    return;
                }

                var openAction = path.ToLowerInvariant() switch
                {
                    "/open_manager" => _openManager,
                    "/open_weather" => _openWeather,
                    "/open_diary" => _openDiary,
                    "/open_reminders" => _openReminders,
                    _ => null
                };
                if (openAction != null)
                {
                    await _mainWindow.Dispatcher.InvokeAsync(openAction, DispatcherPriority.Normal, token);
                    await WriteResponseAsync(context.Response, 200, new { status = "opened" });
                    return;
                }

                var (pluginName, arguments) = CreatePluginAction(path, request);
                var result = await _core.ExecutePluginAction(pluginName, arguments);
                await WriteResponseAsync(context.Response, 200, new { result });
            }
            catch (InvalidOperationException ex)
            {
                _log($"Local API action could not run: {ex.Message}");
                await TryWriteErrorResponseAsync(context.Response, 503, ex.Message);
            }
            catch (ArgumentException ex)
            {
                await TryWriteErrorResponseAsync(context.Response, 400, ex.Message);
            }
            catch (OperationCanceledException) when (_stopTokenSource.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                await TryWriteErrorResponseAsync(context.Response, 408, "The request timed out.");
            }
            catch (Exception ex)
            {
                _log($"Local HTTP request failed: {ex}");
                await TryWriteErrorResponseAsync(context.Response, 500, "The request could not be completed.");
            }
            finally
            {
                CloseResponse(context.Response);
            }
        }

        private static (string PluginName, string Arguments) CreatePluginAction(
            string path,
            PluginApiRequest request)
        {
            static string Value(string? value) => JsonSerializer.Serialize(value ?? string.Empty);
            var action = request.Action?.Trim().ToLowerInvariant();
            return path.ToLowerInvariant() switch
            {
                "/diary" => ("diary",
                    $"action({Value(RequireAction(action, "view", "view", "list", "write", "search", "delete"))}), " +
                    $"text({Value(request.Text)}), id({Value(request.Id)})"),
                "/reminders" => ("reminder",
                    $"action({Value(action switch
                    {
                        null or "" or "list" => "api_list",
                        "save" => "api_save",
                        "delete" => "api_delete",
                        _ => throw new ArgumentException("Reminder action must be list, save, or delete.")
                    })}), " +
                    $"time({Value(request.Time?.ToString(System.Globalization.CultureInfo.InvariantCulture))}), " +
                    $"unit({Value(request.Unit)}), event({Value(request.Event)}), id({Value(request.Id)})"),
                "/weather" => ("weather",
                    $"city({Value(request.City)}), " +
                    $"type({Value(RequireAction(request.Type?.Trim().ToLowerInvariant(), "current", "current", "forecast"))}), " +
                    $"name({Value(request.Name)}), admin1({Value(request.Admin1)}), admin2({Value(request.Admin2)}), " +
                    $"country({Value(request.Country)}), " +
                    $"latitude({Value(request.Latitude?.ToString("R", System.Globalization.CultureInfo.InvariantCulture))}), " +
                    $"longitude({Value(request.Longitude?.ToString("R", System.Globalization.CultureInfo.InvariantCulture))})"),
                "/weather/search" => ("weather",
                    $"city({Value(request.City)}), type({Value("search")})"),
                _ => throw new ArgumentException("Unsupported local API endpoint.")
            };
        }

        private static string RequireAction(string? action, string defaultValue, params string[] supportedActions)
        {
            var selectedAction = string.IsNullOrWhiteSpace(action) ? defaultValue : action;
            if (!supportedActions.Contains(selectedAction, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported action '{selectedAction}'.");

            return selectedAction;
        }

        private static bool IsSupportedPostPath(string path) =>
            path.Equals("/say", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/open_manager", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/open_weather", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/open_diary", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/open_reminders", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/diary", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/reminders", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/weather", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/weather/search", StringComparison.OrdinalIgnoreCase);

        private static async Task<byte[]?> ReadRequestBodyAsync(
            HttpListenerRequest request,
            CancellationToken cancellationToken)
        {
            if (request.ContentLength64 > MaximumRequestBytes)
                return null;

            using var body = new MemoryStream();
            var buffer = new byte[1024];
            int bytesRead;
            while ((bytesRead = await request.InputStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + bytesRead > MaximumRequestBytes)
                    return null;

                body.Write(buffer, 0, bytesRead);
            }

            return body.ToArray();
        }

        private static async Task WriteResponseAsync(HttpListenerResponse response, int statusCode, object payload)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(response.OutputStream, payload);
        }

        private async Task TryWriteErrorResponseAsync(
            HttpListenerResponse response,
            int statusCode,
            string message)
        {
            try
            {
                await WriteResponseAsync(response, statusCode, new { error = message });
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                _log($"Could not return a local HTTP error response: {ex.Message}");
            }
        }

        private void CloseResponse(HttpListenerResponse response)
        {
            try
            {
                response.Close();
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                _log($"Could not close a local HTTP response: {ex.Message}");
            }
        }

        private static bool IsConnectionException(Exception exception) =>
            exception is HttpListenerException or ObjectDisposedException or IOException;

        private sealed class PluginApiRequest
        {
            public string? Action { get; set; }
            public string? Text { get; set; }
            public string? Id { get; set; }
            public int? Time { get; set; }
            public string? Unit { get; set; }
            public string? Event { get; set; }
            public string? City { get; set; }
            public string? Type { get; set; }
            public string? Name { get; set; }
            public string? Admin1 { get; set; }
            public string? Admin2 { get; set; }
            public string? Country { get; set; }
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
        }
    }

    internal sealed class LocalApiResponse
    {
        public string? Result { get; init; }
    }

    internal sealed record ReminderApiInfo(string Id, DateTimeOffset DueAt, string Message, bool IsDelivered);

    internal sealed record WeatherApiLocationInfo(
        string Name,
        string? Admin1,
        string? Admin2,
        string? Country,
        double Latitude,
        double Longitude);

    internal sealed class ReminderApiOperationResponse
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public ReminderApiInfo? Reminder { get; init; }
    }
}
