using System;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public interface IUIVirtualListDriver : IDisposable
    {
        int VisibleCount { get; }
        void ReloadData();
        void RefreshVisible();
        void ScrollToIndex(int index, bool alignToStart);
        void Clear();
    }

#pragma warning disable CS0618
    public static class UIVirtualListBackend
    {
        public static Func<UIVirtualList, IUIVirtualListDriver> Factory { get; set; }
    }
#pragma warning restore CS0618

    [Obsolete("Install the optional SuperScrollView integration and use SuperScrollViewList<T>. This facade is removed after the Y2 migration window.")]
    [RequireComponent(typeof(ScrollRect))]
    public class UIVirtualList : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _content;
        [SerializeField] private UIVirtualListItem _itemPrefab;
        [SerializeField] private UIVirtualListLayout _layout = new UIVirtualListLayout();
        private IUIVirtualListDriver _driver;
        public IUIVirtualListDataSource DataSource { get; private set; }
        public UIVirtualListItem ItemPrefab => _itemPrefab;
        public int DataCount => DataSource?.Count ?? 0;
        public int VisibleCount => _driver?.VisibleCount ?? 0;
        public UIVirtualListLayout Layout => _layout;
        public void SetDataSource(IUIVirtualListDataSource source) => DataSource = source;
        public void SetItemPrefab(UIVirtualListItem prefab)
        {
            if (_itemPrefab == prefab) return;
            _driver?.Dispose();
            _driver = null;
            _itemPrefab = prefab;
        }
        private IUIVirtualListDriver Driver
        {
            get
            {
                if (_driver != null) return _driver;
                var factory = UIVirtualListBackend.Factory ?? throw new InvalidOperationException(
                    "SuperScrollView is not installed. Run Integrations\\SuperScrollView~\\Install.ps1 with a licensed 2.5.3 source.");
                _driver = factory(this);
                return _driver;
            }
        }
        public void ReloadData() => Driver.ReloadData();
        public void RefreshVisible() => Driver.RefreshVisible();
        public void ScrollToIndex(int index, bool alignToStart = true) => Driver.ScrollToIndex(index, alignToStart);
        public void Clear() => _driver?.Clear();
        private void OnDisable() => _driver?.Clear();
        private void OnDestroy() { _driver?.Dispose(); _driver = null; }
    }
}
