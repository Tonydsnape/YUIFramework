using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Localization
{
    [DisallowMultipleComponent]
    public sealed class ConfigLocalizedText : MonoBehaviour
    {
        [SerializeField] private LocalizedTextKey textKey;
        [SerializeField] private string[] arguments = Array.Empty<string>();
        private Text _text;
        private TMP_Text _tmp;
        private Binding _binding;
        public LocalizedTextResult LastResult { get; private set; }
        public bool IsSubscribed => _binding != null && _binding.Subscribed;
        public LocalizedTextKey TextKey { get => textKey; set { textKey = value; Refresh(); } }

        private void Awake() { CacheTargets(); }
        private void CacheTargets()
        {
            if (!_text) _text = GetComponent<Text>();
            if (!_tmp) _tmp = GetComponent<TMP_Text>();
        }
        // With a Context, bind in HandleShow and optionally also TrackDisplayBinding the returned handle.
        public IDisposable Bind(TextLocalizationService service, BaseContext context = null)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (service.IsDisposed) throw new ObjectDisposedException(nameof(service));
            if (context != null && (!context.DisplayToken.CanBeCanceled || context.DisplayToken.IsCancellationRequested))
                throw new InvalidOperationException("A Context binding requires an active display scope.");
            CacheTargets();
            if (!_text && !_tmp) throw new InvalidOperationException("ConfigLocalizedText requires Text or TMP_Text on the same object.");
            _binding?.Dispose();
            var binding = new Binding(this, service, context);
            _binding = binding;
            binding.Start();
            return binding;
        }
        public void SetArguments(params string[] values)
        {
            arguments = values == null ? Array.Empty<string>() : (string[])values.Clone();
            Refresh();
        }
        public void Refresh()
        {
            var binding = _binding;
            if (!isActiveAndEnabled || binding == null) return;
            if (binding.Context?.IsSuspended == true && binding.Service.IsReady) return;
            LastResult = binding.Service.Resolve(textKey.Key, arguments);
            if (LastResult.Status == LocalizedTextStatus.NotReady || LastResult.Status == LocalizedTextStatus.Disposed)
            { Apply(string.Empty); return; }
            if (LastResult.Status == LocalizedTextStatus.Fallback) Debug.LogWarning(LastResult.ToString(), this);
            else if (!LastResult.HasText) Debug.LogError(LastResult.ToString(), this);
            var layout = binding.Service.Layout.Prepare(LastResult.HasText ? LastResult.Text : textKey.Key ?? string.Empty,
                LastResult.ResolvedLocale ?? binding.Service.Locale);
            if (_tmp) _tmp.isRightToLeftText = layout.RightToLeft;
            Apply(layout.Text);
        }
        private void Apply(string value)
        {
            if (_text && _text.text != value) _text.text = value;
            if (_tmp && _tmp.text != value) _tmp.text = value;
        }
        private void OnEnable() { _binding?.Subscribe(); Refresh(); }
        private void OnDisable() { _binding?.Unsubscribe(); }
        private void OnDestroy() { _binding?.Dispose(); }

        private sealed class Binding : IDisposable
        {
            private ConfigLocalizedText _owner;
            internal readonly TextLocalizationService Service;
            internal readonly BaseContext Context;
            private CancellationTokenRegistration _cancellation;
            private readonly bool _rightToLeft;
            internal bool Subscribed { get; private set; }
            internal Binding(ConfigLocalizedText owner, TextLocalizationService service, BaseContext context)
            { _owner = owner; Service = service; Context = context; _rightToLeft = owner._tmp && owner._tmp.isRightToLeftText; }
            internal void Start()
            {
                if (Context != null) _cancellation = Context.DisplayToken.Register(Dispose);
                if (_owner && _owner.isActiveAndEnabled) { Subscribe(); _owner.Refresh(); }
            }
            internal void Subscribe()
            {
                if (Subscribed || !_owner || Service.IsDisposed) return;
                Subscribed = true;
                Service.Changed += OnChanged;
                if (Context != null) Context.VisibilityChanged += OnVisibility;
            }
            internal void Unsubscribe()
            {
                if (!Subscribed) return;
                Subscribed = false;
                Service.Changed -= OnChanged;
                if (Context != null) Context.VisibilityChanged -= OnVisibility;
            }
            private void OnVisibility(UIVisibilityState previous, UIVisibilityState next) { OnChanged(); }
            private void OnChanged()
            {
                if (!_owner) { Dispose(); return; }
                _owner.Refresh();
                if (Service.IsDisposed) Dispose();
            }
            public void Dispose()
            {
                var owner = _owner;
                if (ReferenceEquals(owner, null)) return;
                Unsubscribe();
                _owner = null;
                _cancellation.Dispose();
                if (owner && ReferenceEquals(owner._binding, this))
                {
                    if (owner._tmp) owner._tmp.isRightToLeftText = _rightToLeft;
                    owner._binding = null;
                }
            }
        }
    }
}
