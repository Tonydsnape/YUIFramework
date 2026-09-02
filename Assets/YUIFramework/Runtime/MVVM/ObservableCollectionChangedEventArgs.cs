using System;

namespace YUIFramework
{
    /// <summary>
    /// 可观察集合变更参数。
    /// </summary>
    public sealed class ObservableCollectionChangedEventArgs<T> : EventArgs
    {
        public ObservableCollectionChangedEventArgs(ObservableCollectionChangeType changeType, T item, int index)
        {
            ChangeType = changeType;
            Item = item;
            Index = index;
        }

        public ObservableCollectionChangeType ChangeType { get; }
        public T Item { get; }
        public int Index { get; }
    }
}
