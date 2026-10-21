using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpetCX.Abstractions;
using VpetCX.Abstractions.Plugins;

public sealed record ReminderInfo(string Id, DateTimeOffset DueAt, string Message, bool IsDelivered);

public class ReminderPlugin : VPetPlugin
{
    private const long MaximumReminderFileBytes = 10 * 1024 * 1024;
    private static readonly HttpClient SpeechClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly Uri SpeechEndpoint = new("http://127.0.0.1:52815/say");
    private readonly CancellationTokenSource _unloadCancellation = new();
    private readonly Dictionary<string, ReminderStorageEntry> _scheduledReminders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _scheduledTasks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scheduleLock = new();
    private IReadOnlyList<ReminderInfo> _reminderSnapshot = Array.Empty<ReminderInfo>();
    private string? _reminderFilePath;
    private PluginContext? _core;

    public override string Name => "reminder";
    public override string Author => "VpetCX";
    public override string Description =>
        _core?.Localize("plugin_description_reminder") ??
        "Set countdown reminders in seconds, minutes, or hours. VPet speaks when each timer expires.";

    public override string Examples =>
        "reminder time(10), unit(seconds), event(\"check the oven\") | reminder action(list) | reminder action(cancel), id(abc12345)";

    public override string Parameters =>
        "action(string: set/list/cancel, optional), time(int, required for set), unit(string, optional: seconds/minutes/hours), event(string, required for set), id(string, required for cancel)";

    public override void Initialize(PluginContext core)
    {
        _core = core;
        _reminderFilePath = VpetCXDataPaths.GetDataFilePath("Reminder", "daily-reminders.json");
        LoadReminders();
        _core.Log("Reminder Plugin Initialized.");
    }

    public IReadOnlyList<ReminderInfo> GetReminders()
    {
        lock (_scheduleLock)
            return _reminderSnapshot;
    }

    public bool SaveReminder(
        string? id,
        TimeSpan delay,
        string message,
        out ReminderInfo? savedReminder,
        out string? error)
    {
        savedReminder = null;
        error = null;
        if (delay < TimeSpan.FromSeconds(1) || delay > TimeSpan.FromDays(365))
        {
            error = T("reminder_invalid_duration");
            return false;
        }

        var trimmedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedMessage) || trimmedMessage.Length > 500)
        {
            error = T("reminder_invalid_message");
            return false;
        }

        lock (_scheduleLock)
        {
            if (_unloadCancellation.IsCancellationRequested)
            {
                error = T("reminder_plugin_stopping");
                return false;
            }

            var isUpdate = !string.IsNullOrWhiteSpace(id);
            ReminderStorageEntry? previous = null;
            if (isUpdate && !_scheduledReminders.TryGetValue(id!, out previous))
            {
                error = T("reminder_not_found");
                return false;
            }

            if (!isUpdate && _scheduledReminders.Count >= 100)
            {
                error = T("reminder_limit_reached");
                return false;
            }

            var reminderId = isUpdate ? id! : Guid.NewGuid().ToString("N")[..8];
            var reminder = new ReminderStorageEntry(
                reminderId,
                DateTimeOffset.Now.Add(delay),
                trimmedMessage)
            {
                IsDelivered = false
            };
            _scheduledReminders[reminderId] = reminder;
            if (!TrySaveReminders(out error))
            {
                if (previous == null)
                    _scheduledReminders.Remove(reminderId);
                else
                    _scheduledReminders[reminderId] = previous;
                return false;
            }

            RefreshReminderSnapshotLocked();
            if (_scheduledTasks.TryGetValue(reminderId, out var previousTask))
                previousTask.Cancel();
            StartReminderLocked(reminder);
            savedReminder = ToInfo(reminder);
            return true;
        }
    }

    public bool DeleteReminder(string id, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            error = T("reminder_error_cancel_id");
            return false;
        }

        lock (_scheduleLock)
        {
            if (!_scheduledReminders.Remove(id, out var removed))
            {
                error = T("reminder_not_found");
                return false;
            }

            if (!TrySaveReminders(out error))
            {
                _scheduledReminders.Add(removed.Id, removed);
                return false;
            }

            RefreshReminderSnapshotLocked();
            if (_scheduledTasks.TryGetValue(id, out var task))
                task.Cancel();

            return true;
        }
    }

    private void LoadReminders()
    {
        var path = _reminderFilePath;
        if (path == null || !File.Exists(path))
            return;

        try
        {
            if (new FileInfo(path).Length > MaximumReminderFileBytes)
                throw new JsonException("The reminder file exceeds the supported size limit.");

            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize<List<ReminderStorageEntry?>>(json) ?? new();
            var hasLegacyReminders = stored.OfType<ReminderStorageEntry>()
                .Any(reminder => reminder.DueAt == default && reminder.Time != null);
            var validReminders = stored.OfType<ReminderStorageEntry>()
                .Select(MigrateLegacyReminder)
                .Where(reminder => reminder != null)
                .Cast<ReminderStorageEntry>()
                .Where(IsValidStoredReminder)
                .GroupBy(reminder => reminder.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(100)
                .ToArray();
            if (validReminders.Length != stored.Count)
                _core?.Log($"Skipped {stored.Count - validReminders.Length} invalid or excess reminder(s) in '{path}'.");

            lock (_scheduleLock)
            {
                var hasDeliveredReminders = false;
                foreach (var reminder in validReminders)
                {
                    if (reminder.IsDelivered)
                    {
                        reminder.IsDelivered = false;
                        hasDeliveredReminders = true;
                    }

                    _scheduledReminders[reminder.Id] = reminder;
                }

                RefreshReminderSnapshotLocked();
                if (hasLegacyReminders || hasDeliveredReminders)
                {
                    if (!TrySaveReminders(out var saveError))
                        _core?.Log($"Could not persist reminder startup state: {saveError}");
                }

                foreach (var reminder in validReminders)
                    StartReminderLocked(reminder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _core?.Log($"Could not load reminders from '{path}': {ex}");
        }
    }

    private bool TrySaveReminders(out string? error)
    {
        error = null;
        var path = _reminderFilePath;
        if (path == null)
        {
            error = T("reminder_storage_unavailable");
            return false;
        }

        var temporaryPath = path + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new IOException("Could not determine the reminder storage directory.");

            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(_scheduledReminders.Values.OrderBy(
                reminder => reminder.DueAt));
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _core?.Log($"Could not save reminders to '{path}': {ex}");
            error = T("reminder_save_failed", ex.Message);
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
                _core?.Log($"Could not remove temporary reminder file '{temporaryPath}': {cleanupException}");
            }

            return false;
        }
    }

    private void RefreshReminderSnapshotLocked()
    {
        _reminderSnapshot = _scheduledReminders.Values
            .OrderBy(reminder => reminder.DueAt)
            .Select(ToInfo)
            .ToArray();
    }

    private void StartReminderLocked(ReminderStorageEntry reminder)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_unloadCancellation.Token);
        _scheduledTasks[reminder.Id] = cancellation;
        _ = ScheduledReminderTaskAsync(reminder, cancellation);
    }

    private async Task ScheduledReminderTaskAsync(
        ReminderStorageEntry reminder,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var remaining = reminder.DueAt - DateTimeOffset.Now;
                if (remaining > TimeSpan.Zero)
                {
                    var wait = remaining > TimeSpan.FromDays(1)
                        ? TimeSpan.FromDays(1)
                        : remaining;
                    await Task.Delay(wait, cancellation.Token);
                    continue;
                }

                try
                {
                    var spokenMessage = _core?.Localize("reminder_timer_speech", reminder.Message)
                                        ?? $"It's time to: \"{reminder.Message}\"";

                    using var response = await SpeechClient.PostAsJsonAsync(
                        SpeechEndpoint,
                        new { text = spokenMessage },
                        cancellation.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        _core?.Log($"Reminder {reminder.Id} speech endpoint returned HTTP {(int)response.StatusCode} ({response.StatusCode}); retrying in 15 seconds.");
                        await Task.Delay(TimeSpan.FromSeconds(15), cancellation.Token);
                        continue;
                    }

                    lock (_scheduleLock)
                    {
                        reminder.IsDelivered = true;
                        RefreshReminderSnapshotLocked();
                        if (!TrySaveReminders(out var saveError))
                            _core?.Log($"Reminder {reminder.Id} was spoken but its delivered state could not be saved: {saveError}");
                    }
                    _core?.Log($"Reminder {reminder.Id} was delivered to the VPet speech endpoint.");
                    return;
                }
                catch (HttpRequestException ex)
                {
                    _core?.Log($"Reminder {reminder.Id} could not reach the VPet speech endpoint; retrying in 15 seconds: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellation.Token);
                }
                catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
                {
                    _core?.Log($"Reminder {reminder.Id} speech request timed out; retrying in 15 seconds: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _core?.Log($"Reminder {reminder.Id} failed: {ex}");
        }
        finally
        {
            lock (_scheduleLock)
            {
                if (_scheduledTasks.TryGetValue(reminder.Id, out var current) && current == cancellation)
                    _scheduledTasks.Remove(reminder.Id);
            }

            cancellation.Dispose();
        }
    }

    private static bool IsValidStoredReminder(ReminderStorageEntry reminder) =>
        !string.IsNullOrWhiteSpace(reminder.Id) &&
        reminder.DueAt != default &&
        !string.IsNullOrWhiteSpace(reminder.Message) &&
        reminder.Message.Length <= 500;

    private static ReminderStorageEntry? MigrateLegacyReminder(ReminderStorageEntry reminder)
    {
        if (reminder.DueAt != default || !TryParseLegacyTime(reminder.Time, out var legacyTime))
            return reminder;

        var dueAt = new DateTimeOffset(DateTime.Today.Add(legacyTime.ToTimeSpan()));
        if (dueAt <= DateTimeOffset.Now)
            dueAt = dueAt.AddDays(1);
        reminder.DueAt = dueAt;
        reminder.Time = null;
        return reminder;
    }

    private static bool TryParseLegacyTime(string? time, out TimeOnly parsedTime) =>
        TimeOnly.TryParseExact(
            time,
            new[] { "HH:mm:ss", "HH:mm" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsedTime);

    private static ReminderInfo ToInfo(ReminderStorageEntry reminder) =>
        new(
            reminder.Id,
            reminder.DueAt,
            reminder.Message,
            reminder.IsDelivered);

    public override Task<string> Function(string arguments)
    {
        var action = PluginArguments.TryGetValue(arguments, "action", out var actionValue)
            ? actionValue.Trim().ToLowerInvariant()
            : "set";

        return action switch
        {
            "set" => Task.FromResult(SetReminder(arguments)),
            "list" => Task.FromResult(ListReminders()),
            "cancel" => Task.FromResult(CancelReminder(arguments)),
            "api_list" => Task.FromResult(JsonSerializer.Serialize(GetReminders())),
            "api_save" => Task.FromResult(SaveApiReminder(arguments)),
            "api_delete" => Task.FromResult(DeleteApiReminder(arguments)),
            _ => Task.FromResult(T("reminder_unknown_action"))
        };
    }

    private string SaveApiReminder(string arguments)
    {
        if (!PluginArguments.TryGetValue(arguments, "time", out var timeText) ||
            !int.TryParse(timeText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
            amount <= 0)
        {
            return JsonSerializer.Serialize(new { success = false, error = T("reminder_invalid_duration") });
        }

        var unit = PluginArguments.TryGetValue(arguments, "unit", out var unitText)
            ? unitText.Trim().ToLowerInvariant()
            : string.Empty;
        if (unit is not ("seconds" or "minutes" or "hours"))
            return JsonSerializer.Serialize(new { success = false, error = T("reminder_invalid_duration") });

        var message = PluginArguments.TryGetValue(arguments, "event", out var eventText)
            ? eventText
            : string.Empty;
        var id = PluginArguments.TryGetValue(arguments, "id", out var idText)
            ? idText
            : null;
        TimeSpan delay;
        try
        {
            delay = unit switch
            {
                "seconds" => TimeSpan.FromSeconds(amount),
                "minutes" => TimeSpan.FromMinutes(amount),
                _ => TimeSpan.FromHours(amount)
            };
        }
        catch (OverflowException)
        {
            return JsonSerializer.Serialize(new { success = false, error = T("reminder_invalid_duration") });
        }

        if (!SaveReminder(id, delay, message, out var reminder, out var error))
            return JsonSerializer.Serialize(new { success = false, error });

        return JsonSerializer.Serialize(new { success = true, reminder });
    }

    private string DeleteApiReminder(string arguments)
    {
        if (!PluginArguments.TryGetValue(arguments, "id", out var id) ||
            string.IsNullOrWhiteSpace(id))
        {
            return JsonSerializer.Serialize(new { success = false, error = T("reminder_error_cancel_id") });
        }

        return DeleteReminder(id, out var error)
            ? JsonSerializer.Serialize(new { success = true })
            : JsonSerializer.Serialize(new { success = false, error });
    }

    private string SetReminder(string arguments)
    {
        if (_unloadCancellation.IsCancellationRequested)
            return T("reminder_plugin_stopping");
        if (!PluginArguments.TryGetValue(arguments, "time", out var timeText) ||
            !int.TryParse(
                timeText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timeValue) ||
            timeValue <= 0)
        {
            return T("reminder_error_time");
        }

        var unit = PluginArguments.TryGetValue(arguments, "unit", out var unitValue)
            ? unitValue.Trim().ToLowerInvariant()
            : "minutes";
        if (unit is not ("second" or "seconds" or "minute" or "minutes" or "hour" or "hours"))
            return T("reminder_error_unit");

        if (!PluginArguments.TryGetValue(arguments, "event", out var message) ||
            string.IsNullOrWhiteSpace(message) || message.Trim().Length > 500)
        {
            return T("reminder_error_event");
        }

        TimeSpan delay;
        try
        {
            delay = unit.StartsWith("hour", StringComparison.Ordinal)
                ? TimeSpan.FromHours(timeValue)
                : unit.StartsWith("minute", StringComparison.Ordinal)
                    ? TimeSpan.FromMinutes(timeValue)
                    : TimeSpan.FromSeconds(timeValue);
            if (delay > TimeSpan.FromDays(365))
                return T("reminder_error_delay_limit");
        }
        catch (OverflowException)
        {
            return T("reminder_error_delay_limit");
        }

        if (!SaveReminder(null, delay, message, out var reminder, out var error))
            return error ?? T("reminder_save_failed");

        return T("reminder_set", timeValue, unit, reminder!.Message, reminder.Id);
    }

    private string ListReminders()
    {
        var reminders = GetReminders();
        return reminders.Count == 0
            ? T("reminder_none_pending")
            : string.Join(Environment.NewLine, reminders.Select(reminder =>
            {
                var line = T(
                    "reminder_list_item",
                    reminder.Id,
                    reminder.DueAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    reminder.Message);
                return reminder.IsDelivered
                    ? $"{line} ({T("reminder_delivered")})"
                    : line;
            }));
    }

    private string CancelReminder(string arguments)
    {
        if (!PluginArguments.TryGetValue(arguments, "id", out var requestedId) ||
            string.IsNullOrWhiteSpace(requestedId))
        {
            return T("reminder_error_cancel_id");
        }

        var matches = GetReminders()
            .Where(reminder => reminder.Id.StartsWith(requestedId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            return T("reminder_cancel_not_found", requestedId);
        if (matches.Length > 1)
            return T("reminder_cancel_ambiguous");

        return DeleteReminder(matches[0].Id, out var error)
            ? T("reminder_cancelled", matches[0].Id)
            : error ?? T("reminder_cancel_not_found", requestedId);
    }

    private string T(string key, params object[] arguments) =>
        _core?.Localize(key, arguments) ?? key;

    public override void Unload()
    {
        _unloadCancellation.Cancel();
        lock (_scheduleLock)
        {
            foreach (var cancellation in _scheduledTasks.Values)
                cancellation.Cancel();
        }
        _core?.Log("Reminder Plugin Unloaded.");
    }

}

internal sealed class ReminderStorageEntry
{
    public ReminderStorageEntry()
    {
    }

    public ReminderStorageEntry(string id, DateTimeOffset dueAt, string message)
    {
        Id = id;
        DueAt = dueAt;
        Message = message;
    }

    public string Id { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Time { get; set; }
    public bool IsDelivered { get; set; }
}
