using System;
using UnityEngine;

namespace YUIFramework
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class UISafeAreaContent : MonoBehaviour
    {
        private UIScreenLayout _layout;
        private RectTransform _rect;
        private Vector2 _anchorMin, _anchorMax, _offsetMin, _offsetMax;
        private long _generation;
        private bool _captured;
        private Matrix4x4 _parentTransform;
        private Rect _parentRect;
        public IDisposable Bind(UIScreenLayout layout)
        {
            Unbind();
            _layout = layout ? layout : throw new ArgumentNullException(nameof(layout));
            _rect = (RectTransform)transform;
            _anchorMin = _rect.anchorMin; _anchorMax = _rect.anchorMax;
            _offsetMin = _rect.offsetMin; _offsetMax = _rect.offsetMax; _captured = true;
            var generation = ++_generation;
            if (isActiveAndEnabled) Subscribe();
            return new BindingToken(() => { if (this && generation == _generation) Unbind(); });
        }
        private void Apply()
        {
            if (!_layout || !_rect || !isActiveAndEnabled) return;
            var parent = _rect.parent as RectTransform;
            var safe = _layout.SafeRectIn(parent);
            _parentTransform = parent.localToWorldMatrix; _parentRect = parent.rect;
            _rect.anchorMin = _rect.anchorMax = Vector2.zero;
            _rect.offsetMin = safe.min - parent.rect.min;
            _rect.offsetMax = safe.max - parent.rect.min;
        }
        private void Restore()
        {
            if (!_captured || !_rect) return;
            _rect.anchorMin = _anchorMin; _rect.anchorMax = _anchorMax;
            _rect.offsetMin = _offsetMin; _rect.offsetMax = _offsetMax;
        }
        private void Subscribe()
        {
            _layout.Changed += Apply;
            Canvas.willRenderCanvases += RefreshParent;
            Apply();
        }
        private void RefreshParent()
        {
            if (!_layout || !_rect || !isActiveAndEnabled) return;
            var parent = _rect.parent as RectTransform;
            if (!parent || parent.rect != _parentRect || parent.localToWorldMatrix != _parentTransform) Apply();
        }
        private void Unsubscribe()
        {
            if (_layout) _layout.Changed -= Apply;
            Canvas.willRenderCanvases -= RefreshParent;
        }
        private void OnEnable() { if (_layout) Subscribe(); }
        private void OnDisable() { Unsubscribe(); Restore(); }
        private void OnDestroy() { Unbind(); }
        private void Unbind()
        {
            ++_generation;
            Unsubscribe();
            Restore(); _layout = null; _captured = false;
        }
    }
}
