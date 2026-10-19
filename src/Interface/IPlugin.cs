using System;

namespace VpetCX.Interface
{
    public interface IPlugin
    {
        string Name { get; }
        string Version { get; }
        void OnLoad(IHost host);
        void OnUnload();
    }

    public interface IHost
    {
        void Log(string message);
        // Thêm API khác mà Core cung cấp cho plugin
    }
}
