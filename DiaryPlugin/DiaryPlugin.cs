using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpetCX.Abstractions;
using VpetCX.Abstractions.Plugins;

namespace DiaryPlugin
{
    public class DiaryPlugin : VPetPlugin
    {
        private const int MaximumEntries = 2000;
        private const long MaximumDiaryFileBytes = 12 * 1024 * 1024;
        private static readonly SemaphoreSlim DiaryLock = new(1, 1);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private string _diaryFile = string.Empty;
        private PluginContext? _core;

        public override string Name => "diary";
        public override string Author => "VpetCX";

        public override string Description =>
            _core?.Localize("plugin_description_diary") ??
            "Save diary entries locally and view, search, or delete them.";

        public override string Parameters =>
            "action(string: view/write/search/delete), text(string, required for write/search), search(string, legacy alias for text), id(string, required for delete)";

        public override string Examples =>
            "diary action(write), text(\"A sample entry\") | diary action(search), text(\"sample\")";

        public override void Initialize(PluginContext core)
        {
            _core = core;
            _diaryFile = VpetCXDataPaths.GetDataFilePath("Diary", "diary.json");
            _core.Log($"Diary Plugin Initialized. Entries are stored at '{_diaryFile}'.");
        }

        public override async Task<string> Function(string arguments)
        {
            if (_core == null)
                return _core?.Localize("plugin_error_not_initialized") ?? "Plugin is not initialized.";

            if (!PluginArguments.TryGetValue(arguments, "action", out var actionText))
                actionText = PluginArguments.TryGetValue(arguments, "search", out _) ? "search" : "view";

            var action = actionText.Trim().ToLowerInvariant();
            if (action == "setting")
                return T("diary_storage_location", _diaryFile);

            if (action is not ("view" or "list" or "write" or "search" or "delete"))
                return T("diary_unknown_action", action);

            await DiaryLock.WaitAsync();
            try
            {
                var entries = await ReadEntriesAsync();
                return action switch
                {
                    "view" or "list" => FormatEntries(entries.OrderByDescending(entry => entry.CreatedAt).Take(10)),
                    "write" => await WriteEntryAsync(entries, arguments),
                    "search" => SearchEntries(entries, arguments),
                    _ => await DeleteEntryAsync(entries, arguments)
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _core.Log($"Diary operation failed: {ex}");
                return T("diary_operation_failed", ex.Message);
            }
            finally
            {
                DiaryLock.Release();
            }
        }

        private async Task<DiaryEntry[]> ReadEntriesAsync()
        {
            if (!File.Exists(_diaryFile))
                return Array.Empty<DiaryEntry>();
            if (new FileInfo(_diaryFile).Length > MaximumDiaryFileBytes)
                throw new JsonException("The diary file exceeds the supported size limit.");

            await using var stream = new FileStream(
                _diaryFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var entries = await JsonSerializer.DeserializeAsync<DiaryEntry[]>(stream) ?? Array.Empty<DiaryEntry>();
            if (entries.Length > MaximumEntries)
                throw new JsonException($"The diary contains more than {MaximumEntries} entries.");
            if (entries.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Text == null))
                throw new JsonException("The diary file contains an incomplete entry.");

            return entries;
        }

        private async Task<string> WriteEntryAsync(DiaryEntry[] entries, string arguments)
        {
            if (!PluginArguments.TryGetValue(arguments, "text", out var text) ||
                string.IsNullOrWhiteSpace(text) || text.Length > 4000)
            {
                return T("diary_error_write_text");
            }
            if (entries.Length >= MaximumEntries)
                return T("diary_limit_reached", MaximumEntries);

            var entry = new DiaryEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.Now,
                Text = text.Trim()
            };
            await SaveEntriesAsync(entries.Append(entry).ToArray());
            return T("diary_saved", entry.CreatedAt, entry.Id[..8]);
        }

        private string SearchEntries(DiaryEntry[] entries, string arguments)
        {
            if ((!PluginArguments.TryGetValue(arguments, "text", out var keyword) &&
                 !PluginArguments.TryGetValue(arguments, "search", out keyword)) ||
                string.IsNullOrWhiteSpace(keyword))
            {
                return T("diary_error_search_text");
            }

            var matches = entries
                .Where(entry => entry.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.CreatedAt)
                .Take(10)
                .ToArray();
            return matches.Length == 0
                ? T("diary_search_no_results", keyword)
                : FormatEntries(matches);
        }

        private async Task<string> DeleteEntryAsync(DiaryEntry[] entries, string arguments)
        {
            if (!PluginArguments.TryGetValue(arguments, "id", out var id) || string.IsNullOrWhiteSpace(id))
                return T("diary_error_delete_id");

            var matchingEntries = entries
                .Where(entry => entry.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingEntries.Length == 0)
                return T("diary_delete_not_found", id);
            if (matchingEntries.Length > 1)
                return T("diary_delete_ambiguous");

            await SaveEntriesAsync(entries.Where(entry => !ReferenceEquals(entry, matchingEntries[0])).ToArray());
            return T("diary_deleted", matchingEntries[0].Id[..Math.Min(8, matchingEntries[0].Id.Length)]);
        }

        private async Task SaveEntriesAsync(DiaryEntry[] entries)
        {
            var directory = Path.GetDirectoryName(_diaryFile)
                           ?? throw new InvalidOperationException("Could not resolve the diary directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = $"{_diaryFile}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 useAsync: true))
                    await JsonSerializer.SerializeAsync(stream, entries, JsonOptions);

                File.Move(temporaryPath, _diaryFile, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private string FormatEntries(System.Collections.Generic.IEnumerable<DiaryEntry> entries)
        {
            var formatted = entries
                .Select(entry => _core?.Localize(
                    "diary_entry_line",
                    entry.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    entry.Id[..Math.Min(8, entry.Id.Length)],
                    entry.Text)
                    ?? $"[{entry.CreatedAt:yyyy-MM-dd HH:mm}] {entry.Id[..Math.Min(8, entry.Id.Length)]}: {entry.Text}")
                .ToArray();
            return formatted.Length == 0
                ? T("diary_empty")
                : string.Join(Environment.NewLine, formatted);
        }

        private string T(string key, params object[] arguments) =>
            _core?.Localize(key, arguments) ?? key;

        public override void Unload()
        {
            _core?.Log("Diary Plugin Unloaded.");
        }

        private sealed class DiaryEntry
        {
            public string Id { get; set; } = "";
            public DateTimeOffset CreatedAt { get; set; }
            public string Text { get; set; } = "";
        }
    }
}
