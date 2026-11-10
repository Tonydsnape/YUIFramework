using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.ExceptionServices;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Localization
{
    [DisallowMultipleComponent]
    public sealed class ConfigPresentationBinding : MonoBehaviour
    {
        [SerializeField] private string colorToken = "";
        [SerializeField] private string fontToken = "";
        [SerializeField] private string spriteToken = "";
        [SerializeField] private string materialToken = "";
        private TMP_Text _text;
        private Graphic _graphic;
        private Image _image;
        private TMP_FontAsset _font;
        private Color _color;
        private Material _material, _fontMaterial;
        private Sprite _sprite;
        private float _size, _minSize, _maxSize;
        private UIPresentationService _service;
        private BaseContext _context;
        private CancellationTokenRegistration _display;
        private CancellationTokenSource _pending;
        private BindingToken _leases;
        private long _generation, _binding;
        private bool _subscribed, _captured;
        public Exception LastFailure { get; private set; }
        private Task _refresh = Task.CompletedTask;
        public UniTask LastRefresh => _refresh.AsUniTask();
        public void SetTokens(string color = "", string font = "", string sprite = "", string material = "")
        { colorToken = color; fontToken = font; spriteToken = sprite; materialToken = material; Schedule(); }
        public IDisposable Bind(UIPresentationService service, BaseContext context = null)
        {
            if (service == null || service.IsDisposed) throw new ArgumentException("A live presentation service is required.");
            if (context != null && (!context.DisplayToken.CanBeCanceled || context.DisplayToken.IsCancellationRequested))
                throw new InvalidOperationException("Bind in the active display scope.");
            Unbind();
            _graphic = GetComponent<Graphic>(); _text = GetComponent<TMP_Text>(); _image = GetComponent<Image>();
            if (!_graphic) throw new InvalidOperationException("Presentation binding needs a Graphic.");
            _color = _graphic.color; _material = _graphic.material;
            if (_text) { _font = _text.font; _fontMaterial = _text.fontSharedMaterial; _size = _text.fontSize;
                _minSize = _text.fontSizeMin; _maxSize = _text.fontSizeMax; }
            if (_image) _sprite = _image.sprite;
            _captured = true; _service = service; _context = context;
            var binding = ++_binding;
            if (context != null) _display = context.DisplayToken.Register(Unbind);
            if (isActiveAndEnabled) { Subscribe(); Schedule(); }
            return new BindingToken(() => { if (this && _binding == binding) Unbind(); });
        }
        private void Subscribe()
        {
            if (_subscribed || _service == null) return;
            _subscribed = true; _service.Changed += Schedule;
            if (_context != null) _context.VisibilityChanged += OnVisibility;
        }
        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false; _service.Changed -= Schedule;
            if (_context != null) _context.VisibilityChanged -= OnVisibility;
        }
        private void OnVisibility(UIVisibilityState before, UIVisibilityState after) { Schedule(); }
        private void Schedule()
        {
            if (_service == null) return;
            _refresh = RefreshAsync().AsTask();
            _refresh.AsUniTask().Forget(error => Debug.LogException(error, this));
        }
        public async UniTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            var generation = ++_generation;
            CancelPending();
            if (_service == null || !isActiveAndEnabled || !_service.IsReady)
            { RestoreAndRelease(); return; }
            if (_context?.IsSuspended == true) return;
            var service = _service;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pending = cancellation;
            var token = cancellation.Token;
            var leases = new BindingToken();
            Exception failure = null;
            try
            {
                var color = string.IsNullOrEmpty(colorToken) ? _color : service.Color(new UIThemeToken<Color>(colorToken));
                var size = service.Accessibility.FontScale;
                TMP_FontAsset font = null;
                Sprite sprite = null;
                Material material = null;
                if (!string.IsNullOrEmpty(fontToken))
                {
                    if (!_text) throw new InvalidOperationException("Font tokens require TMP_Text.");
                    font = await Acquire(service, new UIThemeToken<TMP_FontAsset>(fontToken), leases, token);
                }
                if (!string.IsNullOrEmpty(spriteToken))
                {
                    if (!_image) throw new InvalidOperationException("Sprite tokens require Image.");
                    sprite = await Acquire(service, new UIThemeToken<Sprite>(spriteToken), leases, token);
                }
                if (!string.IsNullOrEmpty(materialToken))
                    material = await Acquire(service, new UIThemeToken<Material>(materialToken), leases, token);
                token.ThrowIfCancellationRequested();
                if (!this || !_graphic || generation != _generation || _service != service)
                    throw new OperationCanceledException("Stale presentation binding.");
                _graphic.color = color;
                if (_text)
                {
                    _text.font = font ? font : _font;
                    _text.fontSize = _size * size;
                    _text.fontSizeMin = _minSize * size; _text.fontSizeMax = _maxSize * size;
                }
                if (_image) _image.sprite = sprite ? sprite : _sprite;
                if (!string.IsNullOrEmpty(materialToken))
                {
                    if (_text) _text.fontSharedMaterial = material;
                    else _graphic.material = material;
                }
                else if (_text) _text.fontSharedMaterial = font ? font.material : _fontMaterial;
                else _graphic.material = _material;
                var old = _leases;
                _leases = leases; leases = null;
                LastFailure = null;
                old?.Dispose();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested &&
                (token.IsCancellationRequested || generation != _generation)) { }
            catch (Exception error) { failure = error; }
            finally
            {
                try { leases?.Dispose(); }
                catch (Exception error) { failure = failure == null ? error : new AggregateException(failure, error); }
                finally
                {
                    if (ReferenceEquals(_pending, cancellation)) _pending = null;
                    cancellation.Dispose();
                }
            }
            if (failure != null) { LastFailure = failure; ExceptionDispatchInfo.Capture(failure).Throw(); }
        }
        private static async UniTask<T> Acquire<T>(UIPresentationService service, UIThemeToken<T> key, BindingToken leases,
            CancellationToken token) where T : UnityEngine.Object
        {
            var lease = await service.LoadAsync(key, token);
            if (lease == null) throw new InvalidOperationException("Resource service returned a null lease.");
            leases.Add(lease);
            token.ThrowIfCancellationRequested();
            if (!lease.Asset) throw new InvalidOperationException("Resource service returned a destroyed asset: " + key.Key);
            return lease.Asset;
        }
        private void CancelPending() { var pending = _pending; _pending = null; pending?.Cancel(); }
        private void RestoreAndRelease()
        {
            if (_captured)
            {
                if (_text) { _text.font = _font; _text.fontSharedMaterial = _fontMaterial; _text.fontSize = _size;
                    _text.fontSizeMin = _minSize; _text.fontSizeMax = _maxSize; }
                if (_graphic) { _graphic.color = _color; _graphic.material = _material; }
                if (_image) _image.sprite = _sprite;
            }
            var leases = _leases; _leases = null; leases?.Dispose();
        }
        private void Unbind()
        {
            ++_binding; ++_generation;
            Unsubscribe();
            _service = null; _context = null;
            try { CancelPending(); }
            finally { try { RestoreAndRelease(); } finally { _display.Dispose(); _captured = false; } }
        }
        private void OnEnable() { Subscribe(); Schedule(); }
        private void OnDisable()
        {
            ++_generation; Unsubscribe();
            try { CancelPending(); } finally { RestoreAndRelease(); }
        }
        private void OnDestroy() { Unbind(); }
    }
}
