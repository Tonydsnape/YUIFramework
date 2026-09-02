namespace YUIFramework
{
    /// <summary>
    /// 导航命令的类型，供导航守卫与拒绝异常携带上下文信息使用。
    /// </summary>
    public enum UINavigationCommandKind
    {
        Push,
        Pop,
        Back,
        Replace,
        BringToTop
    }
}
