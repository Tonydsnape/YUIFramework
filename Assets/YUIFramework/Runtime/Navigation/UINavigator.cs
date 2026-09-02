using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    /// <summary>
    /// Page 栈式导航器，仅管理 BasePageContext。
    /// </summary>
    /// <remarks>
    /// Phase 3：Push/Pop/Replace/Back/BringToTop 共享同一条 <see cref="UIOperationCoordinator"/>
    /// FIFO 队列（单一 key），因此导航命令严格按发起顺序串行执行，<see cref="IsBusy"/>
    /// 反映这条队列是否仍在运行/排队。每条命令在真正出队执行时才对 <see cref="Guard"/>
    /// 求值一次并对当前栈拍快照，被拒绝或抛异常都不会产生任何副作用。命令内部的
    /// Hide/Show 调用统一走 <see cref="UIManager.HideCoreAsync"/> / <see cref="UIManager.ShowCoreAsync"/>，
    /// 与 Open/Close 共享同一个按 Context 类型分道的 key 队列，不会绕过队列产生竞态。
    /// 详见 Documentation/Y2.0/Navigation.md。
    /// </remarks>
    public sealed class UINavigator : IUINavigator
    {
        private static readonly object LaneKey = UIOperationReentrancyScope.NavigationKey;

        private readonly List<UIPageStackEntry> _pageStack = new List<UIPageStackEntry>();
        private readonly UIManager _uiManager;
        private readonly UIOperationCoordinator _coordinator = new UIOperationCoordinator();

        public UINavigator(UIManager uiManager)
        {
            _uiManager = uiManager ?? throw new ArgumentNullException(nameof(uiManager));
        }

        public int Count => _pageStack.Count;
        public bool CanGoBack => _pageStack.Count > 1;
        public BasePageContext CurrentPage => CurrentEntry?.Page;
        public Type CurrentPageType => CurrentEntry?.PageType;
        public bool IsBusy => _coordinator.IsBusy(LaneKey);
        public UINavigationGuard Guard { get; set; }

        public UniTask<T> PushAsync<T>(
            object args = null,
            UINavigateOptions options = null,
            CancellationToken cancellationToken = default)
            where T : BasePageContext
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectiveOptions = options ?? new UINavigateOptions();
            return EnqueueNavigationAsync(
                "Push",
                ct => PushCoreAsync<T>(args, effectiveOptions, ct),
                cancellationToken);
        }

        public UniTask<BasePageContext> PopAsync(
            object args = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EnqueueNavigationAsync(
                "Pop",
                ct => PopCoreAsync(args, ct),
                cancellationToken);
        }

        public UniTask<T> ReplaceAsync<T>(
            object args = null,
            UINavigateOptions options = null,
            CancellationToken cancellationToken = default)
            where T : BasePageContext
        {
            cancellationToken.ThrowIfCancellationRequested();
            var effectiveOptions = options ?? new UINavigateOptions();
            return EnqueueNavigationAsync(
                "Replace",
                ct => ReplaceCoreAsync<T>(args, effectiveOptions, ct),
                cancellationToken);
        }

        public UniTask<T> BringToTopAsync<T>(
            object args = null,
            UINavigateOptions options = null,
            CancellationToken cancellationToken = default)
            where T : BasePageContext
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EnqueueNavigationAsync(
                "BringToTop",
                ct => BringToTopPublicCoreAsync<T>(args, ct),
                cancellationToken);
        }

        public UniTask<bool> BackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return EnqueueNavigationAsync(
                "Back",
                ct => BackCoreAsync(ct),
                cancellationToken);
        }

        public UniTask<bool> NavigateBackAsync(CancellationToken cancellationToken = default)
        {
            return BackAsync(cancellationToken);
        }

        public void Clear()
        {
            _pageStack.Clear();
        }

        public bool Contains<T>() where T : BasePageContext
        {
            return TryGetEntryIndex(typeof(T), out _);
        }

        /// <summary>
        /// Shutdown 用：停止接受新的导航命令。已排队/正在执行的命令继续正常排干。
        /// </summary>
        internal void Stop()
        {
            _coordinator.Stop();
        }

        /// <summary>
        /// Shutdown 用：等待这条导航 FIFO 队列彻底排空。
        /// </summary>
        internal UniTask DrainAsync()
        {
            return _coordinator.DrainAsync();
        }

        private UIPageStackEntry CurrentEntry => _pageStack.Count > 0 ? _pageStack[_pageStack.Count - 1] : null;

        private UniTask<T> EnqueueNavigationAsync<T>(
            string operationName,
            Func<CancellationToken, UniTask<T>> work,
            CancellationToken callerToken)
        {
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                callerToken,
                _uiManager.ServiceLifetimeToken);
            try
            {
                var queued = _coordinator.EnqueueAsync(
                    LaneKey,
                    operationName,
                    work,
                    linkedCancellation.Token);
                return AwaitAndDisposeAsync(queued, linkedCancellation);
            }
            catch
            {
                linkedCancellation.Dispose();
                throw;
            }
        }

        private static async UniTask<T> AwaitAndDisposeAsync<T>(
            UniTask<T> task,
            CancellationTokenSource cancellation)
        {
            using (cancellation)
            {
                return await task;
            }
        }

        private UIPageStackEntry CreateEntry(BasePageContext page, object args)
        {
            return new UIPageStackEntry(page.GetType(), page, args, page.IsFullScreen);
        }

        private bool TryGetEntryIndex(Type pageType, out int index)
        {
            for (var i = _pageStack.Count - 1; i >= 0; i--)
            {
                if (_pageStack[i].PageType == pageType)
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private void ReplaceTopEntry(BasePageContext page, object args)
        {
            var entry = CreateEntry(page, args);
            if (_pageStack.Count == 0)
            {
                _pageStack.Add(entry);
                return;
            }

            _pageStack[_pageStack.Count - 1] = entry;
        }

        private async UniTask<bool> EvaluateGuardAsync(UINavigationRequest request, CancellationToken cancellationToken)
        {
            var guard = Guard;
            if (guard == null)
            {
                return true;
            }

            UniTask<bool> evaluation;
            using (UIOperationReentrancyScope.Enter(LaneKey, false))
            {
                // The synchronous portion is guarded against navigation reentry. The
                // returned UniTask is awaited after leaving the thread-local scope so
                // unrelated PlayerLoop work is never misclassified as reentrant.
                evaluation = guard(request, cancellationToken);
            }

            return await evaluation;
        }

        private async UniTask<T> PushCoreAsync<T>(
            object args,
            UINavigateOptions options,
            CancellationToken cancellationToken)
            where T : BasePageContext
        {
            var targetType = typeof(T);
            var currentEntry = CurrentEntry;
            var request = new UINavigationRequest(UINavigationCommandKind.Push, currentEntry?.PageType, targetType, args);
            if (!await EvaluateGuardAsync(request, cancellationToken))
            {
                throw new UINavigationRejectedException(request);
            }

            // A duplicate push is never allowed to create a second stack entry for the
            // same context type, regardless of UINavigateOptions.BringExistingPageToTop.
            if (TryGetEntryIndex(targetType, out var existingIndex))
            {
                if (existingIndex == _pageStack.Count - 1)
                {
                    // Same-type top push is a refresh only: it must not close/duplicate
                    // the refreshed instance.
                    var refreshed = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);
                    ReplaceTopEntry(refreshed, args);
                    return refreshed;
                }

                return await BringToTopCoreAsync<T>(existingIndex, args, cancellationToken);
            }

            var hidCurrent = false;
            try
            {
                if (currentEntry != null)
                {
                    await _uiManager.HideForNavigationAsync(currentEntry.Page, cancellationToken);
                    hidCurrent = true;
                }

                var page = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);
                _pageStack.Add(CreateEntry(page, args));
                return page;
            }
            catch (Exception original)
            {
                Exception rollbackError = null;
                if (hidCurrent && currentEntry != null && !IsRetired(currentEntry.Page.State))
                {
                    rollbackError = await TryShowSilentlyAsync(currentEntry.Page, currentEntry.Args);
                }

                var reconcileError = TryReconcileStack();
                throw Combine(original, rollbackError, reconcileError);
            }
        }

        private async UniTask<BasePageContext> PopCoreAsync(object args, CancellationToken cancellationToken)
        {
            if (_pageStack.Count == 0)
            {
                return null;
            }

            if (_pageStack.Count <= 1)
            {
                return CurrentPage;
            }

            var currentEntry = CurrentEntry;
            var previousEntry = _pageStack[_pageStack.Count - 2];
            var request = new UINavigationRequest(UINavigationCommandKind.Pop, currentEntry.PageType, previousEntry.PageType, args);
            if (!await EvaluateGuardAsync(request, cancellationToken))
            {
                // Guard refusal has no side effects: the stack and every context are
                // left exactly as they were.
                return CurrentPage;
            }

            // Non-destructive-first: show the previous page before destructively
            // closing the current one, so a failed show never touches the current page.
            await _uiManager.ShowForNavigationAsync(previousEntry.Page, args, cancellationToken);

            try
            {
                await _uiManager.CloseForNavigationAsync(currentEntry.Page, cancellationToken);
                _pageStack.RemoveAt(_pageStack.Count - 1);
                return previousEntry.Page;
            }
            catch (Exception original)
            {
                Exception rollbackError;
                if (IsRetired(currentEntry.Page.State))
                {
                    // The current page was already destructively released/pooled by the
                    // time Close failed; never fabricate or reopen that identity. Drop
                    // its now-stale entry and keep the previous page as the converged top.
                    _pageStack.RemoveAt(_pageStack.Count - 1);
                    rollbackError = null;
                }
                else
                {
                    // Close failed before releasing the current page: undo the show and
                    // leave the stack exactly as it was before the command started.
                    rollbackError = await TryHideSilentlyAsync(previousEntry.Page);
                }

                var reconcileError = TryReconcileStack();
                throw Combine(original, rollbackError, reconcileError);
            }
        }

        private async UniTask<bool> BackCoreAsync(CancellationToken cancellationToken)
        {
            if (_pageStack.Count <= 1)
            {
                return false;
            }

            var countBefore = _pageStack.Count;
            await PopCoreAsync(null, cancellationToken);

            // PopCoreAsync returns the (possibly unchanged) current page both when there
            // is nothing to pop and when a guard refuses the pop; comparing the stack
            // count is what actually distinguishes "no side effects happened" so Back
            // can honestly report false instead of always claiming success.
            return _pageStack.Count < countBefore;
        }

        private async UniTask<T> ReplaceCoreAsync<T>(
            object args,
            UINavigateOptions options,
            CancellationToken cancellationToken)
            where T : BasePageContext
        {
            var targetType = typeof(T);
            var currentEntry = CurrentEntry;
            var request = new UINavigationRequest(UINavigationCommandKind.Replace, currentEntry?.PageType, targetType, args);
            if (!await EvaluateGuardAsync(request, cancellationToken))
            {
                throw new UINavigationRejectedException(request);
            }

            // Same-type top Replace is a refresh only: it must not close/duplicate the
            // refreshed instance, regardless of CloseCurrentPageOnReplace.
            if (currentEntry != null && currentEntry.PageType == targetType)
            {
                var refreshed = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);
                ReplaceTopEntry(refreshed, args);
                return refreshed;
            }

            // An existing-elsewhere instance is never duplicated either: replacing onto
            // it behaves like BringToTop and closes every page above it, regardless of
            // CloseCurrentPageOnReplace (documented in Navigation.md).
            if (TryGetEntryIndex(targetType, out var existingIndex) && existingIndex != _pageStack.Count - 1)
            {
                return await BringToTopCoreAsync<T>(existingIndex, args, cancellationToken);
            }

            T opened = null;
            try
            {
                // Non-destructive-first: open the new target before destructively
                // closing/hiding the current page.
                opened = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);

                if (currentEntry != null)
                {
                    if (options.CloseCurrentPageOnReplace)
                    {
                        await _uiManager.CloseForNavigationAsync(currentEntry.Page, cancellationToken);
                    }
                    else
                    {
                        await _uiManager.HideForNavigationAsync(currentEntry.Page, cancellationToken);
                    }

                    _pageStack.RemoveAt(_pageStack.Count - 1);
                }

                _pageStack.Add(CreateEntry(opened, args));
                return opened;
            }
            catch (Exception original)
            {
                Exception undoError = null;
                if (currentEntry != null && IsRetired(currentEntry.Page.State))
                {
                    // The old identity was already destructively released/pooled; never
                    // fabricate a replacement for it. Converge on the newly opened
                    // target as the only top entry instead.
                    if (opened != null)
                    {
                        _pageStack.RemoveAt(_pageStack.Count - 1);
                        _pageStack.Add(CreateEntry(opened, args));
                    }
                }
                else if (opened != null)
                {
                    // The old page is still intact: undo the new open and leave the
                    // stack exactly as it was before the command started.
                    undoError = await TryCloseSilentlyAsync(opened);
                }

                var reconcileError = TryReconcileStack();
                throw Combine(original, undoError, reconcileError);
            }
        }

        private async UniTask<T> BringToTopPublicCoreAsync<T>(object args, CancellationToken cancellationToken)
            where T : BasePageContext
        {
            var targetType = typeof(T);
            var currentEntry = CurrentEntry;
            var request = new UINavigationRequest(UINavigationCommandKind.BringToTop, currentEntry?.PageType, targetType, args);
            if (!await EvaluateGuardAsync(request, cancellationToken))
            {
                throw new UINavigationRejectedException(request);
            }

            if (!TryGetEntryIndex(targetType, out var existingIndex))
            {
                var hidCurrent = false;
                try
                {
                    if (currentEntry != null)
                    {
                        await _uiManager.HideForNavigationAsync(currentEntry.Page, cancellationToken);
                        hidCurrent = true;
                    }

                    var page = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);
                    _pageStack.Add(CreateEntry(page, args));
                    return page;
                }
                catch (Exception original)
                {
                    Exception rollbackError = null;
                    if (hidCurrent && currentEntry != null && !IsRetired(currentEntry.Page.State))
                    {
                        rollbackError = await TryShowSilentlyAsync(currentEntry.Page, currentEntry.Args);
                    }

                    var reconcileError = TryReconcileStack();
                    throw Combine(original, rollbackError, reconcileError);
                }
            }

            return await BringToTopCoreAsync<T>(existingIndex, args, cancellationToken);
        }

        private async UniTask<T> BringToTopCoreAsync<T>(
            int existingIndex,
            object args,
            CancellationToken cancellationToken)
            where T : BasePageContext
        {
            if (existingIndex < 0 || existingIndex >= _pageStack.Count)
            {
                var reopened = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);
                ReplaceTopEntry(reopened, args);
                return reopened;
            }

            if (existingIndex == _pageStack.Count - 1)
            {
                var refreshed = await _uiManager.OpenForNavigationAsync<T>(args, cancellationToken);
                ReplaceTopEntry(refreshed, args);
                return refreshed;
            }

            var existingEntry = _pageStack[existingIndex];
            var above = _pageStack.GetRange(existingIndex + 1, _pageStack.Count - existingIndex - 1);

            // Non-destructive-first: show the target before destructively closing the
            // pages that were stacked above it.
            await _uiManager.ShowForNavigationAsync(existingEntry.Page, args, cancellationToken);

            var errors = new List<Exception>();
            foreach (var entry in above)
            {
                try
                {
                    await _uiManager.CloseForNavigationAsync(entry.Page, cancellationToken);
                }
                catch (Exception exception)
                {
                    // Keep closing the remaining pages above the target even if one of
                    // them fails; this is a degraded-but-consistent outcome rather than
                    // a fully atomic one (documented in Navigation.md).
                    errors.Add(exception);
                }
            }

            // Converge unconditionally: rebuild the tail of the stack from what the
            // target actually is now. A page above the target that failed to close is
            // dropped from the tracked stack rather than fabricated back in.
            var existingPage = (T)existingEntry.Page;
            _pageStack.RemoveRange(existingIndex, _pageStack.Count - existingIndex);
            _pageStack.Add(CreateEntry(existingPage, args));

            var reconcileError = TryReconcileStack();
            if (reconcileError != null)
            {
                errors.Add(reconcileError);
            }

            if (errors.Count == 1)
            {
                throw errors[0];
            }

            if (errors.Count > 1)
            {
                throw new AggregateException(
                    "One or more pages above the BringToTop target failed to close.",
                    errors);
            }

            return existingPage;
        }

        /// <summary>
        /// 收敛安全网：从追踪的栈中剔除任何已经 Released/Pooled（即已不在活动注册表中）
        /// 的条目。绝不会为了"修复"栈而重新打开/伪造一个被销毁的身份；只做结构性
        /// 清理。任何时候都不应该让栈指向一个 Released/未注册的 Context。
        /// </summary>
        private Exception TryReconcileStack()
        {
            try
            {
                for (var i = _pageStack.Count - 1; i >= 0; i--)
                {
                    var page = _pageStack[i]?.Page;
                    if (page == null || IsRetired(page.State))
                    {
                        _pageStack.RemoveAt(i);
                    }
                }

                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private async UniTask<Exception> TryShowSilentlyAsync(BasePageContext page, object args)
        {
            try
            {
                await _uiManager.ShowForNavigationAsync(page, args, CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private async UniTask<Exception> TryHideSilentlyAsync(BasePageContext page)
        {
            try
            {
                await _uiManager.HideForNavigationAsync(page, CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private async UniTask<Exception> TryCloseSilentlyAsync(BasePageContext page)
        {
            try
            {
                await _uiManager.CloseForNavigationRollbackAsync(page);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static bool IsRetired(UIContextState state)
        {
            return state == UIContextState.Released || state == UIContextState.Pooled;
        }

        private static Exception Combine(Exception original, params Exception[] extra)
        {
            List<Exception> all = null;
            foreach (var exception in extra)
            {
                if (exception == null)
                {
                    continue;
                }

                all ??= new List<Exception> { original };
                all.Add(exception);
            }

            return all == null ? original : new AggregateException(all);
        }
    }
}
