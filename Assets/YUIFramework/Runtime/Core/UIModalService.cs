using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIModalService : IDisposable
    {
        private readonly UIRoot _root;
        private readonly UILayerManager _layers;
        private readonly UIInteractionController _interaction;
        private readonly List<BaseContext> _stack = new List<BaseContext>();
        private readonly GameObject _maskObject;
        private readonly RectTransform _maskRect;
        private readonly Canvas _maskCanvas;
        private bool _disposed;

        internal UIModalService(
            UIRoot root,
            UILayerManager layers,
            UIInteractionController interaction)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            _maskObject = new GameObject(
                "ModalMask",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(Image));
            _maskRect = _maskObject.GetComponent<RectTransform>();
            _maskCanvas = _maskObject.GetComponent<Canvas>();
            _maskCanvas.overrideSorting = true;
            var image = _maskObject.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.5f);
            image.raycastTarget = true;
            _maskObject.SetActive(false);
        }

        public int Count => _stack.Count;
        public BaseContext Top => _stack.Count == 0 ? null : _stack[_stack.Count - 1];
        public GameObject MaskObject => _maskObject;

        public void Activate(BaseContext context)
        {
            ThrowIfDisposed();
            if (!context.IsModal || context.SortingLease == null)
            {
                return;
            }

            _stack.Remove(context);
            _stack.Add(context);
            Apply();
        }

        public void Deactivate(BaseContext context)
        {
            if (_disposed || context == null)
            {
                return;
            }

            if (_stack.Remove(context))
            {
                Apply();
            }
        }

        public void Apply()
        {
            if (_stack.Count == 0)
            {
                _maskObject.SetActive(false);
                _interaction.Apply();
                return;
            }

            for (var i = _stack.Count - 1; i >= 0; i--)
            {
                var context = _stack[i];
                if (context?.ViewObject == null ||
                    !context.ViewObject.activeInHierarchy ||
                    context.SortingLease == null ||
                    context.SortingLease.IsDisposed)
                {
                    _stack.RemoveAt(i);
                }
            }

            var top = Top;
            if (top == null)
            {
                _maskObject.SetActive(false);
                _interaction.Apply();
                return;
            }

            _maskRect.SetParent(_root.GetLayerRoot(top.Layer), false);
            _maskRect.anchorMin = Vector2.zero;
            _maskRect.anchorMax = Vector2.one;
            _maskRect.offsetMin = Vector2.zero;
            _maskRect.offsetMax = Vector2.zero;
            _maskCanvas.overrideSorting = true;
            _maskCanvas.sortingOrder = top.SortingLease.SortingOrder - 1;
            _maskObject.SetActive(true);
            _interaction.Apply();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stack.Clear();
            if (_maskObject != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(_maskObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(_maskObject);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UIModalService));
            }
        }
    }
}
