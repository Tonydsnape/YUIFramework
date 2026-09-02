using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public interface IUIBackInputSource
    {
        bool BackPressedThisFrame { get; }
    }

    public sealed class LegacyUIBackInputSource : IUIBackInputSource
    {
        public bool BackPressedThisFrame => Input.GetKeyDown(KeyCode.Escape);
    }

    /// <summary>
    /// Platform-neutral back controller. Escape and Android back share KeyCode.Escape in
    /// Unity's legacy input API; pointer/touch dispatch remains owned by EventSystem.
    /// </summary>
    public sealed class UIInputRouter : IDisposable
    {
        private IUINavigator _navigator;
        private UIInputLockService _inputLocks;
        private IUIBackInputSource _source;
        private bool _backInFlight;
        private bool _disposed;

        public bool IsBackInFlight => _backInFlight;
        internal bool BackPressedThisFrame =>
            !_disposed && _source != null && _source.BackPressedThisFrame;

        public void Bind(
            IUINavigator navigator,
            UIInputLockService inputLocks,
            IUIBackInputSource source = null)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UIInputRouter));
            }

            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            _inputLocks = inputLocks ?? throw new ArgumentNullException(nameof(inputLocks));
            _source = source ?? new LegacyUIBackInputSource();
        }

        public bool Tick()
        {
            if (_disposed || _navigator == null || !BackPressedThisFrame)
            {
                return false;
            }

            return RequestBack();
        }

        public bool RequestBack()
        {
            if (_disposed || _navigator == null || _inputLocks == null)
            {
                return false;
            }

            if (_backInFlight || _navigator.IsBusy || _inputLocks.IsLocked)
            {
                return false;
            }

            _backInFlight = true;
            NavigateBackAsync().Forget(Debug.LogException);
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            _navigator = null;
            _inputLocks = null;
            _source = null;
        }

        private async UniTask NavigateBackAsync()
        {
            try
            {
                await _navigator.NavigateBackAsync();
            }
            finally
            {
                _backInFlight = false;
            }
        }
    }

    [DefaultExecutionOrder(-1000)]
    internal sealed class UIInputDriver : MonoBehaviour
    {
        private UIInputRouter _router;
        private UnityEngine.EventSystems.EventSystem _eventSystem;
        private bool _restoreNavigation;
        private bool _navigationWasEnabled;

        internal void Bind(
            UIInputRouter router,
            UnityEngine.EventSystems.EventSystem eventSystem)
        {
            _router = router;
            _eventSystem = eventSystem;
        }

        private void Update()
        {
            var suppressEventSystemCancel = _router != null && _router.BackPressedThisFrame;
            _router?.Tick();
            if (!suppressEventSystemCancel || _eventSystem == null)
            {
                return;
            }

            _navigationWasEnabled = _eventSystem.sendNavigationEvents;
            _eventSystem.sendNavigationEvents = false;
            _restoreNavigation = true;
        }

        private void LateUpdate()
        {
            if (!_restoreNavigation || _eventSystem == null)
            {
                return;
            }

            _eventSystem.sendNavigationEvents = _navigationWasEnabled;
            _restoreNavigation = false;
        }
    }
}
