using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VpetCX.Abstractions.Plugins;

namespace VpetCX.Core
{
    internal sealed class PluginManagerWindow : Window
    {
        private static readonly Brush BackgroundBrush = Brush("#101722");
        private static readonly Brush PanelBrush = Brush("#182331");
        private static readonly Brush MutedBrush = Brush("#91A1B5");
        private static readonly Brush TextBrush = Brush("#E8EFF7");
        private static readonly Brush AccentBrush = Brush("#57D6B5");

        private readonly CoreLoader _core;
        private readonly VpetCXLocalization _localization;
        private readonly StackPanel _pluginToggleList = new();
        private readonly Dictionary<string, (CheckBox Toggle, TextBlock Label, TextBlock State)> _pluginToggles =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TextBlock _subtitleText = new();
        private readonly TextBlock _enabledCountText = new();
        private readonly TextBlock _footerText = new();
        private readonly TextBlock _settingsTitle = new();
        private readonly TextBlock _settingsDescription = new();
        private readonly TextBlock _languageLabel = new();
        private readonly TextBlock _settingsStatusText = new();
        private readonly ComboBox _languageSelector = new();
        private bool _isInitializingLanguage = true;
        private bool _isInitializingPluginToggles;

        public PluginManagerWindow(CoreLoader core, VpetCXLocalization localization)
        {
            _core = core;
            _localization = localization;
            Title = "VpetCX";
            Width = 600;
            Height = 720;
            MinWidth = 480;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;

            Content = BuildContent();
            BuildPluginToggles();
            foreach (var language in _localization.GetAvailableLanguages())
            {
                _languageSelector.Items.Add(CreateLanguageItem(
                    _localization.GetLanguageName(language),
                    language));
            }

            _languageSelector.SelectedValue = _core.Settings.Language;
            _isInitializingLanguage = false;
            RefreshLocalizedText();
        }

        private UIElement BuildContent()
        {
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock
            {
                Text = "VpetCX",
                FontSize = 27,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush
            });
            _subtitleText.FontSize = 11;
            _subtitleText.FontWeight = FontWeights.SemiBold;
            _subtitleText.Foreground = AccentBrush;
            _subtitleText.Margin = new Thickness(1, 2, 0, 0);
            titles.Children.Add(_subtitleText);
            header.Children.Add(titles);

            var badge = new Border
            {
                Background = PanelBrush,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(13, 9, 13, 9),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _enabledCountText
            };
            _enabledCountText.FontSize = 11;
            _enabledCountText.FontWeight = FontWeights.SemiBold;
            _enabledCountText.Foreground = AccentBrush;
            Grid.SetColumn(badge, 1);
            header.Children.Add(badge);
            root.Children.Add(header);

            var card = new Border
            {
                Background = PanelBrush,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };
            var content = new StackPanel();
            _settingsTitle.FontSize = 22;
            _settingsTitle.FontWeight = FontWeights.SemiBold;
            _settingsTitle.Foreground = TextBrush;
            content.Children.Add(_settingsTitle);

            _settingsDescription.Foreground = MutedBrush;
            _settingsDescription.TextWrapping = TextWrapping.Wrap;
            _settingsDescription.Margin = new Thickness(0, 7, 0, 18);
            content.Children.Add(_settingsDescription);
            content.Children.Add(_pluginToggleList);
            content.Children.Add(new Border
            {
                Height = 1,
                Background = Brush("#2A3949"),
                Margin = new Thickness(0, 10, 0, 18)
            });

            _languageLabel.Foreground = TextBrush;
            _languageLabel.FontWeight = FontWeights.SemiBold;
            _languageLabel.VerticalAlignment = VerticalAlignment.Center;
            _languageLabel.Margin = new Thickness(0, 0, 16, 0);
            ConfigureLanguageSelector();
            var languageRow = new Grid { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
            languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            Grid.SetColumn(_languageLabel, 0);
            Grid.SetColumn(_languageSelector, 1);
            languageRow.Children.Add(_languageLabel);
            languageRow.Children.Add(_languageSelector);
            content.Children.Add(languageRow);

            _settingsStatusText.Foreground = Brush("#F08A8A");
            _settingsStatusText.TextWrapping = TextWrapping.Wrap;
            _settingsStatusText.Margin = new Thickness(0, 12, 0, 0);
            content.Children.Add(_settingsStatusText);

            card.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            };
            Grid.SetRow(card, 1);
            root.Children.Add(card);

            _footerText.Foreground = MutedBrush;
            _footerText.FontSize = 11;
            _footerText.Margin = new Thickness(3, 12, 0, 0);
            Grid.SetRow(_footerText, 2);
            _footerText.Text = _localization.Get("console_settings");
            root.Children.Add(_footerText);
            return root;
        }

        private void ConfigureLanguageSelector()
        {
            _languageSelector.Width = 240;
            _languageSelector.VerticalContentAlignment = VerticalAlignment.Center;
            _languageSelector.Padding = new Thickness(8);
            _languageSelector.Background = Brush("#0E151E");
            _languageSelector.Foreground = TextBrush;
            _languageSelector.BorderBrush = Brush("#34475B");
            _languageSelector.Resources[SystemColors.WindowBrushKey] = Brush("#0E151E");
            _languageSelector.Resources[SystemColors.WindowTextBrushKey] = TextBrush;
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#0E151E")));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            foreach (var property in new[]
                     {
                         ComboBoxItem.IsHighlightedProperty,
                         ComboBoxItem.IsSelectedProperty
                     })
            {
                itemStyle.Triggers.Add(new Trigger
                {
                    Property = property,
                    Value = true,
                    Setters =
                    {
                        new Setter(Control.BackgroundProperty, Brush("#25483F")),
                        new Setter(Control.ForegroundProperty, TextBrush)
                    }
                });
            }

            _languageSelector.ItemContainerStyle = itemStyle;
            _languageSelector.SelectedValuePath = "Tag";
            _languageSelector.SelectionChanged += (_, _) =>
            {
                if (!_isInitializingLanguage && _languageSelector.SelectedValue is string language)
                    ChangeLanguage(language);
            };
        }

        private void BuildPluginToggles()
        {
            _isInitializingPluginToggles = true;
            foreach (var plugin in GetManageablePlugins().OrderBy(
                         candidate => candidate.Name,
                         StringComparer.OrdinalIgnoreCase))
            {
                var toggle = new CheckBox
                {
                    IsChecked = _core.IsPluginEnabledConfigured(plugin.Name),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    Foreground = AccentBrush,
                    ToolTip = _core.IsPluginEnabledConfigured(plugin.Name)
                        ? _localization.Get("console_toggle_disable")
                        : _localization.Get("console_toggle_enable")
                };
                var label = new TextBlock
                {
                    Foreground = TextBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14
                };
                var state = new TextBlock
                {
                    Foreground = _core.IsPluginEnabledConfigured(plugin.Name) ? AccentBrush : MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(toggle, 0);
                Grid.SetColumn(label, 1);
                Grid.SetColumn(state, 2);
                row.Children.Add(toggle);
                row.Children.Add(label);
                row.Children.Add(state);
                _pluginToggleList.Children.Add(new Border
                {
                    Background = Brush("#131D28"),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Child = row
                });

                toggle.Checked += (_, _) =>
                {
                    if (!_isInitializingPluginToggles)
                        SetPluginEnabled(plugin, true);
                };
                toggle.Unchecked += (_, _) =>
                {
                    if (!_isInitializingPluginToggles)
                        SetPluginEnabled(plugin, false);
                };
                _pluginToggles.Add(plugin.Name, (toggle, label, state));
            }

            _isInitializingPluginToggles = false;
            RefreshPluginToggles();
        }

        private void RefreshLocalizedText()
        {
            Title = "VpetCX";
            _subtitleText.Text = _localization.Get("console_subtitle");
            _footerText.Text = _localization.Get("console_settings");
            _settingsTitle.Text = _localization.Get("console_settings");
            _settingsDescription.Text = _localization.Get("console_settings_description");
            _languageLabel.Text = _localization.Get("console_language");
            _enabledCountText.Text = _localization.Get(
                "console_enabled_count",
                GetManageablePlugins().Count(plugin => _core.IsPluginEnabledConfigured(plugin.Name)));
            RefreshPluginToggles();
        }

        private void RefreshPluginToggles()
        {
            _isInitializingPluginToggles = true;
            foreach (var plugin in GetManageablePlugins())
            {
                if (!_pluginToggles.TryGetValue(plugin.Name, out var controls))
                    continue;

                controls.Label.Text = GetPluginDisplayName(plugin);
                var configuredEnabled = _core.IsPluginEnabledConfigured(plugin.Name);
                controls.Toggle.IsChecked = configuredEnabled;
                controls.State.Text = _localization.Get(
                    configuredEnabled ? "console_toggle_enable" : "console_toggle_disable");
                controls.State.Foreground = configuredEnabled ? AccentBrush : MutedBrush;
                controls.Toggle.ToolTip = _localization.Get(
                    configuredEnabled ? "console_toggle_disable" : "console_toggle_enable");
            }

            _isInitializingPluginToggles = false;
        }

        private void SetPluginEnabled(VPetPlugin plugin, bool enabled)
        {
            try
            {
                _core.SetPluginEnabled(plugin.Name, enabled);
                _settingsStatusText.Text = _localization.Get("console_restart_required");
            }
            catch (Exception ex)
            {
                _core.Log($"Could not change enabled state for utility '{plugin.Name}': {ex}");
                _settingsStatusText.Text = _localization.Get("console_settings_save_failed");
            }

            RefreshLocalizedText();
        }

        private void ChangeLanguage(string language)
        {
            try
            {
                _core.SetLanguage(language);
                _localization.SetLanguage(language);
                _settingsStatusText.Text = _localization.Get("console_restart_required");
                RefreshLocalizedText();
            }
            catch (Exception ex)
            {
                _core.Log($"Could not change VpetCX language to '{language}': {ex}");
                _settingsStatusText.Text = _localization.Get("console_language_save_failed", ex.Message);
                _isInitializingLanguage = true;
                _languageSelector.SelectedValue = _core.Settings.Language;
                _isInitializingLanguage = false;
            }
        }

        private IEnumerable<VPetPlugin> GetManageablePlugins() =>
            _core.GetPlugins();

        private string GetPluginDisplayName(VPetPlugin plugin) =>
            plugin.Name.ToLowerInvariant() switch
            {
                "diary" => _localization.Get("plugin_diary"),
                "reminder" => _localization.Get("plugin_reminder"),
                "weather" => _localization.Get("plugin_weather"),
                _ => plugin.Name
            };

        private static ComboBoxItem CreateLanguageItem(string name, string language) =>
            new() { Content = name, Tag = language };

        private static Brush Brush(string color) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
