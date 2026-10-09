using System;
using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// 可观察集合变更参数。
    /// </summary>
    public sealed class ObservableCollectionChangedEventArgs<T> : EventArgs
    {
        public ObservableCollectionChangedEventArgs(
            ObservableCollectionChangeType changeType, T item, int index,
            T oldItem = default, int oldIndex = -1, IReadOnlyList<T> resetItems = null)
        {
            ChangeType = changeType;
            Item = item;
            Index = index;
            OldItem = oldItem;
            OldIndex = oldIndex;
            ResetItems = resetItems;
        }

        public ObservableCollectionChangeType ChangeType { get; }
        public T Item { get; }
        public int Index { get; }
        public T OldItem { get; }
        public int OldIndex { get; }
        public IReadOnlyList<T> ResetItems { get; }
    }
}
