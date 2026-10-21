using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VpetCX.Core
{
    public sealed class VpetCXCore : MainPlugin
    {
        private CoreLoader? _core;
        private LocalApi? _api;
        private PluginManagerWindow? _pluginManagerWindow;
        private ReminderScheduleWindow? _reminderWindow;
        private DiaryWindow? _diaryWindow;
        private WeatherWindow? _weatherWindow;

        public override string PluginName => "VpetCX";

        public VpetCXCore(IMainWindow mainWindow) : base(mainWindow)
        {
        }

        public override void LoadPlugin()
        {
            if (_core != null)
                return;

            var pluginDirectory = Path.GetDirectoryName(typeof(VpetCXCore).Assembly.Location);
            if (string.IsNullOrWhiteSpace(pluginDirectory))
                throw new InvalidOperationException("Could not determine the VpetCX mod plugin directory.");

            _core = new CoreLoader(pluginDirectory, CultureInfo.CurrentUICulture.Name);
            _core.LoadPlugins();
        }

        public override void GameLoaded()
        {
            var core = _core;
            if (core == null)
                return;

            if (_api == null)
            {
                _api = new LocalApi(
                    MW,
                    core.Log,
                    core,
                    OpenManager,
                    OpenWeatherWindow,
                    OpenDiaryWindow,
                    OpenReminderWindow);
                try
                {
                    _api.Start();
                    core.Log($"Local HTTP API Gateway listening on http://{LocalApi.Address}/.");
                }
                catch (Exception ex) when (ex is System.Net.HttpListenerException or InvalidOperationException)
                {
                    core.Log($"Could not start the local API Gateway: {ex}");
                    _api = null;
                    return;
                }

                try
                {
                    MW.Dispatcher.Invoke(() =>
                        MW.Main.LabelDisplayShow(core.Localize("api_started", LocalApi.Address), 4000));
                }
                catch (InvalidOperationException ex)
                {
                    core.Log($"Could not display the local API startup notification: {ex.Message}");
                }
            }

        }

        public override void Setting() => RequestManager();

        public override void LoadDIY()
        {
            var core = _core;
            if (core == null)
                return;

            core.Localization.SetLanguage(core.Settings.Language);

            if (IsPluginEnabled("diary"))
            {
                MW.Main.ToolBar.AddMenuButton(
                    ToolBar.MenuType.DIY,
                    core.Localization.Get("menu_diary"),
                    () => RequestWindow("open_diary"));
            }

            if (IsPluginEnabled("weather"))
            {
                MW.Main.ToolBar.AddMenuButton(
                    ToolBar.MenuType.DIY,
                    core.Localization.Get("menu_weather"),
                    () => RequestWindow("open_weather"));
            }

            if (IsPluginEnabled("reminder"))
            {
                MW.Main.ToolBar.AddMenuButton(
                    ToolBar.MenuType.DIY,
                    core.Localization.Get("menu_reminder"),
                    () => RequestWindow("open_reminders"));
            }
        }

        private void OpenManager()
        {
            var core = _core;
            if (core == null)
                return;

            if (_pluginManagerWindow is { IsVisible: true })
            {
                _pluginManagerWindow.Activate();
                return;
            }

            _pluginManagerWindow = new PluginManagerWindow(core, core.Localization);
            _pluginManagerWindow.Closed += (_, _) => _pluginManagerWindow = null;
            _pluginManagerWindow.Show();
        }

        private void OpenReminderWindow()
        {
            if (!IsPluginEnabled("reminder"))
                return;

            if (_reminderWindow is { IsVisible: true })
            {
                _reminderWindow.Activate();
                return;
            }

            var core = _core;
            if (core == null)
                return;

            _reminderWindow = new ReminderScheduleWindow(core);
            _reminderWindow.Closed += (_, _) => _reminderWindow = null;
            _reminderWindow.Show();
        }

        private void OpenDiaryWindow()
        {
            if (!IsPluginEnabled("diary"))
                return;

            if (_diaryWindow is { IsVisible: true })
            {
                _diaryWindow.Activate();
                return;
            }

            var core = _core;
            if (core == null)
                return;

            _diaryWindow = new DiaryWindow(core);
            _diaryWindow.Closed += (_, _) => _diaryWindow = null;
            _diaryWindow.Show();
        }

        private void OpenWeatherWindow()
        {
            if (!IsPluginEnabled("weather"))
                return;

            if (_weatherWindow is { IsVisible: true })
            {
                _weatherWindow.Activate();
                return;
            }

            var core = _core;
            if (core == null)
                return;

            _weatherWindow = new WeatherWindow(core);
            _weatherWindow.Closed += (_, _) => _weatherWindow = null;
            _weatherWindow.Show();
        }

        private bool IsPluginEnabled(string name) =>
            _core?.GetPlugins().Any(plugin =>
                plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && plugin.Enabled) == true;

        private void RequestManager() => RequestWindow("open_manager");

        private void RequestWindow(string endpoint) => _ = RequestWindowAsync(endpoint);

        private async Task RequestWindowAsync(string endpoint)
        {
            try
            {
                await LocalApi.PostAsync<object, LocalApiResponse>(endpoint, new { });
            }
            catch (HttpRequestException ex)
            {
                _core?.Log($"Could not open a window through local API '{endpoint}': {ex}");
            }
            catch (TaskCanceledException ex)
            {
                _core?.Log($"Local API request timed out while opening '{endpoint}': {ex}");
            }
        }

        public override void EndGame()
        {
            _api?.Stop();
            _api = null;
            _pluginManagerWindow?.Close();
            _pluginManagerWindow = null;
            _reminderWindow?.Close();
            _reminderWindow = null;
            _diaryWindow?.Close();
            _diaryWindow = null;
            _weatherWindow?.Close();
            _weatherWindow = null;
            _core?.UnloadAllPlugins();
            _core = null;
        }
    }
}
