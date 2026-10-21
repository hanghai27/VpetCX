using System;
using System.Text.Json;

namespace VpetCX.Abstractions.Plugins
{
    /// <summary>
    /// Reads named values from commands such as <c>action(search), keyword("red, blue")</c>.
    /// </summary>
    public static class PluginArguments
    {
        public static bool TryGetValue(string? arguments, string name, out string value)
        {
            value = "";
            if (string.IsNullOrEmpty(arguments) || string.IsNullOrWhiteSpace(name))
                return false;

            for (var index = 0; index < arguments.Length; index++)
            {
                if (index > 0 && !char.IsWhiteSpace(arguments[index - 1]) && arguments[index - 1] != ',')
                    continue;

                if (index + name.Length > arguments.Length ||
                    !arguments.AsSpan(index, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var cursor = index + name.Length;
                while (cursor < arguments.Length && char.IsWhiteSpace(arguments[cursor]))
                    cursor++;

                if (cursor >= arguments.Length || arguments[cursor] != '(')
                    continue;

                cursor++;
                while (cursor < arguments.Length && char.IsWhiteSpace(arguments[cursor]))
                    cursor++;

                var valueStart = cursor;
                var depth = 1;
                var quote = '\0';
                var escaped = false;

                for (; cursor < arguments.Length; cursor++)
                {
                    var character = arguments[cursor];
                    if (quote != '\0' && escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (quote != '\0' && character == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (quote != '\0')
                    {
                        if (character == quote)
                            quote = '\0';
                        continue;
                    }

                    if (cursor == valueStart && (character is '"' or '\''))
                    {
                        quote = character;
                        continue;
                    }

                    if (character == '(')
                    {
                        depth++;
                        continue;
                    }

                    if (character != ')' || --depth != 0)
                        continue;

                    return TryDecodeValue(arguments[valueStart..cursor].Trim(), out value);
                }

                return false;
            }

            return false;
        }

        private static bool TryDecodeValue(string value, out string decoded)
        {
            decoded = value;
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                try
                {
                    decoded = JsonSerializer.Deserialize<string>(value) ?? "";
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                decoded = value[1..^1]
                    .Replace("\\'", "'", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
                return true;
            }

            return !value.StartsWith('"') && !value.StartsWith('\'') &&
                   !value.EndsWith('"') && !value.EndsWith('\'');
        }
    }
}
