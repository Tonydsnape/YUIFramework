namespace YUIFramework
{
    /// <summary>
    /// 导航行为选项。
    /// </summary>
    public sealed class UINavigateOptions
    {
        /// <summary>
        /// Push 新页面时是否隐藏当前页。
        /// </summary>
        public bool HideCurrentPage { get; set; } = true;

        /// <summary>
        /// Replace 时是否关闭当前页。
        /// </summary>
        public bool CloseCurrentPageOnReplace { get; set; } = true;

        /// <summary>
        /// 目标页已存在时是否将其提至栈顶。
        /// </summary>
        public bool BringExistingPageToTop { get; set; } = false;
    }
}
