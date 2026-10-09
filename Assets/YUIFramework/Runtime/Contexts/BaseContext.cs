using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        private readonly List<UIMessageToken> _displayMessageTokens = new List<UIMessageToken>();
        private readonly List<IDisposable> _displayBindings = new List<IDisposable>();
        private readonly List<IDisposable> _displayResources = new List<IDisposable>();
        private readonly UIContextStateMachine _stateMachine = new UIContextStateMachine();
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly CancellationToken _lifetimeToken;
        private IViewModel _viewModel;
        private UIViewModelOwnership _viewModelOwnership;
        private IUIMessageBus _messageBus;
        private UIContextOperation _currentOperation;
        private bool _lifetimeCancellationDisposed;
        private CancellationTokenSource _displayCancellation;
        private CancellationToken _displayToken;
        private object _displayArguments;
        private UIVisibilityState _visibilityState;
        private bool _navigationCovered;
        private bool _interactionCovered;

        protected BaseContext()
        {
            _lifetimeToken = _lifetimeCancellation.Token;
        }

        public string Id { get; internal set; }
        public UILayer Layer { get; internal set; }
        public UIContextState State => _stateMachine.State;
        public Exception LastFailure => _stateMachine.LastFailure;
        public CancellationToken LifetimeToken => _lifetimeToken;
        public CancellationToken DisplayToken => _displayCancellation == null
            ? CancellationToken.None
            : _displayToken;
        public object DisplayArguments => _displayArguments;
        public UIVisibilityState VisibilityState => _visibilityState;
        public bool IsVisible => (_visibilityState & UIVisibilityState.Visible) != 0;
        public bool IsInteractable => (_visibilityState & UIVisibilityState.Interactable) != 0;
        public bool IsCovered => (_visibilityState & UIVisibilityState.Covered) != 0;
        public bool IsSuspended => (_visibilityState & UIVisibilityState.Suspended) != 0;
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

        public event Action<UIVisibilityState, UIVisibilityState> VisibilityChanged;

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

        internal void SetRuntimeVisibility(bool visible)
        {
            SetVisibilityFlag(UIVisibilityState.Visible, visible);
            if (!visible)
            {
                SetVisibilityFlag(UIVisibilityState.Interactable, false);
            }
        }

        internal void SetInteractionVisibility(bool interactable, bool covered)
        {
            _interactionCovered = covered;
            SetVisibilityFlag(UIVisibilityState.Interactable, interactable);
            UpdateCoveredFlag();
        }

        internal void SetNavigationVisibility(bool covered, bool suspended)
        {
            _navigationCovered = covered;
            SetVisibilityFlag(UIVisibilityState.Suspended, suspended);
            UpdateCoveredFlag();
        }

        internal void ClearVisibility()
        {
            _navigationCovered = false;
            _interactionCovered = false;
            SetVisibilityState(UIVisibilityState.None);
        }

        public async UniTask WaitUntilResumedAsync(
            CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                DisplayToken,
                LifetimeToken);
            while (IsSuspended)
            {
                linked.Token.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, linked.Token);
            }
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

        internal void BeginDisplayScope(object args)
        {
            var previousArgs = _displayArguments;
            var cleanupError = EndDisplayScope();
            if (cleanupError != null)
            {
                throw cleanupError;
            }

            InvokeLifecycleCallback(() => HandleResetForReuse(previousArgs, args));
            _displayArguments = args;
            _displayCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
            _displayToken = _displayCancellation.Token;
        }

        internal Exception EndDisplayScope()
        {
            var errors = new List<Exception>();
            var cancellation = _displayCancellation;
            _displayCancellation = null;
            _displayToken = CancellationToken.None;
            _displayArguments = null;

            if (cancellation != null)
            {
                try
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        cancellation.Cancel();
                    }
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
                finally
                {
                    cancellation.Dispose();
                }
            }

            DisposeTracked(_displayMessageTokens, errors);
            DisposeTracked(_displayBindings, errors);
            DisposeTracked(_displayResources, errors);
            return errors.Count == 0
                ? null
                : new AggregateException(
                    $"Failed to clean the display scope for {GetType().Name}.",
                    errors);
        }

        private void UpdateCoveredFlag()
        {
            SetVisibilityFlag(
                UIVisibilityState.Covered,
                _navigationCovered || _interactionCovered);
        }

        private void SetVisibilityFlag(UIVisibilityState flag, bool enabled)
        {
            var next = enabled
                ? _visibilityState | flag
                : _visibilityState & ~flag;
            SetVisibilityState(next);
        }

        private void SetVisibilityState(UIVisibilityState next)
        {
            if (_visibilityState == next)
            {
                return;
            }

            var previous = _visibilityState;
            _visibilityState = next;
            VisibilityChanged?.Invoke(previous, next);
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
            var errors = new List<Exception>();
            var displayCleanupError = EndDisplayScope();
            if (displayCleanupError != null)
            {
                errors.Add(displayCleanupError);
            }

            try
            {
                UnsubscribeAllMessages();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                ClearBindings();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
            try
            {
                ClearViewModel();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                InvokeLifecycleCallback(HandleDestroy);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"Context destruction failed for {GetType().Name}.",
                    errors);
            }
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

        /// <summary>
        /// Runs before every display cycle. Override to clear transient arguments or view state;
        /// permanent initialization bindings remain owned by the context lifetime.
        /// </summary>
        protected virtual void HandleResetForReuse(object previousArgs, object nextArgs)
        {
        }

        protected UIMessageToken SubscribeMessage<T>(
            UIMessageTopic<T> topic,
            Action<T> handler,
            int priority = 0)
        {
            var token = GetMessageBus().Subscribe(
                topic,
                handler,
                priority: priority,
                owner: this);
            TrackMessageToken(token);
            return token;
        }

        protected void PublishMessage<T>(UIMessageTopic<T> topic, T payload)
        {
            GetMessageBus().Publish(topic, payload);
        }

        [Obsolete("Use SubscribeMessage(UIMessageTopic<UIMessageUnit>, ...). String message APIs will be removed after the Y2 migration window.")]
        protected UIMessageToken SubscribeMessage(string messageName, Action handler)
        {
            return SubscribeMessage(
                new UIMessageTopic<UIMessageUnit>(messageName),
                _ => handler());
        }

        [Obsolete("Use SubscribeMessage(UIMessageTopic<T>, ...). String message APIs will be removed after the Y2 migration window.")]
        protected UIMessageToken SubscribeMessage<T>(string messageName, Action<T> handler)
        {
            return SubscribeMessage(new UIMessageTopic<T>(messageName), handler);
        }

        [Obsolete("Use PublishMessage(UIMessageTopic<UIMessageUnit>, UIMessageUnit.Value). String message APIs will be removed after the Y2 migration window.")]
        protected void PublishMessage(string messageName)
        {
            PublishMessage(
                new UIMessageTopic<UIMessageUnit>(messageName),
                UIMessageUnit.Value);
        }

        [Obsolete("Use PublishMessage(UIMessageTopic<T>, payload). String message APIs will be removed after the Y2 migration window.")]
        protected void PublishMessage<T>(string messageName, T payload)
        {
            PublishMessage(new UIMessageTopic<T>(messageName), payload);
        }

        protected void TrackMessageToken(UIMessageToken token)
        {
            if (token == null || token.IsDisposed)
            {
                return;
            }

            _messageTokens.Add(token);
        }

        protected UIMessageToken SubscribeDisplayMessage<T>(
            UIMessageTopic<T> topic,
            Action<T> handler,
            int priority = 0)
        {
            var token = GetMessageBus().Subscribe(
                topic,
                handler,
                priority: priority,
                owner: this);
            TrackDisplayMessageToken(token);
            return token;
        }

        [Obsolete("Use SubscribeDisplayMessage(UIMessageTopic<UIMessageUnit>, ...). String message APIs will be removed after the Y2 migration window.")]
        protected UIMessageToken SubscribeDisplayMessage(string messageName, Action handler)
        {
            return SubscribeDisplayMessage(
                new UIMessageTopic<UIMessageUnit>(messageName),
                _ => handler());
        }

        [Obsolete("Use SubscribeDisplayMessage(UIMessageTopic<T>, ...). String message APIs will be removed after the Y2 migration window.")]
        protected UIMessageToken SubscribeDisplayMessage<T>(string messageName, Action<T> handler)
        {
            return SubscribeDisplayMessage(new UIMessageTopic<T>(messageName), handler);
        }

        protected void TrackDisplayMessageToken(UIMessageToken token)
        {
            if (token == null || token.IsDisposed)
            {
                return;
            }

            _displayMessageTokens.Add(token);
        }

        protected void UnsubscribeAllMessages()
        {
            var errors = new List<Exception>();
            for (var i = _messageTokens.Count - 1; i >= 0; i--)
            {
                try
                {
                    _messageTokens[i].Dispose();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            _messageTokens.Clear();
            try
            {
                _messageBus?.UnsubscribeOwner(this);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"Message cleanup failed for {GetType().Name}.",
                    errors);
            }
        }

        /// <summary>
        /// Replaces the current ViewModel after detaching lifetime bindings.
        /// Owned ViewModels are disposed on replacement/destruction; external ones are not.
        /// </summary>
        protected void SetViewModel(
            IViewModel viewModel,
            UIViewModelOwnership ownership = UIViewModelOwnership.Owned)
        {
            if (ReferenceEquals(_viewModel, viewModel))
            {
                _viewModelOwnership = ownership;
                return;
            }

            var errors = new List<Exception>();
            try
            {
                ClearBindings();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            DisposeTracked(_displayBindings, errors);
            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"ViewModel binding cleanup failed for {GetType().Name}.",
                    errors);
            }

            ClearViewModel();
            _viewModel = viewModel;
            _viewModelOwnership = ownership;
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

        protected void TrackDisplayBinding(IDisposable bindingToken)
        {
            if (bindingToken != null)
            {
                _displayBindings.Add(bindingToken);
            }
        }

        protected void TrackDisplayResource(IDisposable resource)
        {
            if (resource == null)
            {
                return;
            }

            _displayResources.Add(resource);
        }

        protected IDisposable AcquireDisplayInputLock(
            string reason,
            params UILayer[] allowedLayers)
        {
            var resource = Services?.InputLocks?.Acquire(this, reason, allowedLayers)
                ?? throw new InvalidOperationException(
                    $"{GetType().Name} is not bound to an input-lock service.");
            TrackDisplayResource(resource);
            return resource;
        }

        protected void RunDisplayTask(Func<CancellationToken, UniTask> taskFactory)
        {
            if (taskFactory == null)
            {
                throw new ArgumentNullException(nameof(taskFactory));
            }

            if (_displayCancellation == null)
            {
                throw new InvalidOperationException(
                    "Display tasks can only be started during an active display scope.");
            }

            RunDisplayTaskCore(taskFactory, _displayToken).Forget(HandleDisplayTaskException);
        }

        protected void ClearBindings()
        {
            List<Exception> errors = null;
            for (var i = _bindingTokens.Count - 1; i >= 0; i--)
            {
                try
                {
                    _bindingTokens[i]?.Dispose();
                }
                catch (Exception exception)
                {
                    errors ??= new List<Exception>();
                    errors.Add(exception);
                }
            }

            _bindingTokens.Clear();
            if (errors != null)
            {
                throw new AggregateException(
                    $"Binding cleanup failed for {GetType().Name}.",
                    errors);
            }
        }

        protected void ClearViewModel(bool dispose = true)
        {
            if (_viewModel == null)
            {
                return;
            }

            var vm = _viewModel;
            _viewModel = null;
            var ownership = _viewModelOwnership;
            _viewModelOwnership = UIViewModelOwnership.External;

            if (dispose && ownership == UIViewModelOwnership.Owned)
            {
                vm.Dispose();
            }
        }

        private IUIMessageBus GetMessageBus()
        {
            return _messageBus ?? throw new InvalidOperationException(
                $"{GetType().Name} has not been bound to an IUIService.");
        }

        private static async UniTask RunDisplayTaskCore(
            Func<CancellationToken, UniTask> taskFactory,
            CancellationToken cancellationToken)
        {
            await taskFactory(cancellationToken);
        }

        private static void HandleDisplayTaskException(Exception exception)
        {
            if (!(exception is OperationCanceledException))
            {
                Debug.LogException(exception);
            }
        }

        private static void DisposeTracked<T>(List<T> tracked, List<Exception> errors)
            where T : IDisposable
        {
            for (var index = tracked.Count - 1; index >= 0; index--)
            {
                try
                {
                    tracked[index]?.Dispose();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            tracked.Clear();
        }
    }
}
