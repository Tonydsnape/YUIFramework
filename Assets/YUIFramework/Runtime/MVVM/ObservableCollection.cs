using System;
using System.Collections;
using System.Collections.Generic;

namespace YUIFramework
{
    public sealed class ObservableCollection<T> : IEnumerable<T>
    {
        private readonly List<T> _items = new List<T>();
        private bool _notifying;
        public int Count => _items.Count;
        public T this[int index] => _items[index];
        public event Action<ObservableCollectionChangedEventArgs<T>> CollectionChanged;
        public event Action<ObservableCollectionChangedEventArgs<T>> CollectionChanging;

        public void Add(T item) => Insert(Count, item);

        public void Insert(int index, T item)
        {
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
            Change(new ObservableCollectionChangedEventArgs<T>(ObservableCollectionChangeType.Add, item, index),
                () => _items.Insert(index, item));
        }

        public bool Remove(T item)
        {
            var index = _items.IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            var item = _items[index];
            Change(new ObservableCollectionChangedEventArgs<T>(ObservableCollectionChangeType.Remove, item, index),
                () => _items.RemoveAt(index));
        }

        public void Replace(int index, T item)
        {
            var old = _items[index];
            Change(new ObservableCollectionChangedEventArgs<T>(ObservableCollectionChangeType.Replace, item, index, old),
                () => _items[index] = item);
        }

        public void Move(int oldIndex, int newIndex)
        {
            var item = _items[oldIndex];
            if (newIndex < 0 || newIndex >= Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            if (oldIndex == newIndex) return;
            Change(new ObservableCollectionChangedEventArgs<T>(ObservableCollectionChangeType.Move, item, newIndex, item, oldIndex),
                () => { _items.RemoveAt(oldIndex); _items.Insert(newIndex, item); });
        }

        public void Clear()
        {
            Change(new ObservableCollectionChangedEventArgs<T>(ObservableCollectionChangeType.Clear, default, -1), _items.Clear);
        }

        public void Reset(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var snapshot = new List<T>(items);
            Change(new ObservableCollectionChangedEventArgs<T>(ObservableCollectionChangeType.Reset, default, -1,
                    resetItems: snapshot.AsReadOnly()),
                () => { _items.Clear(); _items.AddRange(snapshot); });
        }

        public IDisposable Subscribe(Action<ObservableCollectionChangedEventArgs<T>> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            CollectionChanged += handler;
            return new BindingToken(() => CollectionChanged -= handler);
        }

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void Change(ObservableCollectionChangedEventArgs<T> change, Action commit)
        {
            if (_notifying) throw new InvalidOperationException("Collection mutation during change notification is not supported.");
            _notifying = true;
            try
            {
                CollectionChanging?.Invoke(change);
                commit();
                var handlers = CollectionChanged;
                if (handlers == null) return;
                List<Exception> errors = null;
                foreach (Action<ObservableCollectionChangedEventArgs<T>> handler in handlers.GetInvocationList())
                {
                    try { handler(change); }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
                }
                if (errors != null) throw new AggregateException("Collection observers failed after committing the change.", errors);
            }
            finally { _notifying = false; }
        }
    }
}
