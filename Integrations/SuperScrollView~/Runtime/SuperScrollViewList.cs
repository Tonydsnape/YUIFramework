using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using SuperScrollView;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace YUIFramework.Integrations
{
    public enum UIListBackendLayout { Vertical, Horizontal, Grid }

    public sealed class SuperScrollViewOptions
    {
        public UIListBackendLayout Layout = UIListBackendLayout.Vertical;
        public Vector2 ItemSize = new Vector2(160, 60);
        public float Spacing = 4;
        public int Columns = 2;
        public float Overscan = 100;
        public bool DynamicSize;
        internal SuperScrollViewOptions Snapshot()
        {
            if (!Enum.IsDefined(typeof(UIListBackendLayout), Layout) ||
                !FinitePositive(ItemSize.x) || !FinitePositive(ItemSize.y) ||
                float.IsNaN(Spacing) || float.IsInfinity(Spacing) || Spacing < 0 ||
                !FinitePositive(Overscan) || Columns < 1 ||
                Layout == UIListBackendLayout.Grid && DynamicSize)
                throw new ArgumentException("Invalid list options. Grid requires fixed cell size.");
            return (SuperScrollViewOptions)MemberwiseClone();
        }
        private static bool FinitePositive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
    }

    public readonly struct UIListAnchor
    {
        public UIListAnchor(string id, int index, Vector2 position)
        { Id = id; Index = index; Position = position; }
        public string Id { get; }
        public int Index { get; }
        public Vector2 Position { get; }
    }

    public sealed class SuperScrollViewList<T> : IDisposable
    {
        private readonly UIListDataSource<T> _source;
        private readonly Action<UIListItemBinding, T> _binder;
        private readonly SuperScrollViewOptions _options;
        private readonly UIListSelection _selection;
        private readonly List<UIListItemLifetime> _items = new List<UIListItemLifetime>();
        private GameObject _root;
        private GameObject _template;
        private IUIAssetLease<GameObject> _prefabLease;
        private LoopListView2 _list;
        private LoopGridView _grid;
        private BaseContext _context;
        private bool _display;
        private bool _suspended;
        private bool _disposed;
        private bool _externallyDestroyed;
        private UIListAnchor _suspendAnchor;
        private long _displayGeneration;
        private CancellationTokenRegistration _displayCancellation;
        public ScrollRect Scroll { get; private set; }
        public bool IsDisposed => _disposed;
        public bool IsDisplaying => _display && !_suspended;
        public int CreatedItemCount => _items.Count;
        public int BoundItemCount
        {
            get { var count = 0; foreach (var item in _items) if (item != null && item.IsBound) count++; return count; }
        }
        public Exception LastError { get; private set; }
        public event Action<Exception> Error;

        public static async UniTask<SuperScrollViewList<T>> CreateAsync(
            RectTransform parent, UIListDataSource<T> source, SuperScrollViewOptions options,
            IUIResourceService resources, UIResourceKey prefabKey,
            Action<UIListItemBinding, T> binder, UIListSelection selection = null,
            CancellationToken cancellationToken = default)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            var lease = await resources.LoadAssetAsync<GameObject>(prefabKey, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = new SuperScrollViewList<T>(parent, source, options, lease.Asset, binder, selection);
                result._prefabLease = lease;
                lease = null;
                return result;
            }
            finally { lease?.Dispose(); }
        }

        // The direct-prefab overload borrows the asset; use CreateAsync for provider ownership.
        public SuperScrollViewList(RectTransform parent, UIListDataSource<T> source,
            SuperScrollViewOptions options, GameObject prefab, Action<UIListItemBinding, T> binder,
            UIListSelection selection = null)
        {
            if (parent == null || prefab == null || prefab.GetComponent<RectTransform>() == null)
                throw new ArgumentException("A parent and RectTransform item prefab are required.");
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _binder = binder ?? throw new ArgumentNullException(nameof(binder));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            _selection = selection;
            try
            {
                _root = new GameObject("SuperScrollView", typeof(RectTransform));
                _root.SetActive(false);
                var rect = (RectTransform)_root.transform;
                rect.SetParent(parent, false);
                Stretch(rect);
                Scroll = _root.AddComponent<UINestedScrollRect>();
                Scroll.movementType = ScrollRect.MovementType.Clamped;
                Scroll.inertia = false;
                Scroll.horizontal = _options.Layout == UIListBackendLayout.Horizontal;
                Scroll.vertical = !Scroll.horizontal;
                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
                viewport.transform.SetParent(rect, false);
                Scroll.viewport = (RectTransform)viewport.transform;
                Stretch(Scroll.viewport);
                viewport.GetComponent<Image>().color = Color.clear;
                var content = new GameObject("Content", typeof(RectTransform));
                content.transform.SetParent(viewport.transform, false);
                Scroll.content = (RectTransform)content.transform;
                Scroll.content.sizeDelta = parent.rect.size;
                _template = Object.Instantiate(prefab, rect);
                _template.name = "YUIListTemplate";
                _template.SetActive(false);
                var templateRect = (RectTransform)_template.transform;
                templateRect.sizeDelta = _options.ItemSize;
                if (_template.GetComponent<UIListItemLifetime>() == null) _template.AddComponent<UIListItemLifetime>();
                if (_options.Layout == UIListBackendLayout.Grid)
                {
                    if (_template.GetComponent<LoopGridViewItem>() == null) _template.AddComponent<LoopGridViewItem>();
                    _grid = _root.AddComponent<RoutedLoopGridView>();
                    ((UINestedScrollRect)Scroll).SetDragParticipant((IUIListDragParticipant)_grid);
                    _grid.ItemPrefabDataList.Add(new GridViewItemPrefabConfData { mItemPrefab = _template });
                    _grid.InitGridView(0, GetGridItem, new LoopGridViewSettingParam
                    {
                        mItemSize = _options.ItemSize,
                        mItemPadding = new Vector2(_options.Spacing, _options.Spacing),
                        mGridFixedType = GridFixedType.ColumnCountFixed,
                        mFixedRowOrColumnCount = _options.Columns
                    });
                }
                else
                {
                    if (_template.GetComponent<LoopListViewItem2>() == null) _template.AddComponent<LoopListViewItem2>();
                    _list = _root.AddComponent<RoutedLoopListView>();
                    ((UINestedScrollRect)Scroll).SetDragParticipant((IUIListDragParticipant)_list);
                    _list.ArrangeType = Scroll.horizontal ? ListItemArrangeType.LeftToRight : ListItemArrangeType.TopToBottom;
                    _list.ItemPrefabDataList.Add(new ItemPrefabConfData { mItemPrefab = _template, mPadding = _options.Spacing });
                    _list.InitListView(0, GetListItem, new LoopListViewInitParam
                    {
                        mItemDefaultWithPaddingSize = (Scroll.horizontal ? _options.ItemSize.x : _options.ItemSize.y) + _options.Spacing,
                        mDistanceForNew0 = _options.Overscan, mDistanceForNew1 = _options.Overscan,
                        mDistanceForRecycle0 = _options.Overscan + 50, mDistanceForRecycle1 = _options.Overscan + 50
                    });
                }
                var host = _root.AddComponent<SuperScrollViewLifetime>();
                host.Disabled = OnRootDisabled;
                host.Destroyed = OnRootDestroyed;
            }
            catch { Dispose(); throw; }
        }

        public IDisposable BeginDisplay(BaseContext context = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_display) throw new InvalidOperationException("List already has an active display binding.");
            cancellationToken.ThrowIfCancellationRequested();
            _context = context;
            _display = true;
            var generation = ++_displayGeneration;
            try
            {
                _source.Changed += OnChanged;
                if (_selection != null) _selection.Changed += OnSelectionChanged;
                if (_context != null) _context.VisibilityChanged += OnVisibilityChanged;
                _suspended = _context != null && _context.IsSuspended;
                _root.SetActive(!_suspended);
                if (!_suspended) Reload();
                _displayCancellation = cancellationToken.Register(EndDisplay);
                return new BindingToken(() => { if (_displayGeneration == generation) EndDisplay(); });
            }
            catch { EndDisplay(); throw; }
        }

        private void EndDisplay()
        {
            if (!_display) return;
            _display = false;
            _displayGeneration++;
            _displayCancellation.Dispose();
            _source.Changed -= OnChanged;
            if (_selection != null) _selection.Changed -= OnSelectionChanged;
            if (_context != null) _context.VisibilityChanged -= OnVisibilityChanged;
            _context = null;
            List<Exception> errors = null;
            foreach (var item in _items) if (item != null) Attempt(item.EndBind, ref errors);
            if (_root != null) _root.SetActive(false);
            if (errors != null) throw new AggregateException("List display cleanup failed.", errors);
        }

        private void OnRootDisabled()
        {
            // Parent deactivation is a display boundary, but suspension keeps the display owner.
            if (!_suspended) EndDisplay();
        }
        private void OnRootDestroyed()
        {
            if (_disposed) return;
            _externallyDestroyed = true;
            _root = null;
            Dispose();
        }
        private void OnVisibilityChanged(UIVisibilityState previous, UIVisibilityState next)
        {
            SetSuspended((next & UIVisibilityState.Suspended) != 0);
        }
        public void SetSuspended(bool suspended)
        {
            ThrowIfDisposed();
            if (_suspended == suspended) return;
            if (suspended) _suspendAnchor = CaptureAnchor();
            _suspended = suspended;
            if (_suspended)
            {
                List<Exception> errors = null;
                foreach (var item in _items) if (item != null) Attempt(item.EndBind, ref errors);
                _root.SetActive(false);
                if (errors != null) throw new AggregateException("List suspension cleanup failed.", errors);
            }
            else if (_display) { _root.SetActive(true); Reload(); RestoreAnchor(_suspendAnchor); }
        }
        private void OnSelectionChanged()
        {
            if (IsDisplaying && !_source.IsNotifyingChange) RefreshVisible();
        }
        private void OnChanged(UIListChange change)
        {
            if (!IsDisplaying) return;
            var anchor = CaptureAnchor();
            if (change.Kind == ObservableCollectionChangeType.Replace)
            {
                RefreshItem(change.Index);
            }
            else
            {
                SetCount(_source.Count);
                RefreshVisible();
            }
            RestoreAnchor(anchor);
        }
        private LoopListViewItem2 GetListItem(LoopListView2 list, int index)
        {
            if (index < 0 || index >= _source.Count || !IsDisplaying) return null;
            var item = list.NewListViewItem(_template.name);
            if (item == null) throw new InvalidOperationException("SuperScrollView failed to allocate a registered item.");
            var lifetime = item.UserObjectData as UIListItemLifetime;
            if (lifetime == null)
            {
                lifetime = item.GetComponent<UIListItemLifetime>();
                item.UserObjectData = lifetime;
                _items.Add(lifetime);
            }
            Bind(lifetime, index);
            return item;
        }
        private LoopGridViewItem GetGridItem(LoopGridView grid, int index, int row, int column)
        {
            if (index < 0 || index >= _source.Count || !IsDisplaying) return null;
            var item = grid.NewListViewItem(_template.name);
            if (item == null) throw new InvalidOperationException("SuperScrollView failed to allocate a registered grid item.");
            var lifetime = item.UserObjectData as UIListItemLifetime;
            if (lifetime == null)
            {
                lifetime = item.GetComponent<UIListItemLifetime>();
                item.UserObjectData = lifetime;
                _items.Add(lifetime);
            }
            Bind(lifetime, index);
            return item;
        }
        private void Bind(UIListItemLifetime item, int index)
        {
            try
            {
                var id = _source.GetId(index);
                var binding = item.BeginBind(id, index, _selection != null && _selection.Contains(id));
                _binder(binding, _source[index]);
                var size = ((RectTransform)item.transform).rect.size;
                if (float.IsNaN(size.x) || float.IsNaN(size.y) || float.IsInfinity(size.x) ||
                    float.IsInfinity(size.y) || size.x < 1 || size.y < 1)
                    throw new InvalidOperationException("Item binding must leave a finite, positive size of at least one unit.");
            }
            catch (Exception error)
            {
                try { item.EndBind(); }
                catch (Exception cleanupError) { error = new AggregateException(error, cleanupError); }
                Report(error);
            }
        }
        public void Report(Exception error)
        {
            if (error is OperationCanceledException) return;
            LastError = error;
            if (Error != null) Error(error);
            else Debug.LogException(error);
        }
        public UIListItemLifetime GetVisibleItem(string id)
        {
            foreach (var item in _items)
                if (item != null && item.IsBound && item.Id == id) return item;
            return null;
        }
        public UIListAnchor CaptureAnchor()
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            var position = Vector2.zero;
            string id = null;
            foreach (var item in _items)
            {
                if (item == null || !item.IsBound) continue;
                var point = (Vector2)Scroll.viewport.InverseTransformPoint(item.transform.position);
                var distance = Mathf.Abs(Scroll.horizontal ? point.x : point.y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                position = point;
                best = item.Index;
                id = item.Id;
            }
            return new UIListAnchor(id, best, position);
        }
        private void RestoreAnchor(UIListAnchor anchor)
        {
            if (anchor.Id == null || _source.Count == 0) return;
            var index = _source.IndexOf(anchor.Id);
            if (index < 0) index = Mathf.Clamp(anchor.Index, 0, _source.Count - 1);
            var current = GetVisibleItem(_source.GetId(index));
            if (current == null) { ScrollTo(index); current = GetVisibleItem(_source.GetId(index)); }
            if (current == null) throw new InvalidOperationException("Unable to restore list anchor.");
            var position = (Vector2)Scroll.viewport.InverseTransformPoint(current.transform.position);
            var delta = anchor.Position - position;
            Scroll.content.anchoredPosition += Scroll.horizontal ? new Vector2(delta.x, 0) : new Vector2(0, delta.y);
            Scroll.StopMovement();
        }
        public void ScrollTo(int index, float offset = 0)
        {
            ThrowIfDisposed();
            if (!IsDisplaying) throw new InvalidOperationException("BeginDisplay is required before scrolling.");
            if (index < 0 || index >= _source.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (float.IsNaN(offset) || float.IsInfinity(offset) || offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (_list != null) _list.MovePanelToItemIndex(index, offset);
            else _grid.MovePanelToItemByIndex(index, 0, offset);
        }
        public void NotifySizeChanged(int index)
        {
            ThrowIfDisposed();
            if (!_options.DynamicSize || _list == null) throw new InvalidOperationException("Dynamic size is enabled only for List layout.");
            if (index < 0 || index >= _source.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!IsDisplaying) return;
            var anchor = CaptureAnchor();
            _list.OnItemSizeChanged(index);
            RestoreAnchor(anchor);
        }
        public void RefreshItem(int index)
        {
            ThrowIfDisposed();
            if (index < 0 || index >= _source.Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!IsDisplaying) return;
            if (_list != null) _list.RefreshItemByItemIndex(index);
            else _grid.RefreshItemByItemIndex(index);
        }
        public void RefreshVisible()
        {
            ThrowIfDisposed();
            if (!IsDisplaying) return;
            if (_list != null) _list.RefreshAllShownItem();
            else _grid.RefreshAllShownItem();
        }
        private void Reload()
        {
            SetCount(_source.Count);
            if (_source.Count > 0) { RefreshVisible(); ScrollTo(0); }
        }
        private void SetCount(int count)
        {
            if (_list != null) _list.SetListItemCount(count, false);
            else _grid.SetListItemCount(count, false);
        }
        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SuperScrollViewList<T>));
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> errors = null;
            Attempt(EndDisplay, ref errors);
            // 2.5.3 pools use synchronous native destruction; retain the prefab lease through it.
            foreach (var item in _items)
                if (item != null)
                {
                    if (_externallyDestroyed) Attempt(item.EndBind, ref errors);
                    else Attempt(() => Object.DestroyImmediate(item.gameObject), ref errors);
                }
            _items.Clear();
            if (_root != null) Attempt(() => Object.DestroyImmediate(_root), ref errors);
            _root = null;
            _template = null;
            if (_externallyDestroyed && _prefabLease != null)
                ReleaseAfterNativeDestruction(_prefabLease).Forget(Debug.LogException);
            else Attempt(() => _prefabLease?.Dispose(), ref errors);
            _prefabLease = null;
            Error = null;
            if (errors != null) throw new AggregateException("List teardown failed.", errors);
        }
        private static async UniTask ReleaseAfterNativeDestruction(IUIAssetLease<GameObject> lease)
        {
            await UniTask.NextFrame();
            lease.Dispose();
        }
        private static void Attempt(Action action, ref List<Exception> errors)
        {
            try { action(); }
            catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
        }
        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }
    }
}
