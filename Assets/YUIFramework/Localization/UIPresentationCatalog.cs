using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using YUIFramework.Configuration;

namespace YUIFramework.Localization
{
    public readonly struct UIThemeToken<T>
    {
        public UIThemeToken(string key) { Key = !string.IsNullOrWhiteSpace(key) ? key : throw new ArgumentException("Token key required."); }
        public string Key { get; }
    }
    public sealed class UIPresentationCatalog
    {
        public static ConfigTable<UIPresentationCatalog> Table { get; } =
            new ConfigTable<UIPresentationCatalog>("UIPresentationConfig", true, Parse);
        private readonly Dictionary<string, Dictionary<string, Entry>> _groups;
        private UIPresentationCatalog(Dictionary<string, Dictionary<string, Entry>> groups) { _groups = groups; }
        public bool HasTheme(string theme) => theme != null && _groups.ContainsKey("theme." + theme);
        public Entry Get<T>(string theme, string locale, UIThemeToken<T> token)
        {
            if (!_groups.TryGetValue("theme." + theme, out var defaults) || !defaults.TryGetValue(token.Key, out var entry))
                throw new KeyNotFoundException($"Theme token {theme}/{token.Key} not found.");
            if ((typeof(T) == typeof(TMP_FontAsset) || typeof(T) == typeof(Sprite)) &&
                _groups.TryGetValue("locale." + locale, out var localized) && localized.TryGetValue(token.Key, out var replacement))
                entry = replacement;
            if (entry.Type != typeof(T)) throw new ArgumentException($"Token {token.Key} is {entry.Type.Name}, not {typeof(T).Name}.");
            return entry;
        }
        public sealed class Entry
        {
            internal Entry(Type type, string value, string contrast, string package)
            { Type = type; Value = value; HighContrast = contrast; Package = package; }
            public Type Type { get; }
            public string Value { get; }
            public string HighContrast { get; }
            public string Package { get; }
            public Color Color(bool highContrast)
            {
                if (Type != typeof(Color)) throw new InvalidOperationException("Not a color token.");
                ColorUtility.TryParseHtmlString(highContrast && HighContrast != null ? HighContrast : Value, out var color);
                return color;
            }
        }
        public static UIPresentationCatalog Parse(JToken json)
        {
            var groups = new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);
            var kinds = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var node in ConfigValue.EnumerateRows(json, 2, Table.Name))
            {
                var row = node.Row;
                var group = ConfigValue.ReadString(row, "Group", Table.Name);
                var key = ConfigValue.ReadString(row, "Key", Table.Name);
                var kind = ConfigValue.ReadString(row, "Kind", Table.Name);
                var value = ConfigValue.ReadString(row, "Value", Table.Name);
                var contrast = ConfigValue.ReadOptionalString(row, "HighContrast", Table.Name);
                var package = ConfigValue.ReadOptionalString(row, "Package", Table.Name);
                if (group != node.Keys[0].Value<string>() || key != node.Keys[1].Value<string>() ||
                    !Regex.IsMatch(group, @"^(theme\.[a-z][a-z0-9_-]*|locale\.(en|zh-CN|fr))$") ||
                    string.IsNullOrWhiteSpace(key) || key != key.Trim() || string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
                    package != null && (string.IsNullOrWhiteSpace(package) || package != package.Trim()))
                    throw new ConfigDataException("Invalid presentation token or key path.");
                var type = kind switch { "Color" => typeof(Color), "Font" => typeof(TMP_FontAsset),
                    "Sprite" => typeof(Sprite), "Material" => typeof(Material), _ => throw new ConfigDataException("Invalid token kind.") };
                if (kinds.TryGetValue(key, out var prior) && prior != type) throw new ConfigDataException("Token type varies across groups.");
                kinds[key] = type;
                if (type == typeof(Color))
                {
                    if (!ValidColor(value) || contrast != null && !ValidColor(contrast)) throw new ConfigDataException("Invalid hex color.");
                }
                else if (contrast != null) throw new ConfigDataException("Only colors support HighContrast.");
                if (group.StartsWith("locale.", StringComparison.Ordinal) && type != typeof(TMP_FontAsset) && type != typeof(Sprite))
                    throw new ConfigDataException("Locales only override fonts and sprites.");
                if (!groups.TryGetValue(group, out var entries)) groups.Add(group, entries = new Dictionary<string, Entry>(StringComparer.Ordinal));
                entries.Add(key, new Entry(type, value, contrast, package));
            }
            HashSet<string> keys = null;
            foreach (var group in groups)
            {
                if (!group.Key.StartsWith("theme.", StringComparison.Ordinal)) continue;
                if (keys == null) keys = new HashSet<string>(group.Value.Keys);
                else if (!keys.SetEquals(group.Value.Keys)) throw new ConfigDataException("Themes must have identical token keys.");
            }
            if (keys == null) throw new ConfigDataException("At least one theme required.");
            foreach (var group in groups) foreach (var key in group.Value.Keys)
                if (!keys.Contains(key)) throw new ConfigDataException("Locale override has no theme default.");
            return new UIPresentationCatalog(groups);
        }
        private static bool ValidColor(string value) => Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$");
    }
}
