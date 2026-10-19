using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VpetCX.Interface;

namespace VpetCX.Reminder
{
    public class ReminderPlugin : IPlugin
    {
        public string Name => "Reminder";
        public string Description => "Set a timed reminder (deferred). The event parameter should be a brief description only.";
        public bool Enabled { get; set; } = true;

        public void Initialize()
        {
            Console.WriteLine("Reminder Plugin Initialized!");
        }

        public void Unload()
        {
            Console.WriteLine("Reminder Plugin Unloaded!");
        }

        public Task<string> ProcessCommand(string arguments)
        {
            try
            {
                // Parse the command arguments
                var timeMatch = new Regex(@"time\((\d+)\)").Match(arguments);
                var unitMatch = new Regex(@"unit\((\w+)\)").Match(arguments);
                var eventMatch = new Regex(@"event\(""(.*?)""\)").Match(arguments);

                if (!timeMatch.Success || !eventMatch.Success)
                {
                    return Task.FromResult("Failed to create reminder: Missing 'time' or 'event' parameters.");
                }

                var timeValue = int.Parse(timeMatch.Groups[1].Value);
                var unit = unitMatch.Success ? unitMatch.Groups[1].Value.ToLower() : "seconds";
                var message = eventMatch.Groups[1].Value;

                TimeSpan delay;
                switch (unit)
                {
                    case "minute":
                    case "minutes":
                        delay = TimeSpan.FromMinutes(timeValue);
                        break;
                    case "second":
                    case "seconds":
                    default:
                        delay = TimeSpan.FromSeconds(timeValue);
                        break;
                }

                // Start the background task
                _ = ReminderTask(delay, message);

                return Task.FromResult("");
            }
            catch (Exception e)
            {
                return Task.FromResult($"Failed to create reminder: {e.Message}");
            }
        }

        private async Task ReminderTask(TimeSpan delay, string message)
        {
            await Task.Delay(delay);

            // Show the notification (Using Windows MessageBox)
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show($"It's time to: \"{message}\"", "Reminder", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            });
        }
    }
}
