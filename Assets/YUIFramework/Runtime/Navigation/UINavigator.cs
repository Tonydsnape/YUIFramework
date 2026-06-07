using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YUIFramework
{
    /// <summary>
    /// Page 栈式导航器，仅管理 BasePageContext。
    /// </summary>
    public sealed class UINavigator
    {
        private readonly List<UIPageStackEntry> _pageStack = new List<UIPageStackEntry>();
        private readonly UIManager _uiManager;

        public UINavigator(UIManager uiManager)
        {
            _uiManager = uiManager ?? throw new ArgumentNullException(nameof(uiManager));
        }

        public int Count => _pageStack.Count;
        public bool CanGoBack => _pageStack.Count > 1;
        public BasePageContext CurrentPage => CurrentEntry?.Page;
        public Type CurrentPageType => CurrentEntry?.PageType;

        public async Task<T> PushAsync<T>(object args = null, UINavigateOptions options = null) where T : BasePageContext
        {
            options ??= new UINavigateOptions();
            var targetType = typeof(T);

            if (TryGetEntryIndex(targetType, out var existingIndex) && options.BringExistingPageToTop)
            {
                return await BringToTopAsync<T>(existingIndex, args);
            }

            if (CurrentPageType == targetType)
            {
                var refreshed = await _uiManager.OpenAsync<T>(args);
                ReplaceTopEntry(refreshed, args);
                return refreshed;
            }

            if (options.HideCurrentPage && CurrentPage != null)
            {
                _uiManager.HideWithoutClose(CurrentPage);
            }

            var page = await _uiManager.OpenAsync<T>(args);
            _pageStack.Add(CreateEntry(page, args));
            return page;
        }

        public async Task<BasePageContext> PopAsync(object args = null)
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
            await _uiManager.CloseAsync(currentEntry.Page);
            _pageStack.RemoveAt(_pageStack.Count - 1);

            var previous = CurrentPage;
            _uiManager.ShowWithoutOpen(previous, args);
            return previous;
        }

        public async Task<T> ReplaceAsync<T>(object args = null, UINavigateOptions options = null) where T : BasePageContext
        {
            options ??= new UINavigateOptions();

            if (CurrentPage != null)
            {
                if (options.CloseCurrentPageOnReplace)
                {
                    await _uiManager.CloseAsync(CurrentPage);
                }
                else
                {
                    _uiManager.HideWithoutClose(CurrentPage);
                }

                _pageStack.RemoveAt(_pageStack.Count - 1);
            }

            if (TryGetEntryIndex(typeof(T), out var existingIndex) && options.BringExistingPageToTop)
            {
                return await BringToTopAsync<T>(existingIndex, args);
            }

            var page = await _uiManager.OpenAsync<T>(args);
            _pageStack.Add(CreateEntry(page, args));
            return page;
        }

        public async Task<bool> BackAsync()
        {
            if (!CanGoBack)
            {
                return false;
            }

            await PopAsync();
            return true;
        }

        public void Clear()
        {
            _pageStack.Clear();
        }

        public bool Contains<T>() where T : BasePageContext
        {
            return TryGetEntryIndex(typeof(T), out _);
        }

        private UIPageStackEntry CurrentEntry => _pageStack.Count > 0 ? _pageStack[_pageStack.Count - 1] : null;

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

        private async Task<T> BringToTopAsync<T>(int existingIndex, object args) where T : BasePageContext
        {
            if (existingIndex < 0 || existingIndex >= _pageStack.Count)
            {
                var reopened = await _uiManager.OpenAsync<T>(args);
                ReplaceTopEntry(reopened, args);
                return reopened;
            }

            for (var i = _pageStack.Count - 1; i > existingIndex; i--)
            {
                await _uiManager.CloseAsync(_pageStack[i].Page);
                _pageStack.RemoveAt(i);
            }

            var existingPage = (T)_pageStack[existingIndex].Page;
            _uiManager.ShowWithoutOpen(existingPage, args);

            _pageStack.RemoveAt(existingIndex);
            _pageStack.Add(CreateEntry(existingPage, args));
            return existingPage;
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
    }
}
