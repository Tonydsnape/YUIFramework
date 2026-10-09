using System;
using UnityEngine;

namespace YUIFramework.Integrations
{
#pragma warning disable CS0618
    internal sealed class LegacyListDriver : IUIVirtualListDriver
    {
        private readonly UIVirtualList _view;
        private readonly ObservableCollection<int> _indices = new ObservableCollection<int>();
        private readonly UIListDataSource<int> _source;
        private readonly SuperScrollViewList<int> _list;
        private IDisposable _display;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => UIVirtualListBackend.Factory = view => new LegacyListDriver(view);
        private LegacyListDriver(UIVirtualList view)
        {
            _view = view;
            _source = new UIListDataSource<int>(_indices, value => value.ToString());
            var layout = view.Layout;
            layout.Clamp();
            if (layout.PaddingStart != 0 || layout.PaddingEnd != 0)
                throw new NotSupportedException("Legacy padding must be represented by the parent viewport in the SuperScrollView adapter.");
            _list = new SuperScrollViewList<int>((RectTransform)view.transform, _source,
                new SuperScrollViewOptions
                {
                    Layout = layout.Direction == UIVirtualListDirection.Vertical ? UIListBackendLayout.Vertical : UIListBackendLayout.Horizontal,
                    ItemSize = new Vector2(layout.ItemSize, layout.ItemSize),
                    Spacing = layout.Spacing,
                    Overscan = Mathf.Max(1, layout.ExtraVisibleCount * layout.ItemSize)
                }, view.ItemPrefab.gameObject,
                (binding, index) =>
                {
                    var item = binding.View.GetComponent<UIVirtualListItem>();
                    item.BindIndex(index);
                    _view.DataSource.BindItem(item, index);
                });
        }
        public int VisibleCount => _list.BoundItemCount;
        public void ReloadData()
        {
            _display?.Dispose();
            var indices = new int[_view.DataCount];
            for (var index = 0; index < indices.Length; index++) indices[index] = index;
            _indices.Reset(indices);
            _display = _list.BeginDisplay();
        }
        public void RefreshVisible() { if (_display == null || !_list.IsDisplaying) ReloadData(); else _list.RefreshVisible(); }
        public void ScrollToIndex(int index, bool alignToStart)
        {
            if (!alignToStart) throw new NotSupportedException("Use the typed adapter's explicit scroll offset.");
            _list.ScrollTo(index);
        }
        public void Clear() { _display?.Dispose(); _display = null; }
        public void Dispose() { Clear(); _list.Dispose(); _source.Dispose(); }
    }
#pragma warning restore CS0618
}
