using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// 轻量虚拟列表：
    /// 1. 基于 ScrollRect。
    /// 2. 固定尺寸 Item。
    /// 3. 可见区域 + 额外缓存 Item 复用。
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class UIVirtualList : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _content;
        [SerializeField] private UIVirtualListItem _itemPrefab;
        [SerializeField] private UIVirtualListLayout _layout = new UIVirtualListLayout();

        private readonly Dictionary<int, UIVirtualListItem> _visibleItems = new Dictionary<int, UIVirtualListItem>();
        private readonly Stack<UIVirtualListItem> _itemPool = new Stack<UIVirtualListItem>();

        private UnityAction<Vector2> _onScrollChanged;

        public IUIVirtualListDataSource DataSource { get; private set; }
        public int DataCount => DataSource?.Count ?? 0;
        public int VisibleCount => _visibleItems.Count;
        public UIVirtualListLayout Layout => _layout;

        private void Awake()
        {
            _scrollRect = _scrollRect ?? GetComponent<ScrollRect>();
            _content = _content ?? _scrollRect.content;
            _layout ??= new UIVirtualListLayout();

            _onScrollChanged = _ => RefreshVisible();
            _scrollRect.onValueChanged.AddListener(_onScrollChanged);
        }

        private void OnDestroy()
        {
            if (_scrollRect != null && _onScrollChanged != null)
            {
                _scrollRect.onValueChanged.RemoveListener(_onScrollChanged);
            }

            DestroyAllItems();
        }

        public void SetDataSource(IUIVirtualListDataSource dataSource)
        {
            DataSource = dataSource;
        }

        public void SetItemPrefab(UIVirtualListItem itemPrefab)
        {
            if (ReferenceEquals(_itemPrefab, itemPrefab))
            {
                return;
            }

            _itemPrefab = itemPrefab;
            DestroyAllItems();
        }

        public void ReloadData()
        {
            EnsureReady();
            _layout.Clamp();

            RecycleVisibleItems();
            UpdateContentSize();
            RefreshVisible();
        }

        public void RefreshVisible()
        {
            if (!IsReadyForRefresh())
            {
                RecycleVisibleItems();
                return;
            }

            _layout.Clamp();

            var count = DataCount;
            if (count <= 0)
            {
                RecycleVisibleItems();
                return;
            }

            CalculateVisibleRange(count, out var minIndex, out var maxIndex);

            RecycleOutOfRange(minIndex, maxIndex);
            for (var index = minIndex; index <= maxIndex; index++)
            {
                if (_visibleItems.ContainsKey(index))
                {
                    continue;
                }

                var item = GetOrCreateItem();
                _visibleItems[index] = item;
                BindItemAt(item, index);
            }
        }

        public void ScrollToIndex(int index, bool alignToStart = true)
        {
            EnsureReady();

            var count = DataCount;
            if (count <= 0)
            {
                return;
            }

            _layout.Clamp();
            var clampedIndex = Mathf.Clamp(index, 0, count - 1);
            var step = _layout.ItemSize + _layout.Spacing;
            var viewportSize = GetViewportSize();
            var contentSize = GetContentMainAxisSize();

            var targetOffset = _layout.PaddingStart + (clampedIndex * step);
            if (!alignToStart)
            {
                targetOffset = targetOffset - Mathf.Max(0f, viewportSize - _layout.ItemSize);
            }

            var maxOffset = Mathf.Max(0f, contentSize - viewportSize);
            var offset = Mathf.Clamp(targetOffset, 0f, maxOffset);
            SetScrollOffset(offset);
            RefreshVisible();
        }

        public void Clear()
        {
            RecycleVisibleItems();
            UpdateContentSize(0);
        }

        private void EnsureReady()
        {
            _scrollRect = _scrollRect ?? GetComponent<ScrollRect>();
            _content = _content ?? _scrollRect.content;
        }

        private bool IsReadyForRefresh()
        {
            EnsureReady();
            return _scrollRect != null && _content != null && _itemPrefab != null && DataSource != null;
        }

        private void UpdateContentSize(int? forcedCount = null)
        {
            if (_content == null)
            {
                return;
            }

            var count = forcedCount ?? DataCount;
            var mainAxisSize = _layout.PaddingStart + _layout.PaddingEnd;
            if (count > 0)
            {
                mainAxisSize += (count * _layout.ItemSize) + ((count - 1) * _layout.Spacing);
            }

            var size = _content.sizeDelta;
            if (_layout.Direction == UIVirtualListDirection.Vertical)
            {
                size.y = mainAxisSize;
            }
            else
            {
                size.x = mainAxisSize;
            }

            _content.sizeDelta = size;
        }

        private void CalculateVisibleRange(int count, out int minIndex, out int maxIndex)
        {
            var step = _layout.ItemSize + _layout.Spacing;
            var viewportSize = Mathf.Max(1f, GetViewportSize());
            var offset = GetScrollOffset();

            var visibleStart = Mathf.Max(0f, offset - _layout.PaddingStart);
            var visibleEnd = Mathf.Max(0f, offset + viewportSize - _layout.PaddingStart);

            minIndex = Mathf.FloorToInt(visibleStart / step) - _layout.ExtraVisibleCount;
            maxIndex = Mathf.FloorToInt(visibleEnd / step) + _layout.ExtraVisibleCount;

            minIndex = Mathf.Clamp(minIndex, 0, count - 1);
            maxIndex = Mathf.Clamp(maxIndex, 0, count - 1);
            if (maxIndex < minIndex)
            {
                maxIndex = minIndex;
            }
        }

        private void RecycleOutOfRange(int minIndex, int maxIndex)
        {
            if (_visibleItems.Count == 0)
            {
                return;
            }

            var toRecycle = new List<int>();
            foreach (var pair in _visibleItems)
            {
                if (pair.Key < minIndex || pair.Key > maxIndex)
                {
                    toRecycle.Add(pair.Key);
                }
            }

            for (var i = 0; i < toRecycle.Count; i++)
            {
                var index = toRecycle[i];
                if (_visibleItems.TryGetValue(index, out var item))
                {
                    RecycleItem(item);
                    _visibleItems.Remove(index);
                }
            }
        }

        private void RecycleVisibleItems()
        {
            if (_visibleItems.Count == 0)
            {
                return;
            }

            foreach (var pair in _visibleItems)
            {
                RecycleItem(pair.Value);
            }

            _visibleItems.Clear();
        }

        private UIVirtualListItem GetOrCreateItem()
        {
            UIVirtualListItem item;
            if (_itemPool.Count > 0)
            {
                item = _itemPool.Pop();
            }
            else
            {
                item = Instantiate(_itemPrefab);
            }

            var itemRect = item.RectTransform;
            itemRect.SetParent(_content, false);
            item.gameObject.SetActive(true);
            return item;
        }

        private void BindItemAt(UIVirtualListItem item, int index)
        {
            ApplyItemLayout(item.RectTransform, index);
            item.BindIndex(index);
            DataSource.BindItem(item, index);
        }

        private void ApplyItemLayout(RectTransform rect, int index)
        {
            var step = _layout.ItemSize + _layout.Spacing;
            if (_layout.Direction == UIVirtualListDirection.Vertical)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, _layout.ItemSize);
                rect.anchoredPosition = new Vector2(0f, -_layout.PaddingStart - (index * step));
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(_layout.ItemSize, 0f);
                rect.anchoredPosition = new Vector2(_layout.PaddingStart + (index * step), 0f);
            }
        }

        private void RecycleItem(UIVirtualListItem item)
        {
            if (item == null)
            {
                return;
            }

            item.UnbindIndex();
            item.gameObject.SetActive(false);
            item.RectTransform.SetParent(_content, false);
            _itemPool.Push(item);
        }

        private float GetScrollOffset()
        {
            return _layout.Direction == UIVirtualListDirection.Vertical
                ? Mathf.Max(0f, _content.anchoredPosition.y)
                : Mathf.Max(0f, -_content.anchoredPosition.x);
        }

        private void SetScrollOffset(float offset)
        {
            var anchoredPosition = _content.anchoredPosition;
            if (_layout.Direction == UIVirtualListDirection.Vertical)
            {
                anchoredPosition.y = offset;
            }
            else
            {
                anchoredPosition.x = -offset;
            }

            _content.anchoredPosition = anchoredPosition;
        }

        private float GetViewportSize()
        {
            var viewport = _scrollRect != null && _scrollRect.viewport != null
                ? _scrollRect.viewport
                : transform as RectTransform;

            if (viewport == null)
            {
                return 0f;
            }

            return _layout.Direction == UIVirtualListDirection.Vertical ? viewport.rect.height : viewport.rect.width;
        }

        private float GetContentMainAxisSize()
        {
            return _layout.Direction == UIVirtualListDirection.Vertical ? _content.sizeDelta.y : _content.sizeDelta.x;
        }

        private void DestroyAllItems()
        {
            RecycleVisibleItems();
            while (_itemPool.Count > 0)
            {
                var item = _itemPool.Pop();
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }
        }
    }
}
