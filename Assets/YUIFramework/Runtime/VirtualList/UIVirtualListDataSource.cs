namespace YUIFramework
{
    /// <summary>
    /// 虚拟列表数据源接口。
    /// </summary>
    public interface IUIVirtualListDataSource
    {
        int Count { get; }

        void BindItem(UIVirtualListItem item, int index);
    }
}
