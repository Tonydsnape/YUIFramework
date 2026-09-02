using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// Canvas host used by <see cref="UIRootRuntime"/>. It never searches for or creates
    /// scene objects by itself.
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        private static UIRoot _active;
        private readonly Dictionary<UILayer, RectTransform> _layerRoots = new Dictionary<UILayer, RectTransform>();
        private readonly List<GameObject> _generatedLayers = new List<GameObject>();
        private UIRootRuntime _owner;

        [Obsolete("Inject UIRootRuntime into UIManager. Instance no longer creates or searches for objects.")]
        public static UIRoot Instance
        {
            get
            {
                if (_active == null)
                {
                    throw new InvalidOperationException(
                        "No active UIRoot exists. Create a UIRootRuntime or inject a complete UIRoot.");
                }

                return _active;
            }
        }

        public static UIRoot Active => _active;
        public bool IsClaimed => _owner != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
        }

        private void Awake()
        {
            if (_active == null)
            {
                _active = this;
            }
        }

        private void OnDestroy()
        {
            if (_active == this)
            {
                _active = null;
            }

            _owner = null;
            _layerRoots.Clear();
            _generatedLayers.Clear();
        }

        public RectTransform GetLayerRoot(UILayer layer)
        {
            if (_layerRoots.TryGetValue(layer, out var layerRoot))
            {
                return layerRoot;
            }

            throw new InvalidOperationException($"UILayer root not found: {layer}");
        }

        internal void Claim(UIRootRuntime owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (_owner != null && !ReferenceEquals(_owner, owner))
            {
                throw new InvalidOperationException("The UIRoot is already owned by another runtime.");
            }

            if (_active != null && _active != this && _active.IsClaimed)
            {
                throw new InvalidOperationException(
                    $"A different UIRoot is already active: {_active.name}.");
            }

            _active = this;
            _owner = owner;
        }

        internal void Release(UIRootRuntime owner)
        {
            if (!ReferenceEquals(_owner, owner))
            {
                return;
            }

            _owner = null;
            if (_active == this)
            {
                _active = null;
            }

            ClearGeneratedLayers();
        }

        internal void Configure(
            UILayerProfile profile,
            RenderMode renderMode,
            Camera eventCamera)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var rect = GetComponent<RectTransform>();
            var canvas = GetComponent<Canvas>();
            var scaler = GetComponent<CanvasScaler>();
            var raycaster = GetComponent<GraphicRaycaster>();
            if (rect == null || canvas == null || scaler == null || raycaster == null)
            {
                throw new InvalidOperationException(
                    "UIRoot requires RectTransform, Canvas, CanvasScaler, and GraphicRaycaster.");
            }

            if (renderMode != RenderMode.ScreenSpaceOverlay && eventCamera == null)
            {
                throw new ArgumentException(
                    "A camera is required for ScreenSpaceCamera and WorldSpace UIRoot modes.",
                    nameof(eventCamera));
            }

            canvas.renderMode = renderMode;
            canvas.worldCamera = renderMode == RenderMode.ScreenSpaceOverlay ? null : eventCamera;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            raycaster.enabled = true;

            ClearGeneratedLayers();
            foreach (var descriptor in profile.Descriptors)
            {
                var layerObject = new GameObject(
                    $"Layer_{descriptor.Layer}",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(GraphicRaycaster));
                var layerRect = layerObject.GetComponent<RectTransform>();
                layerRect.SetParent(transform, false);
                layerRect.anchorMin = Vector2.zero;
                layerRect.anchorMax = Vector2.one;
                layerRect.offsetMin = Vector2.zero;
                layerRect.offsetMax = Vector2.zero;
                layerRect.localScale = Vector3.one;

                var layerCanvas = layerObject.GetComponent<Canvas>();
                layerCanvas.overrideSorting = true;
                layerCanvas.sortingOrder = descriptor.SortingBase;
                layerObject.GetComponent<GraphicRaycaster>().enabled = descriptor.Interactable;
                _layerRoots.Add(descriptor.Layer, layerRect);
                _generatedLayers.Add(layerObject);
            }
        }

        private void ClearGeneratedLayers()
        {
            _layerRoots.Clear();
            for (var i = _generatedLayers.Count - 1; i >= 0; i--)
            {
                var generated = _generatedLayers[i];
                if (generated == null)
                {
                    continue;
                }

                generated.transform.SetParent(null, false);
                if (Application.isPlaying)
                {
                    Destroy(generated);
                }
                else
                {
                    DestroyImmediate(generated);
                }
            }

            _generatedLayers.Clear();
        }
    }
}
