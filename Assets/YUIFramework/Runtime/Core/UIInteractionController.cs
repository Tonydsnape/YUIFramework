using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// The only writer of layer/view raycast eligibility.
    /// </summary>
    public sealed class UIInteractionController : IDisposable
    {
        private readonly UILayerManager _layers;
        private readonly UILayerProfile _profile;
        private readonly UIFocusService _focus;
        private readonly Dictionary<BaseContext, bool> _visible =
            new Dictionary<BaseContext, bool>();
        private readonly Dictionary<BaseContext, Dictionary<GraphicRaycaster, bool>> _raycasters =
            new Dictionary<BaseContext, Dictionary<GraphicRaycaster, bool>>();
        private UIInputLockService _inputLocks;
        private UIModalService _modals;
        private bool _disposed;

        internal UIInteractionController(
            UILayerManager layers,
            UILayerProfile profile,
            UIFocusService focus)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        }

        internal void Bind(UIInputLockService inputLocks, UIModalService modals)
        {
            _inputLocks = inputLocks ?? throw new ArgumentNullException(nameof(inputLocks));
            _modals = modals ?? throw new ArgumentNullException(nameof(modals));
            Apply();
        }

        public void SetVisible(BaseContext context, bool visible)
        {
            ThrowIfDisposed();
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _visible[context] = visible;
            CaptureRaycasters(context);
            Apply();
        }

        public void Remove(BaseContext context)
        {
            if (_disposed || context == null)
            {
                return;
            }

            _visible.Remove(context);
            if (_raycasters.TryGetValue(context, out var known))
            {
                foreach (var raycaster in known)
                {
                    if (raycaster.Key != null)
                    {
                        raycaster.Key.enabled = raycaster.Value;
                    }
                }
            }

            _raycasters.Remove(context);
            Apply();
        }

        public bool IsInteractable(BaseContext context)
        {
            if (context == null ||
                !_visible.TryGetValue(context, out var visible) ||
                !visible ||
                context.SortingLease == null ||
                context.SortingLease.IsDisposed)
            {
                return false;
            }

            var descriptor = _profile.Get(context.Layer);
            if (!descriptor.Interactable || !_inputLocks.IsLayerAllowed(context.Layer))
            {
                return false;
            }

            var topModal = _modals.Top;
            if (topModal == null)
            {
                return true;
            }

            if (ReferenceEquals(context, topModal))
            {
                return true;
            }

            return _profile.GetIndex(context.Layer) > _profile.GetIndex(topModal.Layer);
        }

        public void Apply()
        {
            if (_disposed || _inputLocks == null || _modals == null)
            {
                return;
            }

            var topModal = _modals.Top;
            var modalIndex = topModal == null ? -1 : _profile.GetIndex(topModal.Layer);
            foreach (var descriptor in _profile.Descriptors)
            {
                var layerAllowed =
                    descriptor.Interactable &&
                    _inputLocks.IsLayerAllowed(descriptor.Layer) &&
                    (modalIndex < 0 || _profile.GetIndex(descriptor.Layer) >= modalIndex);
                var rootRaycaster = _layers.GetLayer(descriptor.Layer).GetComponent<GraphicRaycaster>();
                rootRaycaster.enabled = layerAllowed;
            }

            foreach (var pair in _visible)
            {
                var context = pair.Key;
                if (context?.SortingLease?.View == null)
                {
                    continue;
                }

                CaptureRaycasters(context);
                var eligible = IsInteractable(context);
                foreach (var raycaster in _raycasters[context])
                {
                    if (raycaster.Key != null)
                    {
                        raycaster.Key.enabled = raycaster.Value && eligible;
                    }
                }
            }

            _focus.Refresh(IsInteractable);
        }

        public void Dispose()
        {
            _disposed = true;
            _visible.Clear();
            _raycasters.Clear();
            _inputLocks = null;
            _modals = null;
        }

        private void CaptureRaycasters(BaseContext context)
        {
            if (context?.ViewObject == null)
            {
                return;
            }

            if (!_raycasters.TryGetValue(context, out var known))
            {
                known = new Dictionary<GraphicRaycaster, bool>();
                _raycasters.Add(context, known);
            }

            foreach (var raycaster in context.ViewObject.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                if (!known.ContainsKey(raycaster))
                {
                    known.Add(raycaster, raycaster.enabled);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UIInteractionController));
            }
        }
    }
}
