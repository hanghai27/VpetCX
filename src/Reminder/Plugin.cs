using System;
using VpetCX.Interface;

namespace ReminderPlugin
{
    public class ReminderPlugin : IPlugin
    {
        public string Name => "Reminder";
        public string Version => "0.1.0";

        private IHost _host;

        public void OnLoad(IHost host)
        {
            _host = host;
            _host.Log($"{Name} v{Version} loaded.");
            // Khởi tạo, đăng ký sự kiện, lập timer, ...
        }

        public void OnUnload()
        {
            // Dọn dẹp
            _host?.Log($"{Name} unloaded.");
        }
    }
}
