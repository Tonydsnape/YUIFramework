using System;
using System.Collections.Generic;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 负责层级根节点访问与同层排序分配。
    /// </summary>
    public class UILayerManager
    {
        private const int SortingStep = 10;
        private readonly UIRoot _uiRoot;
        private readonly Dictionary<UILayer, int> _sortingCursor = new Dictionary<UILayer, int>();

        public UILayerManager(UIRoot uiRoot)
        {
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                _sortingCursor[layer] = (int)layer;
            }
        }

        public RectTransform GetLayer(UILayer layer)
        {
            return _uiRoot.GetLayerRoot(layer);
        }

        public void AddToLayer(UILayer layer, RectTransform view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            view.SetParent(GetLayer(layer), false);
            view.SetAsLastSibling();

            var canvas = view.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = AllocSortingOrder(layer);
            }
        }

        public int AllocSortingOrder(UILayer layer)
        {
            var next = _sortingCursor[layer] + SortingStep;
            _sortingCursor[layer] = next;
            return next;
        }
    }
}
