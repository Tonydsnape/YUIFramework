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
            EnsureRootComponents();
            EnsureEventSystem();
            BuildLayerRoots();
        }

        public RectTransform GetLayerRoot(UILayer layer)
        {
            if (_layerRoots.TryGetValue(layer, out var layerRoot))
            {
                return layerRoot;
            }

            throw new InvalidOperationException($"UILayer root not found: {layer}");
        }

        private void EnsureRootComponents()
        {
            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

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
            if (_layerRoots.Count > 0)
            {
                return;
            }

            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                var layerObject = new GameObject($"Layer_{layer}");
                var rect = layerObject.AddComponent<RectTransform>();
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;

                var layerCanvas = layerObject.AddComponent<Canvas>();
                layerCanvas.overrideSorting = true;
                layerCanvas.sortingOrder = (int)layer;

                layerObject.AddComponent<GraphicRaycaster>();
                _layerRoots[layer] = rect;
            }
        }
    }
}
