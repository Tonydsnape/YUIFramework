using System;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIScalePolicy
    {
        public UIScalePolicy(Vector2 referenceResolution, CanvasScaler.ScreenMatchMode mode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight,
            float match = 0.5f)
        {
            if (!float.IsFinite(referenceResolution.x) || !float.IsFinite(referenceResolution.y) ||
                referenceResolution.x <= 0 || referenceResolution.y <= 0 || !float.IsFinite(match) || match < 0 || match > 1 ||
                !Enum.IsDefined(typeof(CanvasScaler.ScreenMatchMode), mode)) throw new ArgumentException("Invalid UI scale policy.");
            ReferenceResolution = referenceResolution; Mode = mode; Match = match;
        }
        public Vector2 ReferenceResolution { get; }
        public CanvasScaler.ScreenMatchMode Mode { get; }
        public float Match { get; }
        public static UIScalePolicy Default { get; } = new UIScalePolicy(new Vector2(1920, 1080), match: 0);
    }

    public readonly struct UIScreenFrame : IEquatable<UIScreenFrame>
    {
        public UIScreenFrame(Vector2 size, Rect safeArea)
        {
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0 ||
                !float.IsFinite(safeArea.x) || !float.IsFinite(safeArea.y) || !float.IsFinite(safeArea.width) ||
                !float.IsFinite(safeArea.height) || safeArea.width < 0 || safeArea.height < 0 ||
                safeArea.xMin < 0 || safeArea.yMin < 0 || safeArea.xMax > size.x || safeArea.yMax > size.y)
                throw new ArgumentException("Safe area must be within a positive screen.");
            Size = size; SafeArea = safeArea;
        }
        public Vector2 Size { get; }
        public Rect SafeArea { get; }
        public bool Equals(UIScreenFrame other) => Size == other.Size && SafeArea == other.SafeArea;
    }
    public interface IUIScreenSource { UIScreenFrame Read(); }
    public sealed class UnityUIScreenSource : IUIScreenSource
    {
        public UIScreenFrame Read() => new UIScreenFrame(new Vector2(Screen.width, Screen.height), Screen.safeArea);
    }

    // One cheap screen observation per root, not per localized control.
    public sealed class UIScreenLayout : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _root;
        private IUIScreenSource _source;
        private UIScreenFrame _frame;
        private Rect _viewport, _rootRect;
        private bool _initialized;
        public event Action Changed;
        public UIScreenFrame Frame => _frame;
        public Rect NormalizedViewport => _viewport;
        public void Initialize(Canvas canvas, IUIScreenSource source = null)
        {
            _canvas = canvas ? canvas : throw new ArgumentNullException(nameof(canvas));
            _root = (RectTransform)canvas.transform;
            _source = source ?? new UnityUIScreenSource();
            Refresh();
        }
        public void Refresh()
        {
            if (!_canvas || _source == null) return;
            var pixels = _canvas.pixelRect;
            var viewport = new Rect(pixels.x / Screen.width, pixels.y / Screen.height,
                pixels.width / Screen.width, pixels.height / Screen.height);
            Apply(_source.Read(), viewport);
        }
        // Preview/test injection never changes the scaler, screen resolution or serialized settings.
        public void Apply(UIScreenFrame frame, Rect normalizedViewport)
        {
            if (!_root) throw new InvalidOperationException("Screen layout is not initialized.");
            if (frame.Size.x <= 0 || frame.Size.y <= 0 || !float.IsFinite(normalizedViewport.x) ||
                !float.IsFinite(normalizedViewport.y) || !float.IsFinite(normalizedViewport.width) ||
                !float.IsFinite(normalizedViewport.height) || normalizedViewport.width <= 0 || normalizedViewport.height <= 0)
                throw new ArgumentException("Canvas viewport must have positive area.");
            if (_initialized && _frame.Equals(frame) && _viewport == normalizedViewport && _rootRect == _root.rect) return;
            _initialized = true; _frame = frame; _viewport = normalizedViewport; _rootRect = _root.rect;
            Exception failures = null;
            var handlers = Changed?.GetInvocationList();
            if (handlers != null) foreach (Action handler in handlers)
                try { handler(); } catch (Exception error) { failures = failures == null ? error : new AggregateException(failures, error); }
            if (failures != null) throw new AggregateException("Screen layout observers failed.", failures);
        }
        public Rect SafeRectIn(RectTransform parent)
        {
            if (!_initialized || !parent || !parent.IsChildOf(_root) && parent != _root)
                throw new ArgumentException("Safe content must be a descendant of the initialized canvas.");
            if (Quaternion.Angle(parent.rotation, _root.rotation) > .01f)
                throw new InvalidOperationException("Safe-area content requires axes aligned with its canvas.");
            var min = new Vector2(_frame.SafeArea.xMin / _frame.Size.x, _frame.SafeArea.yMin / _frame.Size.y);
            var max = new Vector2(_frame.SafeArea.xMax / _frame.Size.x, _frame.SafeArea.yMax / _frame.Size.y);
            min = (min - _viewport.position) / _viewport.size;
            max = (max - _viewport.position) / _viewport.size;
            var a = parent.InverseTransformPoint(_root.TransformPoint(_root.rect.min + _root.rect.size * min));
            var b = parent.InverseTransformPoint(_root.TransformPoint(_root.rect.min + _root.rect.size * max));
            var lower = Vector2.Min(parent.rect.max, Vector2.Max(parent.rect.min, Vector2.Min(a, b)));
            var upper = Vector2.Min(parent.rect.max, Vector2.Max(a, b));
            upper = Vector2.Max(lower, upper);
            return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
        }
        private void Update() { Refresh(); }
    }
}
