using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VpetCX.Abstractions.Plugins;

namespace WeatherPlugin
{
    public sealed record WeatherLocationInfo(
        string Name,
        string? Admin1,
        string? Admin2,
        string? Country,
        double Latitude,
        double Longitude);

    /// <summary>
    /// Gets current conditions and forecasts from Open-Meteo.
    /// </summary>
    public class WeatherPlugin : VPetPlugin
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
        private static readonly string[] WindDirections =
        {
            "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
            "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
        };

        public override string Name => "weather";
        public override string Author => "VpetCX";

        public override string Description =>
            _core?.Localize("plugin_description_weather") ??
            "Get current weather and a 4-day forecast worldwide using Open-Meteo.";

        public override string Parameters => "city(string, required), type(string, optional: current/forecast)";

        public override string Examples =>
            "weather city(Hanoi), type(current) | weather city(Tokyo), type(forecast)";

        private PluginContext? _core;

        public override void Initialize(PluginContext core)
        {
            _core = core;
            _core.Log("Weather Plugin Initialized (Open-Meteo).");
        }

        public override async Task<string> Function(string arguments)
        {
            var cityName = PluginArguments.TryGetValue(arguments, "city", out var cityValue)
                ? cityValue.Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(cityName) || cityName.Length > 120)
                return T("weather_error_city");

            var queryType = PluginArguments.TryGetValue(arguments, "type", out var typeValue)
                ? typeValue.Trim().ToLowerInvariant()
                : "current";

            if (queryType is not ("current" or "forecast" or "search"))
                return T("weather_error_type");

            try
            {
                if (queryType == "search")
                    return JsonSerializer.Serialize(await SearchLocationsAsync(cityName));

                if (TryGetSelectedLocation(arguments, out var selectedLocation) &&
                    selectedLocation != null)
                    return await GetWeatherAsync(selectedLocation, queryType);

                var locations = await SearchLocationsAsync(cityName);
                if (locations.Count == 0)
                    return T("weather_no_location", cityName);

                return await GetWeatherAsync(locations[0], queryType);
            }
            catch (HttpRequestException ex)
            {
                _core?.Log($"Weather API request failed: {ex}");
                return T("weather_connection_failed");
            }
            catch (TaskCanceledException ex)
            {
                _core?.Log($"Weather API request timed out: {ex}");
                return T("weather_timeout");
            }
            catch (JsonException ex)
            {
                _core?.Log($"Weather API returned invalid data: {ex}");
                return T("weather_invalid_response");
            }
            catch (FormatException ex)
            {
                _core?.Log($"Weather API returned an invalid value: {ex}");
                return T("weather_incomplete_data");
            }
        }

        private static bool TryGetSelectedLocation(string arguments, out WeatherLocationInfo? location)
        {
            location = null;
            if (!PluginArguments.TryGetValue(arguments, "latitude", out var latitudeText) ||
                !PluginArguments.TryGetValue(arguments, "longitude", out var longitudeText) ||
                !double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                !PluginArguments.TryGetValue(arguments, "name", out var name) ||
                string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            PluginArguments.TryGetValue(arguments, "admin1", out var admin1);
            PluginArguments.TryGetValue(arguments, "admin2", out var admin2);
            PluginArguments.TryGetValue(arguments, "country", out var country);
            location = new WeatherLocationInfo(name, admin1, admin2, country, latitude, longitude);
            return true;
        }

        private static HttpClient CreateHttpClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async Task<IReadOnlyList<WeatherLocationInfo>> SearchLocationsAsync(string query)
        {
            var url = "https://geocoding-api.open-meteo.com/v1/search" +
                      $"?name={Uri.EscapeDataString(query)}&count=10&language={GeocodingLanguage}&format=json";
            using var response = await HttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var result = await JsonSerializer.DeserializeAsync<GeocodingResponse>(stream, JsonOptions);
            if (result?.Results is not { Length: > 0 })
                return Array.Empty<WeatherLocationInfo>();

            return result.Results
                .Where(IsValidLocation)
                .Select(location => new WeatherLocationInfo(
                    location.Name,
                    location.Admin1,
                    location.Admin2,
                    location.Country,
                    location.Latitude,
                    location.Longitude))
                .ToArray();
        }

        public async Task<string> GetWeatherAsync(WeatherLocationInfo location, string type)
        {
            ArgumentNullException.ThrowIfNull(location);
            if (!IsValidLocation(location.Latitude, location.Longitude))
                throw new ArgumentException("The selected location has invalid coordinates.", nameof(location));
            if (type is not ("current" or "forecast"))
                throw new ArgumentException("Weather type must be current or forecast.", nameof(type));

            var internalLocation = new Location
            {
                Name = location.Name,
                Admin1 = location.Admin1,
                Admin2 = location.Admin2,
                Country = location.Country,
                Latitude = location.Latitude,
                Longitude = location.Longitude
            };
            var forecast = await GetWeatherDataAsync(internalLocation, type == "forecast");
            return type == "forecast"
                ? FormatForecast(internalLocation, forecast)
                : FormatCurrent(internalLocation, forecast);
        }

        private static bool IsValidLocation(Location location) =>
            IsValidLocation(location.Latitude, location.Longitude);

        private static bool IsValidLocation(double latitude, double longitude) =>
            double.IsFinite(latitude) && latitude is >= -90 and <= 90 &&
            double.IsFinite(longitude) && longitude is >= -180 and <= 180;

        private static async Task<ForecastResponse> GetWeatherDataAsync(Location location, bool includeDaily)
        {
            var url = "https://api.open-meteo.com/v1/forecast" +
                      $"?latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                      $"&longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}";
            url += includeDaily
                ? "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=4&timezone=auto"
                : "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m,wind_direction_10m";
            using var response = await HttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<ForecastResponse>(stream, JsonOptions)
                   ?? throw new JsonException("The forecast response was empty.");
        }

        private string FormatCurrent(Location location, ForecastResponse forecast)
        {
            var current = forecast.Current
                          ?? throw new JsonException("The current weather section was missing.");
            var temperature = RequireFinite(current.Temperature, "temperature_2m");
            var apparentTemperature = RequireFinite(current.ApparentTemperature, "apparent_temperature");
            var humidity = current.Humidity
                           ?? throw new JsonException("The relative_humidity_2m value was missing.");
            var precipitation = RequireFinite(current.Precipitation, "precipitation");
            var windSpeed = RequireFinite(current.WindSpeed, "wind_speed_10m");
            var windDirection = RequireFinite(current.WindDirection, "wind_direction_10m");
            var weatherCode = current.WeatherCode
                              ?? throw new JsonException("The weather_code value was missing.");

            if (humidity is < 0 or > 100 || precipitation < 0 || windSpeed < 0 ||
                windDirection is < 0 or > 360)
            {
                throw new JsonException("The current weather response contained an out-of-range value.");
            }

            var weather = DescribeWeather(weatherCode);
            var displayLocation = FormatLocation(location);

            return $"{T("weather_current")}: {displayLocation}{Environment.NewLine}" +
                   $"{T("weather_conditions")}: {weather}{Environment.NewLine}" +
                   $"{T("weather_temperature")}: {FormatNumber(temperature)} °C{Environment.NewLine}" +
                   $"{T("weather_feels_like")}: {FormatNumber(apparentTemperature)} °C{Environment.NewLine}" +
                   $"{T("weather_humidity")}: {humidity.ToString(CultureInfo.InvariantCulture)}%{Environment.NewLine}" +
                   $"{T("weather_precipitation")}: {FormatNumber(precipitation)} mm{Environment.NewLine}" +
                   $"{T("weather_wind")}: {FormatNumber(windSpeed)} km/h, {FormatWindDirection(windDirection)}";
        }

        private static double RequireFinite(double? value, string fieldName)
        {
            if (value is not double number || !double.IsFinite(number))
                throw new JsonException($"The {fieldName} value was missing or invalid.");

            return number;
        }

        private string FormatForecast(Location location, ForecastResponse forecast)
        {
            var daily = forecast.Daily
                        ?? throw new JsonException("The daily forecast section was missing.");
            if (daily.Dates == null || daily.WeatherCodes == null ||
                daily.TemperatureMax == null || daily.TemperatureMin == null)
            {
                throw new JsonException("The daily forecast was incomplete.");
            }

            var dayCount = Math.Min(
                4,
                Math.Min(
                    Math.Min(daily.Dates.Length, daily.WeatherCodes.Length),
                    Math.Min(daily.TemperatureMax.Length, daily.TemperatureMin.Length)));
            if (dayCount == 0)
                throw new JsonException("The daily forecast was empty.");

            var lines = new string[dayCount];
            for (var index = 0; index < dayCount; index++)
            {
                var date = DateOnly.ParseExact(daily.Dates[index], "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var minimum = RequireFinite(daily.TemperatureMin[index], "temperature_2m_min");
                var maximum = RequireFinite(daily.TemperatureMax[index], "temperature_2m_max");
                if (minimum > maximum)
                    throw new JsonException("The daily forecast minimum temperature exceeded its maximum.");

                var precipitation = string.Empty;
                if (daily.PrecipitationProbabilityMax is { Length: > 0 } &&
                    index < daily.PrecipitationProbabilityMax.Length)
                {
                    var probability = daily.PrecipitationProbabilityMax[index];
                    if (probability is < 0 or > 100)
                        throw new JsonException("The precipitation probability was out of range.");

                    precipitation = $"{T("weather_precipitation_chance")}{probability}%";
                }

                var description = DescribeWeather(daily.WeatherCodes[index]);
                lines[index] = $"{FormatDate(date)}: {description}, {FormatNumber(minimum)}–{FormatNumber(maximum)} °C{precipitation}";
            }

            return $"{T("weather_forecast_title")}: {FormatLocation(location)}{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
        }

        private string Language => _core?.Settings.Language ?? "en";

        private string GeocodingLanguage => Language switch
        {
            "vi" => "vi",
            "ja" => "ja",
            "zh-hans" => "zh",
            "zh-hant" => "zh",
            _ => "en"
        };

        private string T(string key, params object[] arguments) =>
            _core?.Localize(key, arguments) ?? key;

        private string FormatDate(DateOnly date) => Language switch
        {
            "vi" => date.ToString("dd/MM", CultureInfo.InvariantCulture),
            "ja" or "zh-hans" or "zh-hant" => date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            _ => date.ToString("ddd, MMM d", CultureInfo.InvariantCulture)
        };

        private static string FormatLocation(Location location)
        {
            var parts = new[] { location.Name, location.Admin2, location.Admin1, location.Country };
            return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Distinct());
        }

        private static string FormatNumber(double value) =>
            value.ToString("0.#", CultureInfo.InvariantCulture);

        private static string FormatWindDirection(double degrees)
        {
            var index = (int)Math.Round(degrees / 22.5, MidpointRounding.AwayFromZero) % WindDirections.Length;
            return $"{WindDirections[index]} ({degrees.ToString("0", CultureInfo.InvariantCulture)}°)";
        }

        private string DescribeWeather(int code) => code switch
        {
            0 => T("weather_condition_clear"),
            1 => T("weather_condition_mainly_clear"),
            2 => T("weather_condition_partly_cloudy"),
            3 => T("weather_condition_overcast"),
            45 or 48 => T("weather_condition_fog"),
            51 or 53 or 55 => T("weather_condition_drizzle"),
            56 or 57 => T("weather_condition_freezing_drizzle"),
            61 or 63 or 65 => T("weather_condition_rain"),
            66 or 67 => T("weather_condition_freezing_rain"),
            71 or 73 or 75 or 77 => T("weather_condition_snow"),
            80 or 81 or 82 => T("weather_condition_rain_showers"),
            85 or 86 => T("weather_condition_snow_showers"),
            95 => T("weather_condition_thunderstorm"),
            96 or 99 => T("weather_condition_thunderstorm_hail"),
            _ => T("weather_condition_unknown")
        };

        public override void Unload()
        {
            _core?.Log("Weather Plugin Unloaded.");
        }

        private sealed class GeocodingResponse
        {
            [JsonPropertyName("results")]
            public Location[]? Results { get; init; }
        }

        private sealed class Location
        {
            [JsonPropertyName("name")]
            public string Name { get; init; } = string.Empty;

            [JsonPropertyName("country")]
            public string? Country { get; init; }

            [JsonPropertyName("admin1")]
            public string? Admin1 { get; init; }

            [JsonPropertyName("admin2")]
            public string? Admin2 { get; init; }

            [JsonPropertyName("latitude")]
            public double Latitude { get; init; }

            [JsonPropertyName("longitude")]
            public double Longitude { get; init; }
        }

        private sealed class ForecastResponse
        {
            [JsonPropertyName("current")]
            public CurrentWeather? Current { get; init; }

            [JsonPropertyName("daily")]
            public DailyWeather? Daily { get; init; }
        }

        private sealed class CurrentWeather
        {
            [JsonPropertyName("temperature_2m")]
            public double? Temperature { get; init; }

            [JsonPropertyName("relative_humidity_2m")]
            public int? Humidity { get; init; }

            [JsonPropertyName("apparent_temperature")]
            public double? ApparentTemperature { get; init; }

            [JsonPropertyName("precipitation")]
            public double? Precipitation { get; init; }

            [JsonPropertyName("weather_code")]
            public int? WeatherCode { get; init; }

            [JsonPropertyName("wind_speed_10m")]
            public double? WindSpeed { get; init; }

            [JsonPropertyName("wind_direction_10m")]
            public double? WindDirection { get; init; }
        }

        private sealed class DailyWeather
        {
            [JsonPropertyName("time")]
            public string[]? Dates { get; init; }

            [JsonPropertyName("weather_code")]
            public int[]? WeatherCodes { get; init; }

            [JsonPropertyName("temperature_2m_max")]
            public double[]? TemperatureMax { get; init; }

            [JsonPropertyName("temperature_2m_min")]
            public double[]? TemperatureMin { get; init; }

            [JsonPropertyName("precipitation_probability_max")]
            public int[]? PrecipitationProbabilityMax { get; init; }
        }
    }
}
