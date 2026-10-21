using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VpetCX.Abstractions.Plugins
{
    public abstract class PluginContext
    {
        public abstract PluginSettings Settings { get; protected set; }

        public abstract void Log(string message);

        public virtual string Localize(string key, params object[] arguments) => key;
    }

    public class PluginSettings
    {
        private HashSet<string> _disabledPlugins = new(StringComparer.OrdinalIgnoreCase);

        public string Language { get; set; } = "en";

        public HashSet<string> DisabledPlugins
        {
            get => _disabledPlugins;
            set => _disabledPlugins = new HashSet<string>(
                (value ?? new HashSet<string>()).Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
        }

    }

    public abstract class VPetPlugin
    {
        public abstract string Name { get; }
        public abstract string Author { get; }
        public abstract string Description { get; }
        public abstract string Parameters { get; }
        public abstract string Examples { get; }

        public bool Enabled { get; set; } = true;

        public abstract Task<string> Function(string arguments);

        public abstract void Initialize(PluginContext core);

        public abstract void Unload();
    }
}

namespace VpetCX.Abstractions
{
    public static class VpetCXDataPaths
    {
        public static string GetDataFilePath(string fileName)
        {
            ValidatePathSegment(fileName, nameof(fileName));

            var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsDirectory))
                throw new InvalidOperationException("The Documents folder could not be resolved.");

            return Path.Combine(documentsDirectory, "VpetCX", fileName);
        }

        public static string GetDataFilePath(string category, string fileName)
        {
            ValidatePathSegment(category, nameof(category));
            ValidatePathSegment(fileName, nameof(fileName));

            var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsDirectory))
                throw new InvalidOperationException("The Documents folder could not be resolved.");

            var categoryDirectory = Path.Combine(documentsDirectory, "VpetCX", category);
            Directory.CreateDirectory(categoryDirectory);
            return Path.Combine(categoryDirectory, fileName);
        }

        private static void ValidatePathSegment(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value is "." or ".." ||
                !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) ||
                value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("A single valid directory or file name is required.", parameterName);
            }
        }
    }
}
