using System;
using System.Collections.Generic;

namespace YUIFramework
{
    public interface IUIListDataSource
    {
        int Count { get; }
        string GetId(int index);
        int IndexOf(string id);
        event Action<UIListChange> Changed;
    }

    public readonly struct UIListChange
    {
        public UIListChange(ObservableCollectionChangeType kind, int index, int oldIndex)
        { Kind = kind; Index = index; OldIndex = oldIndex; }
        public ObservableCollectionChangeType Kind { get; }
        public int Index { get; }
        public int OldIndex { get; }
    }

    public sealed class UIListDataSource<T> : IUIListDataSource, IDisposable
    {
        private readonly ObservableCollection<T> _items;
        private readonly Func<T, string> _identity;
        private readonly Dictionary<string, int> _indices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _ids = new List<string>();
        private bool _disposed;

        public UIListDataSource(ObservableCollection<T> items, Func<T, string> identity)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Reindex();
            _items.CollectionChanging += ValidateChange;
            _items.CollectionChanged += OnChanged;
        }
        public int Count => _ids.Count;
        public T this[int index] => _items[index];
        public string GetId(int index) => _ids[index];
        public int IndexOf(string id) => id != null && _indices.TryGetValue(id, out var index) ? index : -1;
        public event Action<UIListChange> Changed;
        public bool IsNotifyingChange { get; private set; }

        private string Id(T item)
        {
            var id = _identity(item);
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("List identities must be nonempty and stable.");
            return id;
        }
        private void ValidateChange(ObservableCollectionChangedEventArgs<T> change)
        {
            if (change.ChangeType == ObservableCollectionChangeType.Add ||
                change.ChangeType == ObservableCollectionChangeType.Replace)
            {
                var id = Id(change.Item);
                if (_indices.TryGetValue(id, out var existing) &&
                    (change.ChangeType != ObservableCollectionChangeType.Replace || existing != change.Index))
                    throw new ArgumentException($"Duplicate list identity '{id}'.");
            }
            else if (change.ChangeType == ObservableCollectionChangeType.Reset)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in change.ResetItems)
                    if (!seen.Add(Id(item))) throw new ArgumentException("Duplicate identity in list reset.");
            }
        }
        private void Reindex()
        {
            _indices.Clear();
            _ids.Clear();
            for (var index = 0; index < _items.Count; index++)
            {
                var id = Id(_items[index]);
                _indices.Add(id, index);
                _ids.Add(id);
            }
        }
        private void OnChanged(ObservableCollectionChangedEventArgs<T> change)
        {
            Reindex();
            var handlers = Changed;
            if (handlers == null) return;
            List<Exception> errors = null;
            var value = new UIListChange(change.ChangeType, change.Index, change.OldIndex);
            IsNotifyingChange = true;
            try
            {
                foreach (Action<UIListChange> handler in handlers.GetInvocationList())
                {
                    try { handler(value); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
            }
            finally { IsNotifyingChange = false; }
            if (errors != null) throw new AggregateException("List observers failed.", errors);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _items.CollectionChanging -= ValidateChange;
            _items.CollectionChanged -= OnChanged;
            Changed = null;
        }
    }

    public sealed class UIListSelection : IDisposable
    {
        private readonly IUIListDataSource _source;
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _removed = new List<string>();
        public UIListSelection(IUIListDataSource source, bool multiple = false)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Multiple = multiple;
            _source.Changed += Reconcile;
        }
        public bool Multiple { get; }
        public int Count => _selected.Count;
        public event Action Changed;
        public bool Contains(string id) => _selected.Contains(id);
        public void Set(string id, bool selected)
        {
            if (_source.IndexOf(id) < 0) throw new ArgumentException("Selection identity is absent.", nameof(id));
            if (selected && !Multiple && (_selected.Count != 1 || !_selected.Contains(id))) _selected.Clear();
            var changed = selected ? _selected.Add(id) : _selected.Remove(id);
            if (changed) Changed?.Invoke();
        }
        private void Reconcile(UIListChange change)
        {
            _removed.Clear();
            foreach (var id in _selected) if (_source.IndexOf(id) < 0) _removed.Add(id);
            foreach (var id in _removed) _selected.Remove(id);
            if (_removed.Count > 0) Changed?.Invoke();
        }
        public void Dispose() { _source.Changed -= Reconcile; Changed = null; _selected.Clear(); }
    }
}
