using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI Context 生命周期基类。
    /// </summary>
    public abstract class BaseContext : IUIContext
    {
        private bool _initialized;
        private bool _destroyed;
        private readonly List<UIMessageToken> _messageTokens = new List<UIMessageToken>();
        private readonly List<IDisposable> _bindingTokens = new List<IDisposable>();
        private readonly UIContextStateMachine _stateMachine = new UIContextStateMachine();
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly CancellationToken _lifetimeToken;
        private IViewModel _viewModel;
        private IUIMessageBus _messageBus;
        private UIContextOperation _currentOperation;
        private bool _lifetimeCancellationDisposed;

        protected BaseContext()
        {
            _lifetimeToken = _lifetimeCancellation.Token;
        }

        public string Id { get; internal set; }
        public UILayer Layer { get; internal set; }
        public UIContextState State => _stateMachine.State;
        public Exception LastFailure => _stateMachine.LastFailure;
        public CancellationToken LifetimeToken => _lifetimeToken;
        public UIOperationId CurrentOperationId => _currentOperation?.Id ?? default;
        public UIOperationKind CurrentOperationKind => _currentOperation?.Kind ?? UIOperationKind.None;
        public UICloseDisposition CloseDisposition { get; internal set; }
        public UIView View { get; internal set; }
        public GameObject ViewObject { get; internal set; }
        public UISortingLease SortingLease { get; internal set; }
        public bool IsModal { get; internal set; }
        public abstract UILayer DefaultLayer { get; }
        public virtual GameObject DefaultFocus => null;
        protected IUIService Services { get; private set; }

        public event Action<UIContextState, UIContextState> StateChanged
        {
            add => _stateMachine.StateChanged += value;
            remove => _stateMachine.StateChanged -= value;
        }

        internal void BindRuntime(
            IUIService services,
            string id,
            UILayer layer,
            UIView view,
            GameObject viewObject)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            _messageBus = services.MessageBus;
            Id = id;
            Layer = layer;
            View = view;
            ViewObject = viewObject;
        }

        internal bool IsInitialized => _initialized;

        internal UIContextOperation BeginOperation(
            UIOperationKind kind,
            CancellationToken cancellationToken,
            CancellationToken serviceCancellationToken,
            Action onDisposed)
        {
            if (_currentOperation != null)
            {
                throw new UIOperationInProgressException(
                    GetType(),
                    _currentOperation.Id,
                    _currentOperation.Kind);
            }

            var operation = new UIContextOperation(
                this,
                kind,
                cancellationToken,
                _lifetimeToken,
                serviceCancellationToken,
                onDisposed);
            _currentOperation = operation;
            return operation;
        }

        internal void CompleteOperation(UIContextOperation operation)
        {
            if (ReferenceEquals(_currentOperation, operation))
            {
                _currentOperation = null;
            }
        }

        internal void TransitionTo(UIContextState state)
        {
            _stateMachine.TransitionTo(state);
        }

        internal void RecordFailure(Exception exception, bool enterFaultedState)
        {
            _stateMachine.RecordFailure(exception, enterFaultedState);
        }

        internal void CancelLifetime()
        {
            if (_lifetimeCancellationDisposed)
            {
                return;
            }

            try
            {
                if (!_lifetimeCancellation.IsCancellationRequested)
                {
                    _lifetimeCancellation.Cancel();
                }
            }
            finally
            {
                _lifetimeCancellation.Dispose();
                _lifetimeCancellationDisposed = true;
            }
        }

        public void OnInit()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            InvokeLifecycleCallback(HandleInit);
        }

        public void OnShow(object args)
        {
            InvokeLifecycleCallback(() => HandleShow(args));
        }

        public void OnHide()
        {
            InvokeLifecycleCallback(HandleHide);
        }

        public void OnClose()
        {
            InvokeLifecycleCallback(HandleClose);
        }

        public void OnDestroy()
        {
            if (_destroyed)
            {
                return;
            }

            _destroyed = true;
            UnsubscribeAllMessages();
            ClearBindings();
            ClearViewModel();
            InvokeLifecycleCallback(HandleDestroy);
        }

        private void InvokeLifecycleCallback(Action callback)
        {
            var contextType = GetType();
            var includeNavigation =
                Services is UIManager manager &&
                manager.IsNavigationCallbackType(contextType);
            using (UIOperationReentrancyScope.Enter(contextType, includeNavigation))
            {
                callback();
            }
        }

        protected virtual void HandleInit()
        {
        }

        protected virtual void HandleShow(object args)
        {
        }

        protected virtual void HandleHide()
        {
        }

        protected virtual void HandleClose()
        {
        }

        protected virtual void HandleDestroy()
        {
        }

        protected UIMessageToken SubscribeMessage(string messageName, Action handler)
        {
            var token = GetMessageBus().Subscribe(messageName, handler, this);
            TrackMessageToken(token);
            return token;
        }

        protected UIMessageToken SubscribeMessage<T>(string messageName, Action<T> handler)
        {
            var token = GetMessageBus().Subscribe(messageName, handler, this);
            TrackMessageToken(token);
            return token;
        }

        protected void PublishMessage(string messageName)
        {
            GetMessageBus().Publish(messageName);
        }

        protected void PublishMessage<T>(string messageName, T payload)
        {
            GetMessageBus().Publish(messageName, payload);
        }

        protected void TrackMessageToken(UIMessageToken token)
        {
            if (token == null || token.IsDisposed)
            {
                return;
            }

            _messageTokens.Add(token);
        }

        protected void UnsubscribeAllMessages()
        {
            for (var i = _messageTokens.Count - 1; i >= 0; i--)
            {
                _messageTokens[i].Dispose();
            }

            _messageTokens.Clear();
            _messageBus?.UnsubscribeOwner(this);
        }

        /// <summary>
        /// 设置当前 Context 的 ViewModel。替换时会释放旧实例。
        /// </summary>
        protected void SetViewModel(IViewModel viewModel)
        {
            if (ReferenceEquals(_viewModel, viewModel))
            {
                return;
            }

            ClearViewModel();
            _viewModel = viewModel;
        }

        protected T GetViewModel<T>() where T : class, IViewModel
        {
            return _viewModel as T;
        }

        /// <summary>
        /// 追踪绑定 token，OnDestroy 自动释放。
        /// </summary>
        protected void TrackBinding(IDisposable bindingToken)
        {
            if (bindingToken == null)
            {
                return;
            }

            if (bindingToken is BindingToken token && token.IsDisposed)
            {
                return;
            }

            _bindingTokens.Add(bindingToken);
        }

        protected void ClearBindings()
        {
            for (var i = _bindingTokens.Count - 1; i >= 0; i--)
            {
                try
                {
                    _bindingTokens[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _bindingTokens.Clear();
        }

        protected void ClearViewModel(bool dispose = true)
        {
            if (_viewModel == null)
            {
                return;
            }

            var vm = _viewModel;
            _viewModel = null;

            if (dispose)
            {
                vm.Dispose();
            }
        }

        private IUIMessageBus GetMessageBus()
        {
            return _messageBus ?? throw new InvalidOperationException(
                $"{GetType().Name} has not been bound to an IUIService.");
        }
    }
}
