using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VpetCX.Core
{
    internal sealed class DiaryWindow : Window
    {
        private static readonly Brush BackgroundBrush = ReminderScheduleWindow.Brush("#101722");
        private static readonly Brush TextBrush = ReminderScheduleWindow.Brush("#E8EFF7");
        private static readonly Brush MutedBrush = ReminderScheduleWindow.Brush("#91A1B5");
        private static readonly Brush AccentBrush = ReminderScheduleWindow.Brush("#57D6B5");

        private readonly CoreLoader _core;
        private readonly TextBox _entryInput = new();
        private readonly TextBox _searchInput = new();
        private readonly TextBox _idInput = new();
        private readonly TextBox _results = new();
        private readonly TextBlock _status = new();
        private readonly Button _saveButton;
        private readonly Button _listButton;
        private readonly Button _searchButton;
        private readonly Button _deleteButton;
        private bool _isBusy;

        public DiaryWindow(CoreLoader core)
        {
            _core = core;
            Title = core.Localize("diary_window_title");
            Width = 760;
            Height = 620;
            MinWidth = 600;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;

            _saveButton = CreateButton(core.Localize("diary_window_add"), true);
            _listButton = CreateButton(core.Localize("diary_window_list"));
            _searchButton = CreateButton(core.Localize("diary_window_search"));
            _deleteButton = CreateButton(core.Localize("diary_window_delete"));

            _saveButton.Click += async (_, _) => await RunDiaryActionAsync(
                "write", _entryInput.Text);
            _listButton.Click += async (_, _) => await RunDiaryActionAsync("view");
            _searchButton.Click += async (_, _) => await RunDiaryActionAsync(
                "search", _searchInput.Text);
            _deleteButton.Click += async (_, _) => await RunDiaryActionAsync(
                "delete", id: _idInput.Text);
            Loaded += async (_, _) => await RunDiaryActionAsync("view");
            Content = BuildContent();
        }

        private UIElement BuildContent()
        {
            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new TextBlock
            {
                Text = _core.Localize("diary_window_title"),
                Foreground = TextBrush,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 14)
            };
            root.Children.Add(heading);

            ConfigureTextBox(_entryInput, true);
            _entryInput.MinHeight = 92;
            _entryInput.MaxLength = 4000;
            _entryInput.ToolTip = _core.Localize("diary_window_entry_hint");
            Grid.SetRow(_entryInput, 1);
            root.Children.Add(_entryInput);

            var actions = new Grid { Margin = new Thickness(0, 12, 0, 12) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            ConfigureTextBox(_searchInput);
            _searchInput.ToolTip = _core.Localize("diary_window_search_hint");
            searchPanel.Children.Add(_searchInput);
            Grid.SetColumn(searchPanel, 0);
            actions.Children.Add(searchPanel);
            _searchButton.Margin = new Thickness(0, 0, 8, 0);
            _listButton.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(_searchButton, 1);
            Grid.SetColumn(_listButton, 2);
            actions.Children.Add(_searchButton);
            actions.Children.Add(_listButton);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            ConfigureTextBox(_results, true);
            _results.IsReadOnly = true;
            _results.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid.SetRow(_results, 3);
            root.Children.Add(_results);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            footer.LastChildFill = true;
            var deletePanel = new DockPanel { Width = 280 };
            ConfigureTextBox(_idInput);
            _idInput.ToolTip = _core.Localize("diary_window_id_hint");
            DockPanel.SetDock(_deleteButton, Dock.Right);
            _deleteButton.Margin = new Thickness(8, 0, 0, 0);
            deletePanel.Children.Add(_deleteButton);
            deletePanel.Children.Add(_idInput);
            DockPanel.SetDock(deletePanel, Dock.Right);
            footer.Children.Add(deletePanel);
            _status.Foreground = MutedBrush;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.TextWrapping = TextWrapping.Wrap;
            footer.Children.Add(_status);
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);
            return root;
        }

        private async Task RunDiaryActionAsync(string action, string? text = null, string? id = null)
        {
            if (_isBusy)
                return;

            if (!_core.GetPlugins().Any(plugin =>
                    plugin.Name.Equals("diary", StringComparison.OrdinalIgnoreCase) && plugin.Enabled))
            {
                _status.Text = _core.Localize("utility_disabled");
                return;
            }

            _isBusy = true;
            SetButtonsEnabled(false);
            try
            {
                var response = await LocalApi.PostAsync<object, LocalApiResponse>(
                    "diary",
                    new { action, text, id });
                _results.Text = response.Result ?? string.Empty;
                _status.Text = string.Empty;
            }
            catch (Exception ex)
            {
                _core.Log($"Diary window action failed: {ex}");
                _status.Text = _core.Localize("console_error");
                _results.Text = ex.Message;
            }
            finally
            {
                _isBusy = false;
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _saveButton.IsEnabled = enabled;
            _listButton.IsEnabled = enabled;
            _searchButton.IsEnabled = enabled;
            _deleteButton.IsEnabled = enabled;
        }

        private static void ConfigureTextBox(TextBox textBox, bool multiline = false)
        {
            textBox.Background = ReminderScheduleWindow.Brush("#0E151E");
            textBox.Foreground = TextBrush;
            textBox.CaretBrush = AccentBrush;
            textBox.BorderBrush = ReminderScheduleWindow.Brush("#34475B");
            textBox.BorderThickness = new Thickness(1);
            textBox.Padding = new Thickness(11, 9, 11, 9);
            textBox.VerticalContentAlignment = VerticalAlignment.Top;
            textBox.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
            textBox.AcceptsReturn = multiline;
        }

        private static Button CreateButton(string text, bool accent = false) => new()
        {
            Content = text,
            Background = accent ? AccentBrush : ReminderScheduleWindow.Brush("#202E3D"),
            Foreground = accent ? BackgroundBrush : TextBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };
    }
}
