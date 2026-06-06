using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// 全局 UI 根节点，负责 Canvas、EventSystem 与分层根节点初始化。
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        private static UIRoot _instance;
        private readonly Dictionary<UILayer, RectTransform> _layerRoots = new Dictionary<UILayer, RectTransform>();
        private bool _built;

        public static UIRoot Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<UIRoot>();
                    if (_instance == null)
                    {
                        var rootObject = new GameObject(nameof(UIRoot));
                        _instance = rootObject.AddComponent<UIRoot>();
                    }
                }

                _instance.BuildIfNeeded();
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            BuildIfNeeded();
        }

        public RectTransform GetLayerRoot(UILayer layer)
        {
            BuildIfNeeded();
            if (_layerRoots.TryGetValue(layer, out var layerRoot) && layerRoot != null)
            {
                return layerRoot;
            }

            var rebuilt = CreateOrGetLayerRoot(layer);
            if (rebuilt != null)
            {
                return rebuilt;
            }

            throw new InvalidOperationException($"UILayer root not found after rebuild: {layer}");
        }

        public void BuildIfNeeded()
        {
            EnsureRootComponents();
            EnsureEventSystem();

            if (_built && HasAllLayerRoots())
            {
                return;
            }

            BuildLayerRoots();
        }

        private void EnsureRootComponents()
        {
            var rootRect = gameObject.GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            StretchToFullScreen(rootRect);

            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _ = gameObject.GetComponent<GraphicRaycaster>() ?? gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventObject = new GameObject("EventSystem");
            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<StandaloneInputModule>();
            DontDestroyOnLoad(eventObject);
        }

        private void BuildLayerRoots()
        {
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                _layerRoots[layer] = CreateOrGetLayerRoot(layer);
            }

            _built = true;
        }

        private bool HasAllLayerRoots()
        {
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                if (!_layerRoots.TryGetValue(layer, out var root) || root == null)
                {
                    _built = false;
                    return false;
                }
            }

            return true;
        }

        private RectTransform CreateOrGetLayerRoot(UILayer layer)
        {
            if (_layerRoots.TryGetValue(layer, out var cachedRoot) && cachedRoot != null)
            {
                EnsureLayerComponents(cachedRoot, layer);
                return cachedRoot;
            }

            var layerName = $"Layer_{layer}";
            var existing = transform.Find(layerName);
            var rect = existing as RectTransform;
            if (rect == null && existing != null)
            {
                rect = existing.gameObject.GetComponent<RectTransform>() ?? existing.gameObject.AddComponent<RectTransform>();
            }

            if (rect == null)
            {
                var layerObject = new GameObject(layerName, typeof(RectTransform));
                rect = layerObject.GetComponent<RectTransform>();
                rect.SetParent(transform, false);
            }

            EnsureLayerComponents(rect, layer);
            _layerRoots[layer] = rect;
            return rect;
        }

        private static void EnsureLayerComponents(RectTransform rect, UILayer layer)
        {
            StretchToFullScreen(rect);

            var layerCanvas = rect.gameObject.GetComponent<Canvas>() ?? rect.gameObject.AddComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.sortingOrder = (int)layer;

            _ = rect.gameObject.GetComponent<GraphicRaycaster>() ?? rect.gameObject.AddComponent<GraphicRaycaster>();
        }

        private static void StretchToFullScreen(RectTransform rect)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
