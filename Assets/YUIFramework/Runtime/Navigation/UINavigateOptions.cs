namespace YUIFramework
{
    /// <summary>
    /// 导航行为选项。
    /// </summary>
    public sealed class UINavigateOptions
    {
        /// <summary>
        /// 历史兼容字段，Phase 3 起不再生效。当前导航策略始终只显示栈顶页面。
        /// </summary>
        public bool HideCurrentPage { get; set; } = true;

        /// <summary>
        /// Replace 时是否关闭当前页。
        /// </summary>
        public bool CloseCurrentPageOnReplace { get; set; } = true;

        /// <summary>
        /// 历史兼容字段，Phase 3 起不再生效：Push/Replace 目标页若已存在于栈中的其他
        /// 位置，永远会被提到栈顶，绝不会在栈中产生重复条目，无论这个字段是否设置。
        /// 见 Documentation/Y2.0/Navigation.md。
        /// </summary>
        public bool BringExistingPageToTop { get; set; } = true;
    }
}
