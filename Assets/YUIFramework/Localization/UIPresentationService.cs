using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Configuration;

namespace YUIFramework.Localization
{
    public readonly struct UIAccessibilityOptions
    {
        public UIAccessibilityOptions(float fontScale = 1, bool highContrast = false, bool reducedMotion = false)
        {
            if (!float.IsFinite(fontScale) || fontScale < .75f || fontScale > 2) throw new ArgumentOutOfRangeException(nameof(fontScale));
            FontScale = fontScale; HighContrast = highContrast; ReducedMotion = reducedMotion;
        }
        public float FontScale { get; }
        public bool HighContrast { get; }
        public bool ReducedMotion { get; }
    }
    public sealed class UIPresentationService : IDisposable
    {
        private readonly ConfigService _configs;
        private readonly IUIResourceService _resources;
        private readonly UITransitionRunner _transitions;
        private readonly bool _originalMotion;
        private readonly string _defaultPackage;
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private bool _notifying;
        public UIPresentationService(ConfigService configs, TextLocalizationService text, IUIResourceService resources,
            UITransitionRunner transitions = null, string theme = "light", string defaultPackage = null)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            if (!ReferenceEquals(text.ConfigOwner, configs)) throw new ArgumentException("Text and presentation must share one Config owner.", nameof(text));
            if (text.IsDisposed) throw new ObjectDisposedException(nameof(text));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            if (!configs.IsTableRegistered(UIPresentationCatalog.Table)) throw new ArgumentException("Register UIPresentationCatalog.Table.");
            Theme = theme; _defaultPackage = defaultPackage;
            if (Catalog != null && !Catalog.HasTheme(theme)) throw new ArgumentException("Unknown initial theme.");
            _transitions = transitions; _originalMotion = transitions?.ReducedMotion ?? false;
            Accessibility = new UIAccessibilityOptions(1, reducedMotion: _originalMotion);
            Text.Changed += Notify;
        }
        public TextLocalizationService Text { get; }
        public string Theme { get; private set; }
        public UIAccessibilityOptions Accessibility { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool IsReady => !IsDisposed && Text.IsReady && Catalog != null;
        public event Action Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        private UIPresentationCatalog Catalog => _configs.Snapshot != null &&
            _configs.Snapshot.TryGet(UIPresentationCatalog.Table, out var table) ? table : null;
        public void SetTheme(string theme)
        {
            CheckMutation();
            if (Catalog == null) throw new ConfigNotLoadedException(UIPresentationCatalog.Table.Name);
            if (!Catalog.HasTheme(theme)) throw new ArgumentException("Unknown theme: " + theme);
            if (Theme == theme) return;
            Theme = theme; Notify();
        }
        public void SetAccessibility(UIAccessibilityOptions options)
        {
            CheckMutation();
            if (options.FontScale < .75f) throw new ArgumentException("Use an initialized accessibility options value.");
            Accessibility = options;
            _transitions?.SetReducedMotion(options.ReducedMotion);
            Notify();
        }
        public Color Color(UIThemeToken<Color> token) => Entry(token).Color(Accessibility.HighContrast);
        private UIPresentationCatalog.Entry Entry<T>(UIThemeToken<T> token)
        {
            CheckThread();
            if (!IsReady) throw new InvalidOperationException("Presentation is not ready.");
            return Catalog.Get(Theme, Text.Locale, token);
        }
        public UniTask<IUIAssetLease<T>> LoadAsync<T>(UIThemeToken<T> token, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var entry = Entry(token);
            return _resources.LoadAssetAsync<T>(entry.Value, entry.Package ?? _defaultPackage, cancellationToken);
        }
        private void Notify()
        {
            var previous = _notifying; _notifying = true;
            List<Exception> errors = null;
            try
            {
                var handlers = Changed?.GetInvocationList();
                if (handlers != null) foreach (Action handler in handlers)
                    try { handler(); } catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            }
            finally { _notifying = previous; }
            if (errors != null) throw new AggregateException("Presentation observers failed.", errors);
        }
        private void CheckThread()
        {
            if (_thread != Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Use presentation on its owning Unity thread.");
        }
        private void CheckMutation()
        {
            CheckThread();
            if (IsDisposed) throw new ObjectDisposedException(nameof(UIPresentationService));
            if (_notifying) throw new InvalidOperationException("Do not mutate presentation during Changed.");
        }
        public void Dispose()
        {
            CheckThread();
            if (IsDisposed) return;
            CheckMutation(); IsDisposed = true;
            Text.Changed -= Notify;
            try { Notify(); }
            finally { Changed = null; if (_transitions != null && !_transitions.IsDisposed) _transitions.SetReducedMotion(_originalMotion); }
        }
    }
}
