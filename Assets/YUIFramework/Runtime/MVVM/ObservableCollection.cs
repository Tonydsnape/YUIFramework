using System;
using System.Collections;
using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// 轻量可观察集合。
    /// </summary>
    public sealed class ObservableCollection<T> : IEnumerable<T>
    {
        private readonly List<T> _items = new List<T>();

        public int Count => _items.Count;
        public T this[int index] => _items[index];

        public event Action<ObservableCollectionChangedEventArgs<T>> CollectionChanged;

        public void Add(T item)
        {
            _items.Add(item);
            NotifyChanged(ObservableCollectionChangeType.Add, item, _items.Count - 1);
        }

        public bool Remove(T item)
        {
            var index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            _items.RemoveAt(index);
            NotifyChanged(ObservableCollectionChangeType.Remove, item, index);
            return true;
        }

        public void RemoveAt(int index)
        {
            var item = _items[index];
            _items.RemoveAt(index);
            NotifyChanged(ObservableCollectionChangeType.Remove, item, index);
        }

        public void Clear()
        {
            _items.Clear();
            NotifyChanged(ObservableCollectionChangeType.Clear, default, -1);
        }

        public void Reset(IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            _items.Clear();
            _items.AddRange(items);
            NotifyChanged(ObservableCollectionChangeType.Reset, default, -1);
        }

        public IDisposable Subscribe(Action<ObservableCollectionChangedEventArgs<T>> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            CollectionChanged += handler;
            return new BindingToken(() => CollectionChanged -= handler);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void NotifyChanged(ObservableCollectionChangeType changeType, T item, int index)
        {
            CollectionChanged?.Invoke(new ObservableCollectionChangedEventArgs<T>(changeType, item, index));
        }
    }
}
