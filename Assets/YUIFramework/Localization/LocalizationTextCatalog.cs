using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Newtonsoft.Json.Linq;
using YUIFramework.Configuration;

namespace YUIFramework.Localization
{
    public sealed class LocalizationTextCatalog
    {
        public static IReadOnlyList<string> SupportedLocales { get; } = Array.AsReadOnly(new[] { "en", "zh-CN", "fr" });
        public static ConfigTable<LocalizationTextCatalog> Table { get; } =
            new ConfigTable<LocalizationTextCatalog>("LocalizationTextConfig", true, Parse);
        private readonly Dictionary<string, LocalizedTextEntry> _entries;
        private LocalizationTextCatalog(Dictionary<string, LocalizedTextEntry> entries, List<string> locales)
        {
            _entries = entries;
            var keys = new List<string>(entries.Keys); keys.Sort(StringComparer.Ordinal);
            Keys = keys.AsReadOnly();
            Locales = locales.AsReadOnly();
        }
        public IReadOnlyList<string> Keys { get; }
        public IReadOnlyList<string> Locales { get; }
        public bool TryGet(string key, out LocalizedTextEntry entry)
        {
            entry = null;
            return key != null && _entries.TryGetValue(key, out entry);
        }
        public LocalizedTextEntry Get(string key) => TryGet(key, out var value) ? value :
            throw new KeyNotFoundException($"Localization key not found: {key}");
        public bool Supports(string locale)
        {
            for (var i = 0; i < Locales.Count; i++) if (Locales[i] == locale) return true;
            return false;
        }
        public static LocalizationTextCatalog Parse(JToken root)
        {
            var entries = new Dictionary<string, LocalizedTextEntry>(StringComparer.Ordinal);
            List<string> locales = null;
            foreach (var rowNode in ConfigValue.EnumerateRows(root, 1, Table.Name))
            {
                var row = rowNode.Row;
                var key = ConfigValue.ReadString(row, "Key", Table.Name);
                if (string.IsNullOrWhiteSpace(key) || key != key.Trim() || key != rowNode.Keys[0].Value<string>())
                    throw new ConfigDataException("Localization key is blank, untrimmed or differs from its map path.");
                var translations = new Dictionary<string, string>(StringComparer.Ordinal);
                int? signature = null;
                foreach (var property in row.Properties())
                {
                    if (property.Name == "Key") continue;
                    ValidateLocale(property.Name);
                    if (property.Value.Type != JTokenType.String && property.Value.Type != JTokenType.Null)
                        throw new ConfigDataException($"Translation {key}/{property.Name} must be string or null.");
                    var text = property.Value.Value<string>();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var arguments = ValidateFormat(text);
                        if (signature.HasValue && signature != arguments)
                            throw new ConfigDataException($"Translations for {key} disagree on argument indices.");
                        signature = arguments;
                    }
                    translations.Add(property.Name, text);
                }
                if (!translations.ContainsKey("en") || !translations.ContainsKey("zh-CN"))
                    throw new ConfigDataException("Locale columns must include en and zh-CN.");
                if (locales == null) { locales = new List<string>(translations.Keys); locales.Sort(StringComparer.Ordinal); }
                if (translations.Count != locales.Count) throw new ConfigDataException("Locale columns differ between rows.");
                foreach (var locale in locales)
                    if (!translations.ContainsKey(locale)) throw new ConfigDataException("Locale columns differ between rows.");
                entries.Add(key, new LocalizedTextEntry(key, translations));
            }
            if (entries.Count == 0) throw new ConfigDataException("Localization table cannot be empty.");
            return new LocalizationTextCatalog(entries, locales);
        }
        public static void ValidateLocale(string locale)
        {
            var supported = false;
            foreach (var candidate in SupportedLocales) if (candidate == locale) { supported = true; break; }
            if (!supported) throw new ConfigDataException($"Unsupported locale: {locale}. Supported: en, zh-CN, fr.");
            try { CultureInfo.GetCultureInfo(locale); }
            catch (CultureNotFoundException error) { throw new ConfigDataException($"Unknown locale: {locale}", error); }
        }
        public static int ValidateFormat(string text)
        {
            var mask = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '{' && c != '}') continue;
                if (i + 1 < text.Length && text[i + 1] == c) { i++; continue; }
                if (c == '}') throw new ConfigDataException("Unmatched closing brace in localized text.");
                var end = text.IndexOf('}', i + 1);
                if (end < 0) throw new ConfigDataException("Unclosed localized argument.");
                var argument = text.Substring(i + 1, end - i - 1);
                if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                    index > 15 || argument != index.ToString(CultureInfo.InvariantCulture))
                    throw new ConfigDataException("Localized formats support {0}..{15} and escaped braces only.");
                mask |= 1 << index;
                i = end;
            }
            return mask;
        }
    }

    public sealed class LocalizedTextEntry
    {
        internal LocalizedTextEntry(string key, Dictionary<string, string> translations)
        { Key = key; Translations = new ReadOnlyDictionary<string, string>(translations); }
        public string Key { get; }
        public IReadOnlyDictionary<string, string> Translations { get; }
    }
}
