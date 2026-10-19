using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows; // Cần thiết để dùng MessageBox
using VpetCX.Interface;

namespace VpetCX.Reminder
{
    public class ReminderPlugin : IPlugin
    {
        public string Name => "Reminder";
        public string Description => "Set a timed reminder (deferred).";
        public bool Enabled { get; set; } = true;

        public void Initialize()
        {
            // Có thể để trống hoặc ghi log
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
                // Tách các tham số từ chuỗi lệnh (ví dụ: time(10), unit(minutes), event("study"))
                var timeMatch = new Regex(@"time\((\d+)\)").Match(arguments);
                var unitMatch = new Regex(@"unit\((\w+)\)").Match(arguments);
                var eventMatch = new Regex(@"event\(""(.*?)""\)").Match(arguments);

                if (!timeMatch.Success || !eventMatch.Success)
                {
                    return Task.FromResult("Failed: Missing 'time' or 'event' parameters.");
                }

                var timeValue = int.Parse(timeMatch.Groups[1].Value);
                var unit = unitMatch.Success ? unitMatch.Groups[1].Value.ToLower() : "seconds";
                var message = eventMatch.Groups[1].Value;

                // Tính toán thời gian chờ
                TimeSpan delay = (unit == "minutes" || unit == "minute") 
                    ? TimeSpan.FromMinutes(timeValue) 
                    : TimeSpan.FromSeconds(timeValue);

                // Bắt đầu chạy ngầm việc đếm giờ
                _ = ReminderTask(delay, message);
                
                // Trả về chuỗi rỗng để AI không hiểu nhầm là đã xong việc
                return Task.FromResult("");
            }
            catch (Exception e)
            {
                return Task.FromResult($"Failed: {e.Message}");
            }
        }

        private async Task ReminderTask(TimeSpan delay, string message)
        {
            // Đợi đến thời gian đã hẹn
            await Task.Delay(delay);

            // Hiển thị thông báo trên màn hình (phải chạy trên luồng UI của Windows)
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"It's time to: \"{message}\"", "Reminder", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }
    }
}
