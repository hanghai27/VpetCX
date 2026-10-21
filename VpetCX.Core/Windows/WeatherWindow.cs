using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VpetCX.Core
{
    internal sealed class WeatherWindow : Window
    {
        private static readonly Brush BackgroundBrush = ReminderScheduleWindow.Brush("#101722");
        private static readonly Brush PanelBrush = ReminderScheduleWindow.Brush("#182331");
        private static readonly Brush TextBrush = ReminderScheduleWindow.Brush("#E8EFF7");
        private static readonly Brush MutedBrush = ReminderScheduleWindow.Brush("#91A1B5");
        private static readonly Brush AccentBrush = ReminderScheduleWindow.Brush("#57D6B5");

        private readonly CoreLoader _core;
        private readonly TextBox _locationInput = new();
        private readonly ListBox _locationResults = new();
        private readonly TextBox _result = new();
        private readonly TextBlock _status = new();
        private readonly CheckBox _speakResult = new();
        private readonly Button _searchButton;
        private readonly Button _currentButton;
        private readonly Button _forecastButton;
        private bool _isBusy;

        public WeatherWindow(CoreLoader core)
        {
            _core = core;
            Title = core.Localize("weather_window_title");
            Width = 760;
            Height = 620;
            MinWidth = 600;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;

            _searchButton = CreateButton(core.Localize("weather_window_search"), true);
            _currentButton = CreateButton(core.Localize("weather_window_current"));
            _forecastButton = CreateButton(core.Localize("weather_window_forecast"));
            _searchButton.Click += async (_, _) => await SearchLocationsAsync();
            _currentButton.Click += async (_, _) => await QueryWeatherAsync("current");
            _forecastButton.Click += async (_, _) => await QueryWeatherAsync("forecast");
            _locationResults.SelectionChanged += (_, _) => UpdateWeatherButtons();
            _locationInput.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    await SearchLocationsAsync();
                }
            };
            Content = BuildContent();
            UpdateWeatherButtons();
        }

        private UIElement BuildContent()
        {
            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock
            {
                Text = _core.Localize("weather_window_title"),
                Foreground = TextBrush,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var locationSearch = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
            _searchButton.Margin = new Thickness(10, 0, 0, 0);
            DockPanel.SetDock(_searchButton, Dock.Right);
            locationSearch.Children.Add(_searchButton);
            _locationInput.Background = ReminderScheduleWindow.Brush("#0E151E");
            _locationInput.Foreground = TextBrush;
            _locationInput.CaretBrush = AccentBrush;
            _locationInput.BorderBrush = ReminderScheduleWindow.Brush("#34475B");
            _locationInput.BorderThickness = new Thickness(1);
            _locationInput.Padding = new Thickness(11, 9, 11, 9);
            _locationInput.ToolTip = _core.Localize("weather_window_location_hint");
            locationSearch.Children.Add(_locationInput);
            Grid.SetRow(locationSearch, 1);
            root.Children.Add(locationSearch);

            _locationResults.Background = PanelBrush;
            _locationResults.Foreground = TextBrush;
            _locationResults.BorderBrush = ReminderScheduleWindow.Brush("#34475B");
            _locationResults.BorderThickness = new Thickness(1);
            _locationResults.SelectionMode = SelectionMode.Single;
            _locationResults.ToolTip = _core.Localize("weather_window_select_location");
            _locationResults.ItemContainerStyle = CreateLocationItemStyle();
            Grid.SetRow(_locationResults, 2);
            root.Children.Add(_locationResults);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 8)
            };
            _currentButton.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(_currentButton);
            actions.Children.Add(_forecastButton);
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            _speakResult.Content = _core.Localize("weather_window_speak");
            _speakResult.IsChecked = true;
            _speakResult.Foreground = TextBrush;
            _speakResult.Margin = new Thickness(0, 0, 0, 10);
            Grid.SetRow(_speakResult, 4);
            root.Children.Add(_speakResult);

            _result.Background = PanelBrush;
            _result.Foreground = TextBrush;
            _result.CaretBrush = AccentBrush;
            _result.BorderBrush = ReminderScheduleWindow.Brush("#34475B");
            _result.BorderThickness = new Thickness(1);
            _result.Padding = new Thickness(14);
            _result.IsReadOnly = true;
            _result.TextWrapping = TextWrapping.Wrap;
            _result.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _result.VerticalContentAlignment = VerticalAlignment.Top;
            _result.Text = _core.Localize("weather_window_placeholder");
            Grid.SetRow(_result, 5);
            root.Children.Add(_result);

            _status.Foreground = MutedBrush;
            _status.Margin = new Thickness(0, 10, 0, 0);
            if (!IsWeatherEnabled())
                _status.Text = _core.Localize("utility_disabled");
            Grid.SetRow(_status, 6);
            root.Children.Add(_status);
            return root;
        }

        private async Task SearchLocationsAsync()
        {
            if (_isBusy)
                return;

            if (!IsWeatherEnabled())
            {
                _status.Text = _core.Localize("utility_disabled");
                return;
            }

            var city = _locationInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(city) || city.Length > 120)
            {
                _status.Text = _core.Localize("weather_error_location");
                _locationInput.Focus();
                return;
            }

            _isBusy = true;
            SetButtonsEnabled(false);
            _status.Text = _core.Localize("console_running_status");
            _locationResults.Items.Clear();
            try
            {
                var response = await LocalApi.PostAsync<object, LocalApiResponse>(
                    "weather/search",
                    new { city });
                var locationJson = response.Result?.Trim();
                if (string.IsNullOrWhiteSpace(locationJson) || !locationJson.StartsWith("[", StringComparison.Ordinal))
                {
                    _status.Text = response.Result ?? _core.Localize("weather_invalid_response");
                    return;
                }

                var locations = JsonSerializer.Deserialize<WeatherApiLocationInfo[]>(
                    locationJson,
                    LocalApi.JsonOptions) ?? Array.Empty<WeatherApiLocationInfo>();

                foreach (var location in locations)
                {
                    _locationResults.Items.Add(new ListBoxItem
                    {
                        Content = FormatLocation(location),
                        Tag = location,
                        Padding = new Thickness(9, 7, 9, 7)
                    });
                }

                if (locations.Length == 0)
                {
                    _status.Text = _core.Localize("weather_no_location", city);
                }
                else
                {
                    _locationResults.SelectedIndex = 0;
                    _status.Text = _core.Localize("weather_window_locations_found", locations.Length);
                }
            }
            catch (HttpRequestException ex)
            {
                _core.Log($"Weather location HTTP request failed: {ex}");
                _status.Text = _core.Localize("weather_connection_failed");
            }
            catch (TaskCanceledException ex)
            {
                _core.Log($"Weather location request timed out: {ex}");
                _status.Text = _core.Localize("weather_timeout");
            }
            catch (JsonException ex)
            {
                _core.Log($"Weather location response was invalid: {ex}");
                _status.Text = _core.Localize("weather_invalid_response");
            }
            catch (Exception ex)
            {
                _core.Log($"Weather location search failed: {ex}");
                _status.Text = _core.Localize("console_error");
            }
            finally
            {
                _isBusy = false;
                _searchButton.IsEnabled = true;
                UpdateWeatherButtons();
            }
        }

        private async Task QueryWeatherAsync(string type)
        {
            if (_isBusy)
                return;

            if (!IsWeatherEnabled())
            {
                _status.Text = _core.Localize("utility_disabled");
                return;
            }

            if (_locationResults.SelectedItem is not ListBoxItem { Tag: WeatherApiLocationInfo location })
            {
                _status.Text = _core.Localize("weather_window_select_location");
                return;
            }

            _isBusy = true;
            SetButtonsEnabled(false);
            _searchButton.IsEnabled = false;
            _status.Text = _core.Localize("console_running_status");
            try
            {
                var response = await LocalApi.PostAsync<object, LocalApiResponse>(
                    "weather",
                    new
                    {
                        city = location.Name,
                        type,
                        location.Name,
                        location.Admin1,
                        location.Admin2,
                        location.Country,
                        location.Latitude,
                        location.Longitude
                    });
                _result.Text = response.Result ?? string.Empty;
                if (_speakResult.IsChecked == true)
                {
                    await LocalApi.SayAsync(_result.Text);
                    _status.Text = _core.Localize("weather_window_spoken");
                }
                else
                {
                    _status.Text = string.Empty;
                }
            }
            catch (HttpRequestException ex)
            {
                _core.Log($"Weather HTTP request failed: {ex}");
                _status.Text = _core.Localize("weather_connection_failed");
                _result.Text = _status.Text;
            }
            catch (TaskCanceledException ex)
            {
                _core.Log($"Weather HTTP request timed out: {ex}");
                _status.Text = _core.Localize("weather_timeout");
                _result.Text = _status.Text;
            }
            catch (JsonException ex)
            {
                _core.Log($"Weather HTTP response was invalid: {ex}");
                _status.Text = _core.Localize("weather_invalid_response");
                _result.Text = _status.Text;
            }
            catch (Exception ex)
            {
                _core.Log($"Weather window query failed: {ex}");
                _status.Text = _core.Localize("console_error");
                _result.Text = ex.Message;
            }
            finally
            {
                _isBusy = false;
                _searchButton.IsEnabled = true;
                UpdateWeatherButtons();
            }
        }

        private bool IsWeatherEnabled() =>
            _core.GetPlugins().Any(plugin =>
                plugin.Name.Equals("weather", StringComparison.OrdinalIgnoreCase) && plugin.Enabled);

        private void UpdateWeatherButtons()
        {
            var canQuery = !_isBusy && IsWeatherEnabled() &&
                           _locationResults.SelectedItem is ListBoxItem { Tag: WeatherApiLocationInfo };
            _currentButton.IsEnabled = canQuery;
            _forecastButton.IsEnabled = canQuery;
            _searchButton.IsEnabled = !_isBusy;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _searchButton.IsEnabled = enabled;
            _currentButton.IsEnabled = enabled;
            _forecastButton.IsEnabled = enabled;
        }

        private static string FormatLocation(WeatherApiLocationInfo location) =>
            string.Join(", ", new[] { location.Name, location.Admin2, location.Admin1, location.Country }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        private static Style CreateLocationItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, PanelBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 7, 9, 7)));
            style.Triggers.Add(new Trigger
            {
                Property = ListBoxItem.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, ReminderScheduleWindow.Brush("#25483F")),
                    new Setter(Control.ForegroundProperty, TextBrush)
                }
            });

            return style;
        }

        private static Button CreateButton(string text, bool accent = false) => new()
        {
            Content = text,
            Background = accent ? AccentBrush : ReminderScheduleWindow.Brush("#202E3D"),
            Foreground = accent ? BackgroundBrush : TextBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 9, 14, 9),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
    }
}
