using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LinePutScript;
using LiteDB;
using VpetCX.Abstractions;
using VpetCX.Abstractions.Plugins;

namespace VpetCX.Core
{
    /// <summary>
    /// CoreLoader - Tự động load và quản lý plugin
    /// CoreLoader - Automatically load and manage plugins
    /// </summary>
    public class CoreLoader : PluginContext
    {
        private static readonly HashSet<string> RemovedPluginNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "pixiv",
            "system_info"
        };

        private readonly List<VPetPlugin> _plugins = new();
        private readonly string _pluginsDirectory;
        private readonly string _settingsDatabasePath;
        private readonly VpetCXLocalization _localization;
        private bool _pluginsLoaded;

        public override PluginSettings Settings { get; protected set; } = new PluginSettings();
        internal VpetCXLocalization Localization => _localization;

        public CoreLoader(string pluginsDirectory, string? defaultLanguage = null)
        {
            _pluginsDirectory = pluginsDirectory;
            _settingsDatabasePath = VpetCXDataPaths.GetDataFilePath("settings.db");
            _localization = new VpetCXLocalization(
                Path.Combine(_pluginsDirectory, "language"),
                "en",
                Log);
            LoadSettings(defaultLanguage);
            _localization.SetLanguage(Settings.Language);
        }

        public override string Localize(string key, params object[] arguments) =>
            _localization.Get(key, arguments);

        /// <summary>
        /// Load tất cả plugin từ thư mục
        /// Load all plugins from directory
        /// </summary>
        public void LoadPlugins()
        {
            if (_pluginsLoaded)
                return;

            if (!Directory.Exists(_pluginsDirectory))
            {
                Log($"Plugin directory not found: {_pluginsDirectory}");
                return;
            }

            string[] dllFiles;
            try
            {
                dllFiles = Directory.GetFiles(_pluginsDirectory, "*.dll")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log($"Could not enumerate plugins in '{_pluginsDirectory}': {ex.Message}");
                return;
            }

            _pluginsLoaded = true;
            Log($"Found {dllFiles.Length} plugin DLL(s) in {_pluginsDirectory}");

            foreach (var dllFile in dllFiles)
            {
                var fileName = Path.GetFileName(dllFile);
                try
                {
                    try
                    {
                        AssemblyName.GetAssemblyName(dllFile);
                    }
                    catch (BadImageFormatException)
                    {
                        Log($"Skipped native library '{fileName}'.");
                        continue;
                    }

                    var assembly = Assembly.LoadFrom(dllFile);
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.OfType<Type>().ToArray();
                        foreach (var loaderException in ex.LoaderExceptions.OfType<Exception>())
                            Log($"Could not load a type from {Path.GetFileName(dllFile)}: {loaderException.Message}");
                    }

                    var pluginTypes = types
                        .Where(t => typeof(VPetPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var pluginType in pluginTypes)
                    {
                        VPetPlugin? plugin = null;
                        try
                        {
                            plugin = (VPetPlugin?)Activator.CreateInstance(pluginType);
                            if (plugin == null)
                            {
                                Log($"Failed to instantiate plugin {pluginType.Name}: Constructor returned null");
                                continue;
                            }

                            if (RemovedPluginNames.Contains(plugin.Name))
                            {
                                Log($"Ignored removed plugin '{plugin.Name}' from {Path.GetFileName(dllFile)}.");
                                continue;
                            }

                            if (_plugins.Any(existing =>
                                    existing.Name.Equals(plugin.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                Log($"Skipped duplicate plugin name '{plugin.Name}' from {Path.GetFileName(dllFile)}.");
                                continue;
                            }

                            plugin.Enabled = !Settings.DisabledPlugins.Contains(plugin.Name);
                            if (plugin.Enabled)
                                plugin.Initialize(this);

                            _plugins.Add(plugin);
                            Log($"{(plugin.Enabled ? "Loaded" : "Registered disabled")} plugin: {plugin.Name} - {plugin.Author}");
                        }
                        catch (Exception ex)
                        {
                            if (plugin != null)
                            {
                                try
                                {
                                    plugin.Unload();
                                }
                                catch (Exception unloadException)
                                {
                                    Log($"Failed to clean up plugin {pluginType.Name}: {unloadException.Message}");
                                }

                            }

                            Log($"Failed to instantiate plugin {pluginType.Name}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Failed to load assembly {Path.GetFileName(dllFile)}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Run a plugin action requested through a typed local API endpoint.
        /// </summary>
        internal async Task<string> ExecutePluginAction(string pluginName, string arguments)
        {
            var matchingPlugin = _plugins.FirstOrDefault(plugin =>
                plugin.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase));

            if (matchingPlugin == null)
                throw new InvalidOperationException($"Plugin '{pluginName}' is not loaded.");

            if (!matchingPlugin.Enabled)
                throw new InvalidOperationException($"Plugin '{pluginName}' is disabled.");

            try
            {
                return await matchingPlugin.Function(arguments);
            }
            catch (Exception ex)
            {
                Log($"Error executing HTTP action for plugin '{pluginName}': {ex}");
                throw;
            }
        }

        /// <summary>
        /// Unload tất cả plugin
        /// Unload all plugins
        /// </summary>
        public void UnloadAllPlugins()
        {
            foreach (var plugin in _plugins)
            {
                try
                {
                    plugin.Unload();
                }
                catch (Exception ex)
                {
                    Log($"Error unloading plugin {plugin.Name}: {ex.Message}");
                }
            }
            _plugins.Clear();
            _pluginsLoaded = false;
        }

        /// <summary>
        /// Lấy danh sách plugin
        /// Get plugin list
        /// </summary>
        public IEnumerable<VPetPlugin> GetPlugins()
        {
            return _plugins.AsReadOnly();
        }

        public bool IsPluginEnabledConfigured(string pluginName) =>
            !Settings.DisabledPlugins.Contains(pluginName, StringComparer.OrdinalIgnoreCase);

        public void SetPluginEnabled(string pluginName, bool enabled)
        {
            var plugin = _plugins.FirstOrDefault(candidate =>
                candidate.Name.Equals(pluginName, StringComparison.OrdinalIgnoreCase));
            if (plugin == null)
                throw new InvalidOperationException($"Plugin '{pluginName}' is not loaded.");

            var wasDisabled = Settings.DisabledPlugins.Contains(plugin.Name);
            if (enabled)
                Settings.DisabledPlugins.Remove(plugin.Name);
            else
                Settings.DisabledPlugins.Add(plugin.Name);

            try
            {
                SaveSettings();
            }
            catch
            {
                if (wasDisabled)
                    Settings.DisabledPlugins.Add(plugin.Name);
                else
                    Settings.DisabledPlugins.Remove(plugin.Name);
                throw;
            }
        }

        public void SetLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language) ||
                !_localization.IsLanguageAvailable(language))
                throw new ArgumentException($"Unsupported language '{language}'.", nameof(language));

            var previousLanguage = Settings.Language;
            Settings.Language = language;
            try
            {
                SaveSettings();
                _localization.SetLanguage(language);
            }
            catch
            {
                Settings.Language = previousLanguage;
                throw;
            }
        }

        private void LoadSettings(string? defaultLanguage)
        {
            try
            {
                using var database = OpenSettingsDatabase();
                var storedSettings = database
                    .GetCollection<StoredSettings>("settings")
                    .FindById(1);
                if (storedSettings != null)
                {
                    var storedLanguage = storedSettings.Language;
                    Settings = new PluginSettings
                    {
                        Language = !string.IsNullOrWhiteSpace(storedLanguage) &&
                                   _localization.IsLanguageAvailable(storedLanguage)
                            ? storedLanguage
                            : NormalizeDefaultLanguage(defaultLanguage),
                        DisabledPlugins = new HashSet<string>(
                            storedSettings.DisabledPlugins ?? new List<string>(),
                            StringComparer.OrdinalIgnoreCase)
                    };

                    return;
                }
            }
            catch (Exception ex) when (
                ex is LiteException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Log($"Could not load settings from '{_settingsDatabasePath}': {ex.Message}");
                throw new InvalidOperationException("VpetCX settings database could not be read.", ex);
            }

            Settings.Language = NormalizeDefaultLanguage(defaultLanguage);
            SaveSettings();
        }

        private void SaveSettings()
        {
            using var database = OpenSettingsDatabase();
            database.GetCollection<StoredSettings>("settings").Upsert(new StoredSettings
            {
                Id = 1,
                Language = Settings.Language,
                DisabledPlugins = Settings.DisabledPlugins
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        private LiteDatabase OpenSettingsDatabase()
        {
            var directory = Path.GetDirectoryName(_settingsDatabasePath)
                            ?? throw new InvalidOperationException("Could not determine the settings directory.");
            Directory.CreateDirectory(directory);
            return new LiteDatabase(_settingsDatabasePath);
        }

        private sealed class StoredSettings
        {
            public StoredSettings()
            {
            }

            public int Id { get; set; }
            public string Language { get; set; } = "en";
            public List<string>? DisabledPlugins { get; set; } = new();
        }

        private string NormalizeDefaultLanguage(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
                return GetFallbackLanguage();

            var normalized = language.Trim().Replace('_', '-').ToLowerInvariant();
            if (_localization.IsLanguageAvailable(normalized))
                return GetAvailableLanguage(normalized);

            try
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(normalized);
                var cultureName = culture.Name.ToLowerInvariant();
                if (_localization.IsLanguageAvailable(cultureName))
                    return GetAvailableLanguage(cultureName);

                if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                {
                    var traditional = cultureName.Contains("-hant", StringComparison.OrdinalIgnoreCase) ||
                                      cultureName.EndsWith("-tw", StringComparison.OrdinalIgnoreCase) ||
                                      cultureName.EndsWith("-hk", StringComparison.OrdinalIgnoreCase) ||
                                      cultureName.EndsWith("-mo", StringComparison.OrdinalIgnoreCase);
                    var chineseLanguage = traditional ? "zh-hant" : "zh-hans";
                    if (_localization.IsLanguageAvailable(chineseLanguage))
                        return GetAvailableLanguage(chineseLanguage);
                }

                var baseLanguage = culture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (_localization.IsLanguageAvailable(baseLanguage))
                    return GetAvailableLanguage(baseLanguage);
            }
            catch (System.Globalization.CultureNotFoundException)
            {
            }

            return GetFallbackLanguage();
        }

        private string GetAvailableLanguage(string language) =>
            _localization.GetAvailableLanguages()
                .FirstOrDefault(available =>
                    available.Equals(language, StringComparison.OrdinalIgnoreCase))
            ?? language;

        private string GetFallbackLanguage()
        {
            if (_localization.IsLanguageAvailable("en"))
                return GetAvailableLanguage("en");

            return _localization.GetAvailableLanguages().FirstOrDefault() ?? "en";
        }

        /// <summary>
        /// Ghi log
        /// Log message
        /// </summary>
        public override void Log(string message)
        {
            Console.WriteLine($"[VpetCX] {message}");
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpetcx_core.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[VpetCX] Could not write log file: {ex.Message}");
            }
        }
    }

    internal sealed class VpetCXLocalization
    {
        private readonly Dictionary<string, Dictionary<string, string>> _translations =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly string[] _availableLanguages;
        private readonly Action<string> _log;
        private string _language = "en";

        public VpetCXLocalization(string localizationDirectory, string? language, Action<string> log)
        {
            _log = log;
            _availableLanguages = LoadTranslations(localizationDirectory);
            SetLanguage(language);
        }

        public IReadOnlyList<string> GetAvailableLanguages() => _availableLanguages;

        public bool IsLanguageAvailable(string? language) =>
            language != null && _availableLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);

        public void SetLanguage(string? language)
        {
            _language = !string.IsNullOrWhiteSpace(language) && IsLanguageAvailable(language)
                ? _availableLanguages.First(available =>
                    available.Equals(language, StringComparison.OrdinalIgnoreCase))
                : IsLanguageAvailable("en")
                    ? _availableLanguages.First(available =>
                        available.Equals("en", StringComparison.OrdinalIgnoreCase))
                    : _availableLanguages.FirstOrDefault() ?? "en";
        }

        public string GetLanguageName(string language) =>
            _translations.TryGetValue("language_name", out var names) &&
            names.TryGetValue(language, out var name) &&
            !string.IsNullOrWhiteSpace(name)
                ? name
                : language;

        public string Get(string key, params object[] arguments)
        {
            var language = _language;
            if (!_translations.TryGetValue(key, out var values) ||
                (!values.TryGetValue(language, out var value) && !values.TryGetValue("en", out value)))
            {
                _log($"VpetCX localization key '{key}' is missing for language '{language}'.");
                value = key;
            }

            return arguments.Length == 0
                ? value
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, value, arguments);
        }

        private string[] LoadTranslations(string localizationDirectory)
        {
            if (!Directory.Exists(localizationDirectory))
            {
                _log($"Localization directory not found: '{localizationDirectory}'.");
                return Array.Empty<string>();
            }

            string[] languageDirectories;
            try
            {
                languageDirectories = Directory.GetDirectories(localizationDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log($"Could not enumerate localization directory '{localizationDirectory}': {ex}");
                return Array.Empty<string>();
            }

            var availableLanguages = new List<string>();
            foreach (var languageDirectory in languageDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var language = Path.GetFileName(languageDirectory);
                if (string.IsNullOrWhiteSpace(language))
                {
                    _log($"Skipped localization folder with an empty name: '{languageDirectory}'.");
                    continue;
                }

                string[] localizationFiles;
                try
                {
                    localizationFiles = Directory.GetFiles(languageDirectory, "*.lps");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log($"Could not enumerate localization files in '{languageDirectory}': {ex}");
                    continue;
                }

                if (localizationFiles.Length == 0)
                    continue;

                foreach (var localizationFile in localizationFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    LoadFile(localizationFile, language);

                availableLanguages.Add(language);
            }

            if (availableLanguages.Count == 0)
                _log($"No language resources were found in '{localizationDirectory}'.");
            else if (!availableLanguages.Contains("en", StringComparer.OrdinalIgnoreCase))
                _log($"English localization resources were not found in '{localizationDirectory}'.");

            return availableLanguages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void LoadFile(string path, string language)
        {
            try
            {
                var document = new LpsDocument(File.ReadAllText(path));
                foreach (var line in document.Assemblage)
                {
                    if (string.IsNullOrWhiteSpace(line.Name) || string.IsNullOrWhiteSpace(line.Info))
                    {
                        _log($"Skipped empty localization key or value in '{path}'.");
                        continue;
                    }

                    if (!_translations.TryGetValue(line.Name, out var values))
                    {
                        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        _translations.Add(line.Name, values);
                    }

                    if (!values.TryAdd(language, line.Info))
                        _log($"Duplicate localization key '{line.Name}' for language '{language}' in '{path}'.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _log($"Could not load localization file '{path}': {ex}");
            }
        }
    }
}
