using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using YUIFramework.Configuration;

namespace YUIFramework.Localization
{
    public enum LocalizedTextStatus { Exact, Fallback, NotReady, MissingKey, MissingTranslation, UnsupportedLocale, InvalidFormat, Disposed }

    public readonly struct LocalizedTextResult
    {
        internal LocalizedTextResult(LocalizedTextStatus status, string key, string requested, string resolved,
            string text, Exception error = null)
        { Status = status; Key = key; RequestedLocale = requested; ResolvedLocale = resolved; Text = text; Error = error; }
        public LocalizedTextStatus Status { get; }
        public string Key { get; }
        public string RequestedLocale { get; }
        public string ResolvedLocale { get; }
        public string Text { get; }
        public Exception Error { get; }
        public bool HasText => Status == LocalizedTextStatus.Exact || Status == LocalizedTextStatus.Fallback;
        public override string ToString() => $"Localization {Status}: '{Key}', requested '{RequestedLocale}', resolved '{ResolvedLocale}'.";
    }

    public sealed class LocalizedTextException : InvalidOperationException
    {
        public LocalizedTextException(LocalizedTextResult result) : base(result.ToString(), result.Error) { Result = result; }
        public LocalizedTextResult Result { get; }
    }

    // Borrows ConfigService. All lookups use its currently published, immutable table.
    public sealed class TextLocalizationService : IDisposable
    {
        private readonly ConfigService _configs;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private bool _disposed, _notifying;
        public TextLocalizationService(ConfigService configs, string locale = "zh-CN", string fallbackLocale = "en",
            ILocalizedTextFormatter formatter = null, ILocalizedTextLayout layout = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            if (!configs.IsTableRegistered(LocalizationTextCatalog.Table))
                throw new ArgumentException("ConfigService must register LocalizationTextCatalog.Table.", nameof(configs));
            LocalizationTextCatalog.ValidateLocale(locale);
            LocalizationTextCatalog.ValidateLocale(fallbackLocale);
            Locale = locale; FallbackLocale = fallbackLocale;
            Formatter = formatter ?? new DefaultLocalizedTextFormatter();
            Layout = layout ?? new LeftToRightTextLayout();
            if (Catalog != null && (!Catalog.Supports(locale) || !Catalog.Supports(fallbackLocale)))
                throw new ArgumentException("Locale/fallback is not present in the loaded catalog.");
            _configs.SnapshotChanged += OnSnapshotChanged;
        }
        private LocalizationTextCatalog Catalog => !_disposed && _configs.Snapshot != null &&
            _configs.Snapshot.TryGet(LocalizationTextCatalog.Table, out var table) ? table : null;
        public string Locale { get; private set; }
        public string FallbackLocale { get; }
        public ILocalizedTextFormatter Formatter { get; }
        public ILocalizedTextLayout Layout { get; }
        public string FormatDate(DateTime value) { CheckThread(); return Formatter.Date(value, CultureInfo.GetCultureInfo(Locale)); }
        public string FormatCurrency(decimal value) { CheckThread(); return Formatter.Currency(value, CultureInfo.GetCultureInfo(Locale)); }
        public string GetPlural(string oneKey, string otherKey, decimal count, params object[] arguments) =>
            Get(Formatter.PluralKey(oneKey, otherKey, count, CultureInfo.GetCultureInfo(Locale)), arguments);
        public bool IsReady => Catalog != null;
        internal ConfigService ConfigOwner => _configs;
        public bool IsDisposed => _disposed;
        public event Action Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

        public void SetLocale(string locale)
        {
            CheckMutation();
            LocalizationTextCatalog.ValidateLocale(locale);
            if (Catalog != null && !Catalog.Supports(locale))
                throw new ArgumentException($"Unsupported locale: {locale}", nameof(locale));
            if (Locale == locale) return;
            Locale = locale;
            Notify();
        }
        public string Get(string key, params object[] arguments)
        {
            var result = Resolve(key, arguments);
            if (result.Status != LocalizedTextStatus.Exact) throw new LocalizedTextException(result);
            return result.Text;
        }
        public bool TryGet(string key, out string text, out LocalizedTextResult result, params object[] arguments)
        {
            result = Resolve(key, arguments);
            text = result.Status == LocalizedTextStatus.Exact ? result.Text : null;
            return result.Status == LocalizedTextStatus.Exact;
        }
        public LocalizedTextResult Resolve(string key, params object[] arguments)
        {
            CheckThread();
            if (_disposed) return Result(LocalizedTextStatus.Disposed, key);
            var catalog = Catalog;
            if (catalog == null) return Result(LocalizedTextStatus.NotReady, key);
            if (!catalog.Supports(Locale) || !catalog.Supports(FallbackLocale))
                return Result(LocalizedTextStatus.UnsupportedLocale, key);
            if (!catalog.TryGet(key, out var entry)) return Result(LocalizedTextStatus.MissingKey, key);
            var resolved = Locale;
            var text = entry.Translations[Locale];
            var status = LocalizedTextStatus.Exact;
            if (string.IsNullOrWhiteSpace(text))
            {
                resolved = FallbackLocale;
                text = entry.Translations[resolved];
                status = LocalizedTextStatus.Fallback;
            }
            if (string.IsNullOrWhiteSpace(text)) return Result(LocalizedTextStatus.MissingTranslation, key);
            try
            {
                text = Formatter.Format(text, arguments ?? Array.Empty<object>(), CultureInfo.GetCultureInfo(resolved));
            }
            catch (FormatException error) { return Result(LocalizedTextStatus.InvalidFormat, key, resolved, error: error); }
            return Result(status, key, resolved, text);
        }
        private LocalizedTextResult Result(LocalizedTextStatus status, string key, string resolved = null,
            string text = null, Exception error = null) => new LocalizedTextResult(status, key, Locale, resolved, text, error);
        private void OnSnapshotChanged() { if (!_disposed) Notify(); }
        private void Notify()
        {
            List<Exception> failures = null;
            var alreadyNotifying = _notifying;
            _notifying = true;
            try
            {
                var handlers = Changed?.GetInvocationList();
                if (handlers != null)
                    foreach (Action handler in handlers)
                        try { handler(); }
                        catch (Exception error) { (failures ??= new List<Exception>()).Add(error); }
            }
            finally { _notifying = alreadyNotifying; }
            if (failures != null) throw new AggregateException("Localization changed, but notification failed.", failures);
        }
        public void Dispose()
        {
            CheckThread();
            if (_disposed) return;
            CheckMutation();
            _disposed = true;
            _configs.SnapshotChanged -= OnSnapshotChanged;
            try { Notify(); }
            finally { Changed = null; }
        }
        private void CheckMutation()
        {
            CheckThread();
            if (_disposed) throw new ObjectDisposedException(nameof(TextLocalizationService));
            if (_notifying) throw new InvalidOperationException("Localization mutation during Changed is not supported.");
        }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("Localization must be used on its Unity owning thread.");
        }
    }
}
