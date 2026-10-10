using System;
using System.Globalization;

namespace YUIFramework.Localization
{
    public interface ILocalizedTextFormatter
    {
        string Format(string template, object[] arguments, CultureInfo culture);
        string Date(DateTime value, CultureInfo culture);
        string Currency(decimal value, CultureInfo culture);
        string PluralKey(string oneKey, string otherKey, decimal count, CultureInfo culture);
    }
    public sealed class DefaultLocalizedTextFormatter : ILocalizedTextFormatter
    {
        public string Format(string template, object[] arguments, CultureInfo culture) => string.Format(culture, template, arguments);
        public string Date(DateTime value, CultureInfo culture) => value.ToString("d", culture);
        public string Currency(decimal value, CultureInfo culture) => value.ToString("C", culture);
        // Explicit one/other hook, not a CLDR plural-rules implementation.
        public string PluralKey(string oneKey, string otherKey, decimal count, CultureInfo culture) => count == 1 ? oneKey : otherKey;
    }
    public readonly struct LocalizedTextLayout
    {
        public LocalizedTextLayout(string text, bool rightToLeft = false) { Text = text; RightToLeft = rightToLeft; }
        public string Text { get; }
        public bool RightToLeft { get; }
    }
    public interface ILocalizedTextLayout
    {
        LocalizedTextLayout Prepare(string text, string locale);
    }
    public sealed class LeftToRightTextLayout : ILocalizedTextLayout
    {
        public LocalizedTextLayout Prepare(string text, string locale) => new LocalizedTextLayout(text);
    }
}
