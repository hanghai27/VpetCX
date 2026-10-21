using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VpetCX.Core
{
    internal sealed class ReminderScheduleWindow : Window
    {
        private static readonly Brush BackgroundBrush = Brush("#101722");
        private static readonly Brush PanelBrush = Brush("#182331");
        private static readonly Brush MutedBrush = Brush("#91A1B5");
        private static readonly Brush TextBrush = Brush("#E8EFF7");
        private static readonly Brush AccentBrush = Brush("#57D6B5");

        private readonly VpetCXLocalization _localization;
        private readonly Action<string> _log;
        private ReminderApiInfo[] _reminderSnapshot = Array.Empty<ReminderApiInfo>();
        private readonly ListBox _scheduleList = new();
        private readonly TextBox _durationInput = new();
        private readonly ComboBox _unitSelector = new();
        private readonly TextBox _messageInput = new();
        private readonly TextBlock _statusText = new();
        private readonly Button _addButton = CreateButton(string.Empty, true);
        private readonly Button _saveButton = CreateButton(string.Empty);
        private readonly Button _deleteButton = CreateButton(string.Empty);
        private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private string? _selectedReminderId;
        private bool _refreshingSchedule;

        public ReminderScheduleWindow(CoreLoader core)
        {
            _localization = core.Localization;
            _log = core.Log;
            Title = $"VpetCX - {_localization.Get("reminder_title")}";
            Width = 880;
            Height = 590;
            MinWidth = 700;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Content = BuildContent();

            _addButton.Click += async (_, _) => await AddReminderAsync();
            _saveButton.Click += async (_, _) => await SaveReminderAsync();
            _deleteButton.Click += async (_, _) => await DeleteReminderAsync();
            _scheduleList.SelectionChanged += (_, _) => SelectReminder();
            _messageInput.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    e.Handled = true;
                    if (_selectedReminderId == null)
                        await AddReminderAsync();
                    else
                        await SaveReminderAsync();
                }
            };
            Loaded += async (_, _) =>
            {
                await RefreshScheduleListAsync();
                _countdownTimer.Start();
            };
            Closed += (_, _) => _countdownTimer.Stop();
            _countdownTimer.Tick += async (_, _) => await RefreshCountdownLabelsAsync();
            SetSelectedReminder(null);
        }

        private UIElement BuildContent()
        {
            var root = new Grid { Margin = new Thickness(20) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new Grid { Margin = new Thickness(0, 0, 16, 0) };
            root.Children.Add(left);
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.Children.Add(CreateHeading(_localization.Get("reminder_list_title")));

            _scheduleList.Background = PanelBrush;
            _scheduleList.Foreground = TextBrush;
            _scheduleList.BorderBrush = Brush("#34475B");
            _scheduleList.BorderThickness = new Thickness(1);
            _scheduleList.Margin = new Thickness(0, 12, 0, 12);
            _scheduleList.SelectionMode = SelectionMode.Single;
            Grid.SetRow(_scheduleList, 1);
            left.Children.Add(_scheduleList);

            _addButton.Content = _localization.Get("reminder_new");
            _addButton.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(_addButton, 2);
            left.Children.Add(_addButton);
            _deleteButton.Content = _localization.Get("reminder_delete_selected");
            Grid.SetRow(_deleteButton, 3);
            left.Children.Add(_deleteButton);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            right.Children.Add(CreateHeading(_localization.Get("reminder_details")));
            var countdownLabel = CreateLabel(_localization.Get("reminder_countdown_duration"));
            countdownLabel.Margin = new Thickness(0, 18, 0, 8);
            Grid.SetRow(countdownLabel, 1);
            right.Children.Add(countdownLabel);

            var countdownRow = new StackPanel { Orientation = Orientation.Horizontal };
            _durationInput.Text = "4";
            _durationInput.Width = 100;
            _durationInput.Padding = new Thickness(9, 7, 9, 7);
            _durationInput.Background = Brush("#0E151E");
            _durationInput.Foreground = TextBrush;
            _durationInput.CaretBrush = AccentBrush;
            _durationInput.BorderBrush = Brush("#34475B");
            _durationInput.VerticalContentAlignment = VerticalAlignment.Center;
            _durationInput.Margin = new Thickness(0, 0, 8, 0);
            countdownRow.Children.Add(_durationInput);

            _unitSelector.MinWidth = 125;
            _unitSelector.Padding = new Thickness(9, 7, 9, 7);
            _unitSelector.Background = Brush("#0E151E");
            _unitSelector.Foreground = TextBrush;
            _unitSelector.BorderBrush = Brush("#34475B");
            _unitSelector.Items.Add(CreateUnitItem(_localization.Get("reminder_unit_seconds"), "seconds"));
            _unitSelector.Items.Add(CreateUnitItem(_localization.Get("reminder_unit_minutes"), "minutes"));
            _unitSelector.Items.Add(CreateUnitItem(_localization.Get("reminder_unit_hours"), "hours"));
            _unitSelector.SelectedValuePath = "Tag";
            _unitSelector.SelectedValue = "seconds";
            countdownRow.Children.Add(_unitSelector);
            Grid.SetRow(countdownRow, 2);
            right.Children.Add(countdownRow);

            var messageLabel = CreateLabel(_localization.Get("reminder_message"));
            messageLabel.Margin = new Thickness(0, 18, 0, 8);
            Grid.SetRow(messageLabel, 3);
            right.Children.Add(messageLabel);

            _messageInput.Background = Brush("#0E151E");
            _messageInput.Foreground = TextBrush;
            _messageInput.CaretBrush = AccentBrush;
            _messageInput.BorderBrush = Brush("#34475B");
            _messageInput.BorderThickness = new Thickness(1);
            _messageInput.Padding = new Thickness(11, 9, 11, 9);
            _messageInput.VerticalContentAlignment = VerticalAlignment.Top;
            _messageInput.TextWrapping = TextWrapping.Wrap;
            _messageInput.AcceptsReturn = true;
            _messageInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _messageInput.MaxLength = 500;
            _messageInput.MinHeight = 110;
            Grid.SetRow(_messageInput, 4);
            right.Children.Add(_messageInput);

            _saveButton.Content = _localization.Get("reminder_save");
            _saveButton.HorizontalAlignment = HorizontalAlignment.Left;
            _saveButton.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(_saveButton, 5);
            right.Children.Add(_saveButton);

            _statusText.Foreground = MutedBrush;
            _statusText.TextWrapping = TextWrapping.Wrap;
            _statusText.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(_statusText, 6);
            right.Children.Add(_statusText);
            return root;
        }

        private async Task RefreshScheduleListAsync()
        {
            try
            {
                var response = await LocalApi.PostAsync<object, LocalApiResponse>(
                    "reminders",
                    new { action = "list" });
                _reminderSnapshot = JsonSerializer.Deserialize<ReminderApiInfo[]>(
                    response.Result ?? "[]",
                    LocalApi.JsonOptions) ?? Array.Empty<ReminderApiInfo>();
            }
            catch (Exception ex)
            {
                _log($"Could not load reminders through the local API: {ex}");
                _statusText.Text = _localization.Get("console_error");
                return;
            }

            var selectedId = _selectedReminderId;
            _refreshingSchedule = true;
            _scheduleList.Items.Clear();
            foreach (var reminder in _reminderSnapshot)
            {
                var itemContent = new StackPanel { Margin = new Thickness(5, 3, 5, 3) };
                itemContent.Children.Add(new TextBlock
                {
                    Text = GetReminderStatus(reminder),
                    Foreground = AccentBrush,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold
                });
                itemContent.Children.Add(new TextBlock
                {
                    Text = reminder.Message,
                    Foreground = TextBrush,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 42,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                _scheduleList.Items.Add(new ListBoxItem
                {
                    Content = itemContent,
                    Tag = reminder,
                    Padding = new Thickness(8, 6, 8, 6)
                });
            }

            if (selectedId != null)
            {
                var selectedItem = _scheduleList.Items
                    .OfType<ListBoxItem>()
                    .FirstOrDefault(item => item.Tag is ReminderApiInfo reminder && reminder.Id == selectedId);
                if (selectedItem == null)
                    SetSelectedReminder(null);
                else
                    _scheduleList.SelectedItem = selectedItem;
            }
            _refreshingSchedule = false;
            _statusText.Text = _scheduleList.Items.Count == 0
                ? _localization.Get("reminder_empty_list")
                : _localization.Get("reminder_list_countdown", _scheduleList.Items.Count);
        }

        private void SelectReminder()
        {
            if (_refreshingSchedule || _scheduleList.SelectedItem is not ListBoxItem { Tag: ReminderApiInfo reminder })
                return;

            _selectedReminderId = reminder.Id;
            var remaining = reminder.DueAt - DateTimeOffset.Now;
            if (remaining < TimeSpan.FromSeconds(1))
                remaining = TimeSpan.FromSeconds(1);
            if (remaining.TotalHours >= 1)
            {
                _durationInput.Text = Math.Ceiling(remaining.TotalHours).ToString(CultureInfo.InvariantCulture);
                _unitSelector.SelectedValue = "hours";
            }
            else if (remaining.TotalMinutes >= 1)
            {
                _durationInput.Text = Math.Ceiling(remaining.TotalMinutes).ToString(CultureInfo.InvariantCulture);
                _unitSelector.SelectedValue = "minutes";
            }
            else
            {
                _durationInput.Text = Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                _unitSelector.SelectedValue = "seconds";
            }
            _messageInput.Text = reminder.Message;
            _saveButton.IsEnabled = true;
            _deleteButton.IsEnabled = true;
        }

        private void SetSelectedReminder(string? id)
        {
            _selectedReminderId = id;
            _scheduleList.SelectedItem = id == null
                ? null
                : _scheduleList.Items
                    .OfType<ListBoxItem>()
                    .FirstOrDefault(item => item.Tag is ReminderApiInfo reminder && reminder.Id == id);
            if (id == null)
            {
                _durationInput.Text = "4";
                _unitSelector.SelectedValue = "seconds";
                _messageInput.Clear();
            }

            _saveButton.IsEnabled = id != null;
            _deleteButton.IsEnabled = id != null;
        }

        private async Task AddReminderAsync()
        {
            var message = _messageInput.Text.Trim();
            if (!ValidateMessage(message))
                return;

            if (!await SaveAsync(null, message))
                return;

            await RefreshScheduleListAsync();
            SetSelectedReminder(null);
            _statusText.Text = _localization.Get("reminder_added");
        }

        private async Task SaveReminderAsync()
        {
            if (_selectedReminderId == null)
            {
                _statusText.Text = _localization.Get("reminder_select_to_edit");
                return;
            }

            var message = _messageInput.Text.Trim();
            if (!ValidateMessage(message) || !await SaveAsync(_selectedReminderId, message))
                return;

            var id = _selectedReminderId;
            await RefreshScheduleListAsync();
            SetSelectedReminder(id);
            _statusText.Text = _localization.Get("reminder_saved");
        }

        private async Task DeleteReminderAsync()
        {
            if (_selectedReminderId == null)
                return;

            try
            {
                var response = await LocalApi.PostAsync<object, LocalApiResponse>(
                    "reminders",
                    new { action = "delete", id = _selectedReminderId });
                var operation = JsonSerializer.Deserialize<ReminderApiOperationResponse>(
                    response.Result ?? "{}",
                    LocalApi.JsonOptions);
                if (operation?.Success != true)
                {
                    _statusText.Text = operation?.Error ?? _localization.Get("reminder_delete_failed");
                    return;
                }

                await RefreshScheduleListAsync();
                SetSelectedReminder(null);
                _statusText.Text = _localization.Get("reminder_deleted");
            }
            catch (Exception ex)
            {
                _log($"Could not delete reminder through the local API: {ex}");
                _statusText.Text = _localization.Get("reminder_delete_failed");
            }
        }

        private async Task<bool> SaveAsync(string? id, string message)
        {
            if (!int.TryParse(_durationInput.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
                amount < 1 ||
                _unitSelector.SelectedValue is not string unit)
            {
                _statusText.Text = _localization.Get("reminder_invalid_duration");
                return false;
            }

            var maximumAmount = unit switch
            {
                "seconds" => 31_536_000,
                "minutes" => 525_600,
                "hours" => 8_760,
                _ => 0
            };
            if (amount > maximumAmount)
            {
                _statusText.Text = _localization.Get("reminder_invalid_duration");
                return false;
            }

            try
            {
                var response = await LocalApi.PostAsync<object, LocalApiResponse>(
                    "reminders",
                    new { action = "save", id, time = amount, unit, @event = message });
                var operation = JsonSerializer.Deserialize<ReminderApiOperationResponse>(
                    response.Result ?? "{}",
                    LocalApi.JsonOptions);
                if (operation?.Success == true)
                    return true;

                _statusText.Text = operation?.Error ?? _localization.Get("reminder_save_failed");
                return false;
            }
            catch (Exception ex)
            {
                _log($"Could not save reminder through the local API: {ex}");
                _statusText.Text = _localization.Get("reminder_save_failed");
                return false;
            }
        }

        private async Task RefreshCountdownLabelsAsync()
        {
            var reminders = _reminderSnapshot;
            if (_scheduleList.Items.Count != reminders.Length)
            {
                await RefreshScheduleListAsync();
                return;
            }

            for (var index = 0; index < reminders.Length; index++)
            {
                if (_scheduleList.Items[index] is not ListBoxItem item ||
                    item.Tag is not ReminderApiInfo reminder ||
                    reminder.Id != reminders[index].Id)
                {
                    await RefreshScheduleListAsync();
                    return;
                }

                var currentReminder = reminders[index];
                if (item.Content is not StackPanel { Children.Count: > 0 } content ||
                    content.Children[0] is not TextBlock countdown)
                {
                    continue;
                }

                item.Tag = currentReminder;
                countdown.Text = GetReminderStatus(currentReminder);
            }
        }

        private static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;
            var totalHours = (int)Math.Min(remaining.TotalHours, 9999);
            return $"{totalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        }

        private string GetReminderStatus(ReminderApiInfo reminder) =>
            reminder.IsDelivered
                ? _localization.Get("reminder_delivered")
                : FormatRemaining(reminder.DueAt - DateTimeOffset.Now);

        private static ComboBoxItem CreateUnitItem(string label, string unit) =>
            new() { Content = label, Tag = unit };

        private bool ValidateMessage(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && message.Length <= 500)
                return true;

            _statusText.Text = _localization.Get("reminder_validation");
            return false;
        }

        private static TextBlock CreateHeading(string text) => new()
        {
            Text = text,
            Foreground = TextBrush,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        };

        private static TextBlock CreateLabel(string text) => new()
        {
            Text = text,
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };

        private static Button CreateButton(string text, bool accent = false) => new()
        {
            Content = text,
            Background = accent ? AccentBrush : Brush("#202E3D"),
            Foreground = accent ? BackgroundBrush : TextBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = Cursors.Hand,
            FontWeight = FontWeights.SemiBold
        };

        internal static SolidColorBrush Brush(string color) =>
            new((Color)ColorConverter.ConvertFromString(color));
    }
}
