using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    public interface IUINavigator
    {
        int Count { get; }
        bool CanGoBack { get; }
        BasePageContext CurrentPage { get; }
        Type CurrentPageType { get; }

        /// <summary>
        /// 是否仍有导航命令在这条 FIFO 队列上运行或排队。
        /// </summary>
        bool IsBusy { get; }

        /// <summary>
        /// 可选的异步导航守卫扩展点。每条命令在真正执行（出队）时求值一次；
        /// 拒绝或抛异常都不会对导航栈产生任何副作用。
        /// </summary>
        UINavigationGuard Guard { get; set; }

        UniTask<T> PushAsync<T>(
            object args = null,
            UINavigateOptions options = null,
            CancellationToken cancellationToken = default)
            where T : BasePageContext;

        UniTask<BasePageContext> PopAsync(
            object args = null,
            CancellationToken cancellationToken = default);

        UniTask<T> ReplaceAsync<T>(
            object args = null,
            UINavigateOptions options = null,
            CancellationToken cancellationToken = default)
            where T : BasePageContext;

        /// <summary>
        /// 将已存在于栈中的页面提到栈顶（若不存在则等价于 Push）。与 Push/Pop/Replace/Back
        /// 共享同一条 FIFO 队列。
        /// </summary>
        UniTask<T> BringToTopAsync<T>(
            object args = null,
            UINavigateOptions options = null,
            CancellationToken cancellationToken = default)
            where T : BasePageContext;

        UniTask<bool> BackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// <see cref="BackAsync"/> 的显式命名别名，供业务代码（例如返回键处理）使用，
        /// 语义完全相同。
        /// </summary>
        UniTask<bool> NavigateBackAsync(CancellationToken cancellationToken = default);

        bool Contains<T>() where T : BasePageContext;
        void Clear();
    }
}
