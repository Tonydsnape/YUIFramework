using System;

namespace YUIFramework
{
    /// <summary>
    /// 页面栈条目，记录页面实例与调试信息。
    /// </summary>
    public sealed class UIPageStackEntry
    {
        public Type PageType { get; }
        public BasePageContext Page { get; }
        public object Args { get; }
        public bool FullScreen { get; }
        public DateTime CreatedAt { get; }
        public string DebugName => $"{PageType.Name}({Page.Id})";

        public UIPageStackEntry(Type pageType, BasePageContext page, object args, bool fullScreen)
        {
            PageType = pageType ?? throw new ArgumentNullException(nameof(pageType));
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Args = args;
            FullScreen = fullScreen;
            CreatedAt = DateTime.UtcNow;
        }
    }
}
