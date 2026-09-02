using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 核心调度器，负责注册、打开、关闭与生命周期驱动。
    /// </summary>
    public class UIManager : IUIService
    {
        private static readonly Lazy<UIManager> LazyInstance = new Lazy<UIManager>(() => new UIManager());

        private readonly Dictionary<Type, UIConfig> _configRegistry = new Dictionary<Type, UIConfig>();
        private readonly Dictionary<Type, BaseContext> _activeContexts = new Dictionary<Type, BaseContext>();
        private readonly Dictionary<BaseContext, string> _contextPrefabKeys = new Dictionary<BaseContext, string>();
        private readonly Dictionary<Type, int> _navigationCallbackTypes = new Dictionary<Type, int>();
        private readonly object _operationGate = new object();
        private readonly object _shutdownGate = new object();

        private IResourceLoader _resourceLoader;
        private IUIObjectPool _objectPool = new UIObjectPool();
        private UILayerManager _layerManager;
        private UIRootRuntime _rootRuntime;
        private UITransitionRunner _transitionRunner;
        private CancellationTokenSource _serviceLifetimeCancellation;
        private UIOperationCoordinator _coordinator;
        private int _inFlightOperationCount;
        private bool _acceptingOperations;
        private bool _shuttingDown;
        private bool _initialized;
        private Task _shutdownTask;

        public static UIManager Instance => LazyInstance.Value;

        public UINavigator Navigator { get; private set; }
        public UIMessageCenter MessageCenter { get; private set; }
        public UITransitionRunner TransitionRunner => _transitionRunner;
        public UIRootRuntime RootRuntime => _rootRuntime;
        public UILayerManager LayerManager => _layerManager;
        public UIInputLockService InputLocks => _rootRuntime?.InputLocks;
        public UIInputRouter Input => _rootRuntime?.Input;
        public UIFocusService Focus => _rootRuntime?.Focus;
        public UIModalService Modals => _rootRuntime?.Modals;
        public int LastShutdownInputLockLeakCount { get; private set; }
        public bool IsInitialized => _initialized;
        internal CancellationToken ServiceLifetimeToken =>
            _serviceLifetimeCancellation?.Token ?? CancellationToken.None;
        internal bool IsNavigationCallbackType(Type contextType) =>
            contextType != null && _navigationCallbackTypes.ContainsKey(contextType);

        IUINavigator IUIService.Navigator => Navigator;
        IUIMessageBus IUIService.MessageBus => MessageCenter;

        public UIManager()
        {
        }

        [Obsolete("Use Initialize on an injected IUIService. UIManager.Init will be removed after the Y2 migration window.")]
        public void Init(IResourceLoader loader, IUIObjectPool pool = null)
        {
            Initialize(loader, pool);
        }

        public void Initialize(IResourceLoader loader, IUIObjectPool pool = null)
        {
            EnsureCanInitialize();
            var runtime = UIRootRuntime.CreateCompatible();
            try
            {
                Initialize(loader, runtime, pool);
            }
            catch
            {
                runtime.Dispose();
                throw;
            }
        }

        public void Initialize(
            IResourceLoader loader,
            UIRootRuntime rootRuntime,
            IUIObjectPool pool = null)
        {
            EnsureCanInitialize();

            _resourceLoader = loader ?? throw new ArgumentNullException(nameof(loader));
            _rootRuntime = rootRuntime ?? throw new ArgumentNullException(nameof(rootRuntime));
            if (_rootRuntime.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(rootRuntime));
            }

            MessageCenter = new UIMessageCenter();
            _objectPool = pool ?? new UIObjectPool();
            _serviceLifetimeCancellation = new CancellationTokenSource();
            _acceptingOperations = true;
            _shuttingDown = false;
            _layerManager = _rootRuntime.LayerManager;
            _coordinator = new UIOperationCoordinator();
            Navigator = new UINavigator(this);
            _rootRuntime.BindNavigator(Navigator);
            _transitionRunner = new UITransitionRunner();
            LastShutdownInputLockLeakCount = 0;
            _initialized = true;
        }

        public UniTask InitializeAsync(
            IResourceLoader loader,
            IUIObjectPool pool = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Initialize(loader, pool);
            return UniTask.CompletedTask;
        }

        public UniTask InitializeAsync(
            IResourceLoader loader,
            UIRootRuntime rootRuntime,
            IUIObjectPool pool = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Initialize(loader, rootRuntime, pool);
            return UniTask.CompletedTask;
        }

        public void Register<T>(UIConfig config) where T : BaseContext
        {
            EnsureInitialized();

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.Id))
            {
                throw new ArgumentException("UIConfig.Id 不能为空。", nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.PrefabKey))
            {
                throw new ArgumentException("UIConfig.PrefabKey 不能为空。", nameof(config));
            }

            _configRegistry[typeof(T)] = config;
        }

        public bool IsRegistered<T>() where T : BaseContext
        {
            return _configRegistry.ContainsKey(typeof(T));
        }

        public bool TryGetConfig(Type contextType, out UIConfig config)
        {
            if (contextType == null)
            {
                config = null;
                return false;
            }

            return _configRegistry.TryGetValue(contextType, out config);
        }

        public UniTask<T> OpenAsync<T>(
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            cancellationToken.ThrowIfCancellationRequested();

            return OpenCoordinatedAsync<T>(args, cancellationToken);
        }

        private UniTask<T> OpenCoordinatedAsync<T>(
            object args,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            var contextType = typeof(T);
            if (!_configRegistry.TryGetValue(contextType, out var config))
            {
                throw new KeyNotFoundException($"未注册 UI Context: {contextType.Name}");
            }

            // 同一 Type 的 Open/Close/Hide/Show 都在这条 key 队列上严格 FIFO 执行；
            // 不同 Type 之间互不阻塞。只有"执行时发现既无活动实例也无可复用池化实例"
            // 的首次创建型 Open，才允许与随后到达、参数相等且队列为空的并发 Open
            // 共享同一次执行结果（见 UIOperationCoordinator.EnqueueOpenAsync）。
            return _coordinator.EnqueueOpenAsync<T>(
                contextType,
                args,
                !_activeContexts.ContainsKey(contextType) &&
                (_objectPool == null || _objectPool.Count(contextType) == 0),
                (markFirstCreation, ct) => OpenRoutedAsync<T>(contextType, config, args, markFirstCreation, ct),
                cancellationToken,
                _serviceLifetimeCancellation.Token);
        }

        private async UniTask<T> OpenRoutedAsync<T>(
            Type contextType,
            UIConfig config,
            object args,
            Action markFirstCreation,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            if (_activeContexts.TryGetValue(contextType, out var cachedContext))
            {
                return await OpenExistingAsync<T>(
                    cachedContext,
                    config,
                    args,
                    cancellationToken);
            }

            if (_objectPool.TryGet(contextType, out var pooled))
            {
                return await OpenPooledAsync<T>(
                    pooled,
                    config,
                    args,
                    cancellationToken);
            }

            // Only a genuinely brand-new instantiation is eligible for single-flight
            // merging with a later equivalent concurrent Open request.
            markFirstCreation();
            return await OpenNewAsync<T>(config, args, cancellationToken);
        }

        public async UniTask<UIHandle<T>> OpenHandleAsync<T>(
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            var context = await OpenAsync<T>(args, cancellationToken);
            var config = _configRegistry[typeof(T)];
            return new UIHandle<T>(this, config.Key, context);
        }

        internal async UniTask<T> OpenForNavigationAsync<T>(
            object args,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            using (EnterNavigationCallbackType(typeof(T)))
            {
                return await OpenCoordinatedAsync<T>(args, cancellationToken);
            }
        }

        public UniTask CloseAsync<T>(CancellationToken cancellationToken = default) where T : BaseContext
        {
            EnsureInitialized();
            EnsureAcceptingOperations();

            var contextType = typeof(T);
            // The active context for T is resolved when the command executes, not when
            // it is enqueued, so a rapid Open->Close pair closes whatever Open produced.
            return _coordinator.EnqueueAsync(
                contextType,
                "Close",
                ct => CloseRoutedAsync(contextType, null, ct),
                cancellationToken);
        }

        public UniTask CloseAsync(
            BaseContext ctx,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            EnsureAcceptingOperations();

            if (ctx == null)
            {
                return UniTask.CompletedTask;
            }

            var contextType = ctx.GetType();
            return _coordinator.EnqueueAsync(
                contextType,
                "Close",
                ct => CloseRoutedAsync(contextType, ctx, ct),
                cancellationToken);
        }

        private UniTask CloseRoutedAsync(
            Type contextType,
            BaseContext expectedContext,
            CancellationToken cancellationToken,
            bool allowDuringShutdown = false)
        {
            if (expectedContext != null)
            {
                // CloseInternalAsync re-validates identity against the active registry;
                // a stale handle/reference is a no-op even if a newer instance of the
                // same type is now active.
                return CloseInternalAsync(expectedContext, cancellationToken, allowDuringShutdown);
            }

            return _activeContexts.TryGetValue(contextType, out var activeContext)
                ? CloseInternalAsync(activeContext, cancellationToken, allowDuringShutdown)
                : UniTask.CompletedTask;
        }

        internal async UniTask CloseForNavigationAsync(
            BaseContext context,
            CancellationToken cancellationToken)
        {
            await CloseForNavigationAsync(context, cancellationToken, false);
        }

        internal async UniTask CloseForNavigationRollbackAsync(BaseContext context)
        {
            await CloseForNavigationAsync(context, CancellationToken.None, true);
        }

        private async UniTask CloseForNavigationAsync(
            BaseContext context,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
        {
            if (context == null)
            {
                return;
            }

            using (EnterNavigationCallbackType(context.GetType()))
            {
                await _coordinator.EnqueueAsync(
                    context.GetType(),
                    "Close",
                    ct => CloseRoutedAsync(context.GetType(), context, ct, allowDuringShutdown),
                    cancellationToken);
            }
        }

        private async UniTask CloseInternalAsync(
            BaseContext ctx,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
        {
            EnsureInitialized();
            cancellationToken.ThrowIfCancellationRequested();

            if (ctx == null)
            {
                return;
            }

            var contextType = ctx.GetType();
            if (!_activeContexts.TryGetValue(contextType, out var activeContext) ||
                !ReferenceEquals(activeContext, ctx))
            {
                return;
            }

            _configRegistry.TryGetValue(contextType, out var config);
            var prefabKey = ResolvePrefabKey(ctx, config);
            using var operation = BeginContextOperation(
                ctx,
                UIOperationKind.Close,
                cancellationToken,
                allowDuringShutdown);
            var policy = config == null ? null : UIPoolPolicy.FromConfig(config);
            var intendsToPool =
                policy != null &&
                policy.CacheOnClose &&
                policy.MaxPoolSize > 0 &&
                _objectPool != null;
            ctx.CloseDisposition = intendsToPool
                ? UICloseDisposition.Pool
                : UICloseDisposition.Release;

            try
            {
                if (ctx.State == UIContextState.Opened)
                {
                    ctx.TransitionTo(UIContextState.Hiding);
                    try
                    {
                        await PlayHideTransitionAsync(ctx, config, operation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        ctx.TransitionTo(UIContextState.Opened);
                        throw;
                    }

                    ctx.OnHide();
                    if (ctx.ViewObject != null)
                    {
                        ctx.ViewObject.SetActive(false);
                    }

                    ctx.TransitionTo(UIContextState.Hidden);
                    HideContextRuntime(ctx);
                }
                else if (ctx.State != UIContextState.Hidden)
                {
                    throw new InvalidOperationException(
                        $"Cannot close {contextType.Name} from state {ctx.State}.");
                }

                ctx.TransitionTo(UIContextState.Closing);
                ctx.OnClose();
                _activeContexts.Remove(contextType);
                ReleaseContextRuntime(ctx);

                if (intendsToPool)
                {
                    var pooledObject = new UIPooledObject(
                        contextType,
                        prefabKey,
                        ctx,
                        ctx.ViewObject);
                    if (_objectPool.TryRelease(
                            contextType,
                            pooledObject,
                            policy,
                            out var overflow))
                    {
                        ctx.TransitionTo(UIContextState.Pooled);
                        return;
                    }

                    if (overflow != null)
                    {
                        ctx.CloseDisposition = UICloseDisposition.Release;
                        DestroyContextInternal(overflow.Context, overflow.PrefabKey);
                        return;
                    }
                }

                DestroyContextInternal(ctx, prefabKey);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = HandleTerminalLifecycleFailure(
                    ctx,
                    contextType,
                    prefabKey,
                    operation,
                    "close",
                    exception);
                throw failure;
            }
        }

        public UniTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource<object> completion;
            Task sharedTask;
            lock (_shutdownGate)
            {
                if (_shutdownTask != null)
                {
                    return _shutdownTask.AsUniTask();
                }

                if (!_initialized)
                {
                    return UniTask.CompletedTask;
                }

                completion = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                sharedTask = completion.Task;
                _shutdownTask = sharedTask;
            }

            CompleteShutdownAsync(completion, sharedTask).Forget(Debug.LogException);
            return sharedTask.AsUniTask();
        }

        private async UniTask CompleteShutdownAsync(
            TaskCompletionSource<object> completion,
            Task sharedTask)
        {
            try
            {
                await ShutdownCoreAsync();
                completion.TrySetResult(null);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                lock (_shutdownGate)
                {
                    if (ReferenceEquals(_shutdownTask, sharedTask))
                    {
                        _shutdownTask = null;
                    }
                }
            }
        }

        private async UniTask ShutdownCoreAsync()
        {
            // Reject all new public work first. The navigator stops accepting commands,
            // then its already-accepted transaction may still use the UI lanes to finish
            // cancellation rollback before those lanes are stopped in turn.
            lock (_operationGate)
            {
                _acceptingOperations = false;
            }

            Navigator?.Stop();
            _serviceLifetimeCancellation.Cancel();

            if (Navigator != null)
            {
                await Navigator.DrainAsync();
            }

            _coordinator.Stop();
            await _coordinator.DrainAsync();

            lock (_operationGate)
            {
                _shuttingDown = true;
            }

            await WaitForInFlightOperationsAsync();

            var errors = new List<Exception>();
            var contexts = new List<BaseContext>(_activeContexts.Values);
            for (var i = contexts.Count - 1; i >= 0; i--)
            {
                try
                {
                    await CloseInternalAsync(
                        contexts[i],
                        CancellationToken.None,
                        true);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            _objectPool?.Clear(pooled =>
            {
                try
                {
                    DestroyPooledObject(pooled, true);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            });
            Navigator?.Clear();
            MessageCenter?.Clear();
            _configRegistry.Clear();
            _activeContexts.Clear();
            _contextPrefabKeys.Clear();
            _navigationCallbackTypes.Clear();
            LastShutdownInputLockLeakCount = _rootRuntime?.InputLocks.ActiveLockCount ?? 0;
            try
            {
                _rootRuntime?.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            _resourceLoader = null;
            _objectPool = null;
            _layerManager = null;
            _rootRuntime = null;
            _transitionRunner = null;
            _serviceLifetimeCancellation.Dispose();
            _serviceLifetimeCancellation = null;
            _coordinator = null;
            Navigator = null;
            MessageCenter = null;
            _initialized = false;
            lock (_operationGate)
            {
                _acceptingOperations = false;
                _shuttingDown = false;
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    "One or more UI contexts failed while shutting down.",
                    errors);
            }
        }

        public T Get<T>() where T : BaseContext
        {
            return _activeContexts.TryGetValue(typeof(T), out var context) ? (T)context : null;
        }

        public bool IsOpen<T>() where T : BaseContext
        {
            if (!_activeContexts.TryGetValue(typeof(T), out var context) || context.ViewObject == null)
            {
                return false;
            }

            return context.ViewObject.activeInHierarchy;
        }

        public void ClearPool<T>() where T : BaseContext
        {
            EnsureInitialized();
            ClearPools(typeof(T));
        }

        public void ClearAllPools()
        {
            EnsureInitialized();
            ClearPools(null);
        }

        /// <summary>
        /// 供导航器等内部调用方使用：在不触发关闭生命周期的前提下隐藏一个已打开的
        /// Context。和 Open/Close 一样经由该 Context 类型对应的 key 队列 FIFO 执行，
        /// 不会绕过队列产生竞态。
        /// </summary>
        internal UniTask HideCoreAsync(BaseContext ctx, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (ctx == null)
            {
                return UniTask.CompletedTask;
            }

            var contextType = ctx.GetType();
            return _coordinator.EnqueueAsync(
                contextType,
                "Hide",
                _ => HideCoreExecuteAsync(ctx),
                cancellationToken);
        }

        internal async UniTask HideForNavigationAsync(
            BaseContext context,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                return;
            }

            using (EnterNavigationCallbackType(context.GetType()))
            {
                await HideCoreAsync(context, cancellationToken);
            }
        }

        private UniTask HideCoreExecuteAsync(BaseContext ctx)
        {
            if (ctx.State == UIContextState.Hidden)
            {
                return UniTask.CompletedTask;
            }

            using var operation = BeginContextOperation(
                ctx,
                UIOperationKind.Hide,
                CancellationToken.None,
                false);
            try
            {
                ctx.TransitionTo(UIContextState.Hiding);
                ctx.OnHide();
                if (ctx.ViewObject != null)
                {
                    ctx.ViewObject.SetActive(false);
                }

                ctx.TransitionTo(UIContextState.Hidden);
                HideContextRuntime(ctx);
            }
            catch (Exception exception)
            {
                ctx.RecordFailure(exception, false);
                if (ctx.ViewObject != null)
                {
                    ctx.ViewObject.SetActive(true);
                }

                if (ctx.State == UIContextState.Hiding)
                {
                    ctx.TransitionTo(UIContextState.Opened);
                }

                throw CreateLifecycleException(ctx, operation, "hide", exception);
            }

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 供导航器等内部调用方使用：在不触发打开生命周期（不重新 Init/不走池化解析）
        /// 的前提下重新显示一个已存在的 Context。同样经由该 Context 类型对应的 key
        /// 队列 FIFO 执行。
        /// </summary>
        internal UniTask ShowCoreAsync(BaseContext ctx, object args = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            if (ctx == null)
            {
                return UniTask.CompletedTask;
            }

            var contextType = ctx.GetType();
            return _coordinator.EnqueueAsync(
                contextType,
                "Show",
                _ => ShowCoreExecuteAsync(ctx, args),
                cancellationToken);
        }

        internal async UniTask ShowForNavigationAsync(
            BaseContext context,
            object args,
            CancellationToken cancellationToken)
        {
            if (context == null)
            {
                return;
            }

            using (EnterNavigationCallbackType(context.GetType()))
            {
                await ShowCoreAsync(context, args, cancellationToken);
            }
        }

        private UniTask ShowCoreExecuteAsync(BaseContext ctx, object args)
        {
            if (ctx.State == UIContextState.Opened)
            {
                return UniTask.CompletedTask;
            }

            using var operation = BeginContextOperation(
                ctx,
                UIOperationKind.Show,
                CancellationToken.None,
                false);
            try
            {
                ctx.TransitionTo(UIContextState.Opening);
                if (ctx.ViewObject != null)
                {
                    ctx.ViewObject.SetActive(true);
                }

                if (ctx.View != null && ctx.View.RectTransform != null)
                {
                    ctx.SortingLease = _layerManager.AddToLayer(ctx.Layer, ctx.View.RectTransform);
                    PrepareContextRuntime(ctx);
                }

                ctx.OnShow(args);
                ctx.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(ctx);
            }
            catch (Exception exception)
            {
                ctx.RecordFailure(exception, false);
                if (ctx.ViewObject != null)
                {
                    ctx.ViewObject.SetActive(false);
                }

                if (ctx.State == UIContextState.Opening)
                {
                    ctx.TransitionTo(UIContextState.Hidden);
                }

                throw CreateLifecycleException(ctx, operation, "show", exception);
            }

            return UniTask.CompletedTask;
        }

        private async UniTask<T> OpenExistingAsync<T>(
            BaseContext context,
            UIConfig config,
            object args,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            if (context.View == null || context.ViewObject == null)
            {
                throw new InvalidOperationException(
                    $"Context {typeof(T).Name} has lost its runtime binding.");
            }

            var stableState = context.State;
            if (stableState != UIContextState.Opened &&
                stableState != UIContextState.Hidden)
            {
                throw new InvalidOperationException(
                    $"Cannot open active context {typeof(T).Name} from state {stableState}.");
            }

            using var operation = BeginContextOperation(
                context,
                UIOperationKind.Open,
                cancellationToken,
                false);
            context.CloseDisposition = UICloseDisposition.None;
            context.TransitionTo(UIContextState.Opening);
            var previousSortingPosition = context.SortingLease == null
                ? -1
                : _layerManager.GetPosition(context.SortingLease);
            try
            {
                context.SortingLease = _layerManager.AddToLayer(context.Layer, context.View.RectTransform);
                PrepareContextRuntime(context);
                context.ViewObject.SetActive(true);
                context.OnShow(args);
                await PlayShowTransitionAsync(context, config, operation.Token);
                context.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(context);
                return (T)context;
            }
            catch (OperationCanceledException cancellation)
            {
                var rollbackError = TryRollbackExistingOpen(
                    context,
                    stableState,
                    previousSortingPosition);
                if (rollbackError != null)
                {
                    var combined = new AggregateException(cancellation, rollbackError);
                    throw HandleTerminalLifecycleFailure(
                        context,
                        typeof(T),
                        ResolvePrefabKey(context, config),
                        operation,
                        "open-cancel-rollback",
                        combined);
                }

                throw;
            }
            catch (Exception exception)
            {
                context.RecordFailure(exception, false);
                var rollbackError = TryRollbackExistingOpen(
                    context,
                    stableState,
                    previousSortingPosition);
                if (rollbackError == null)
                {
                    throw CreateLifecycleException(
                        context,
                        operation,
                        "open-existing",
                        exception);
                }

                var combined = new AggregateException(exception, rollbackError);
                throw HandleTerminalLifecycleFailure(
                    context,
                    typeof(T),
                    ResolvePrefabKey(context, config),
                    operation,
                    "open-existing-rollback",
                    combined);
            }
        }

        private async UniTask<T> OpenPooledAsync<T>(
            UIPooledObject pooled,
            UIConfig config,
            object args,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            var contextType = typeof(T);
            var context = pooled.Context;
            var viewObject = pooled.ViewObject;
            using var operation = BeginContextOperation(
                context,
                UIOperationKind.Open,
                cancellationToken,
                false);

            try
            {
                var view = ResolveOrCreateView(context, viewObject);
                context.BindRuntime(this, config.Id, config.Layer, view, viewObject);
                context.IsModal = ResolveModal(config);
                context.CloseDisposition = UICloseDisposition.None;
                context.TransitionTo(UIContextState.Opening);
                view.Context = context;
                context.SortingLease = _layerManager.AddToLayer(config.Layer, view.RectTransform);
                PrepareContextRuntime(context);
                viewObject.SetActive(true);
                context.OnShow(args);
                await PlayShowTransitionAsync(context, config, operation.Token);
                context.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(context);

                _activeContexts[contextType] = context;
                _contextPrefabKeys[context] = pooled.PrefabKey;
                return (T)context;
            }
            catch (OperationCanceledException cancellation)
            {
                var rollbackError = TryRollbackPooledOpen(
                    contextType,
                    context,
                    pooled.PrefabKey,
                    config);
                if (rollbackError != null)
                {
                    var combined = new AggregateException(cancellation, rollbackError);
                    throw HandleTerminalLifecycleFailure(
                        context,
                        contextType,
                        pooled.PrefabKey,
                        operation,
                        "open-pooled-cancel-rollback",
                        combined);
                }

                throw;
            }
            catch (Exception exception)
            {
                throw HandleTerminalLifecycleFailure(
                    context,
                    contextType,
                    pooled.PrefabKey,
                    operation,
                    "open-pooled",
                    exception);
            }
        }

        private async UniTask<T> OpenNewAsync<T>(
            UIConfig config,
            object args,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            var contextType = typeof(T);
            var context = Activator.CreateInstance<T>();
            var prefabKey = config.PrefabKey;
            var resourceAcquired = false;
            using var operation = BeginContextOperation(
                context,
                UIOperationKind.Open,
                cancellationToken,
                false);

            try
            {
                context.TransitionTo(UIContextState.Loading);
                GameObject prefab;
                var loaderType = _resourceLoader?.GetType().Name ?? "UnknownLoader";
                try
                {
                    prefab = await _resourceLoader.LoadPrefabAsync(
                        prefabKey,
                        operation.Token);
                }
                catch (ResourceLoadException exception)
                {
                    throw new InvalidOperationException(
                        BuildPrefabLoadErrorMessage(
                            contextType,
                            config,
                            exception.LoaderType,
                            exception.DetailMessage),
                        exception);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        BuildPrefabLoadErrorMessage(
                            contextType,
                            config,
                            loaderType,
                            exception.Message),
                        exception);
                }

                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        BuildPrefabLoadErrorMessage(
                            contextType,
                            config,
                            loaderType,
                            "Loader returned a null prefab."));
                }

                resourceAcquired = true;
                operation.Token.ThrowIfCancellationRequested();
                var instance = UnityEngine.Object.Instantiate(prefab);
                var view = instance.GetComponent<UIView>() ?? instance.AddComponent<UIView>();

                context.TransitionTo(UIContextState.Initializing);
                context.BindRuntime(this, config.Id, config.Layer, view, instance);
                context.IsModal = ResolveModal(config);
                context.CloseDisposition = UICloseDisposition.None;
                view.Context = context;
                context.SortingLease = _layerManager.AddToLayer(config.Layer, view.RectTransform);
                PrepareContextRuntime(context);
                context.OnInit();

                context.TransitionTo(UIContextState.Opening);
                operation.Token.ThrowIfCancellationRequested();
                instance.SetActive(true);
                context.OnShow(args);
                await PlayShowTransitionAsync(context, config, operation.Token);
                context.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(context);

                _activeContexts[contextType] = context;
                _contextPrefabKeys[context] = prefabKey;
                return context;
            }
            catch (OperationCanceledException cancellation)
            {
                var rollbackError = TryRollbackNewOpen(context);
                var cleanupError = ReleaseContextInternal(
                    context,
                    prefabKey,
                    resourceAcquired);
                if (rollbackError != null || cleanupError != null)
                {
                    var errors = new List<Exception> { cancellation };
                    if (rollbackError != null)
                    {
                        errors.Add(rollbackError);
                    }

                    if (cleanupError != null)
                    {
                        errors.Add(cleanupError);
                    }

                    throw CreateLifecycleException(
                        context,
                        operation,
                        "open-new-cancel-rollback",
                        new AggregateException(errors));
                }

                throw;
            }
            catch (Exception exception)
            {
                context.RecordFailure(exception, true);
                var cleanupError = ReleaseContextInternal(
                    context,
                    prefabKey,
                    resourceAcquired);
                var failure = cleanupError == null
                    ? exception
                    : new AggregateException(exception, cleanupError);
                throw CreateLifecycleException(
                    context,
                    operation,
                    "open-new",
                    failure);
            }
        }

        private Exception TryRollbackExistingOpen(
            BaseContext context,
            UIContextState stableState,
            int previousSortingPosition)
        {
            try
            {
                if (previousSortingPosition >= 0 && context.SortingLease != null)
                {
                    _layerManager.RestorePosition(
                        context.SortingLease,
                        previousSortingPosition);
                    _rootRuntime.Modals.Apply();
                }

                if (stableState == UIContextState.Opened)
                {
                    context.TransitionTo(UIContextState.Opened);
                    _rootRuntime.Interaction.SetVisible(context, true);
                    return null;
                }

                context.TransitionTo(UIContextState.Hiding);
                context.OnHide();
                if (context.ViewObject != null)
                {
                    context.ViewObject.SetActive(false);
                }

                context.TransitionTo(UIContextState.Hidden);
                _rootRuntime.Interaction.SetVisible(context, false);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private Exception TryRollbackPooledOpen(
            Type contextType,
            BaseContext context,
            string prefabKey,
            UIConfig config)
        {
            try
            {
                context.TransitionTo(UIContextState.Hiding);
                context.OnHide();
                if (context.ViewObject != null)
                {
                    context.ViewObject.SetActive(false);
                }

                context.TransitionTo(UIContextState.Hidden);
                ReleaseContextRuntime(context);
                context.CloseDisposition = UICloseDisposition.Pool;
                context.TransitionTo(UIContextState.Closing);
                context.OnClose();

                var rollbackEntry = new UIPooledObject(
                    contextType,
                    prefabKey,
                    context,
                    context.ViewObject);
                var rollbackPolicy = UIPoolPolicy.FromConfig(config);
                if (_objectPool.TryRelease(
                        contextType,
                        rollbackEntry,
                        rollbackPolicy,
                        out var overflow))
                {
                    context.TransitionTo(UIContextState.Pooled);
                    return null;
                }

                context.CloseDisposition = UICloseDisposition.Release;
                return ReleaseContextInternal(
                    overflow?.Context ?? context,
                    overflow?.PrefabKey ?? prefabKey,
                    true);
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private static Exception TryRollbackNewOpen(BaseContext context)
        {
            if (context.State != UIContextState.Opening)
            {
                return null;
            }

            try
            {
                context.TransitionTo(UIContextState.Hiding);
                context.OnHide();
                if (context.ViewObject != null)
                {
                    context.ViewObject.SetActive(false);
                }

                context.TransitionTo(UIContextState.Hidden);
                context.CloseDisposition = UICloseDisposition.Release;
                context.TransitionTo(UIContextState.Closing);
                context.OnClose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private UILifecycleException HandleTerminalLifecycleFailure(
            BaseContext context,
            Type contextType,
            string prefabKey,
            UIContextOperation operation,
            string phase,
            Exception exception)
        {
            _activeContexts.TryGetValue(contextType, out var active);
            if (ReferenceEquals(active, context))
            {
                _activeContexts.Remove(contextType);
            }

            if (context.State != UIContextState.Released)
            {
                context.RecordFailure(exception, true);
            }

            var cleanupError = ReleaseContextInternal(context, prefabKey, true);
            var failure = cleanupError == null
                ? exception
                : new AggregateException(exception, cleanupError);
            return CreateLifecycleException(context, operation, phase, failure);
        }

        private Exception ReleaseContextInternal(
            BaseContext context,
            string prefabKey,
            bool releaseResource)
        {
            if (context == null || context.State == UIContextState.Released)
            {
                return null;
            }

            var errors = new List<Exception>();
            try
            {
                ReleaseContextRuntime(context);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                context.TransitionTo(UIContextState.Releasing);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            if (context.IsInitialized)
            {
                try
                {
                    context.OnDestroy();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (releaseResource)
            {
                try
                {
                    _resourceLoader.Release(prefabKey, context.ViewObject);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            try
            {
                context.CancelLifetime();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                context.TransitionTo(UIContextState.Released);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            _contextPrefabKeys.Remove(context);
            if (errors.Count == 0)
            {
                return null;
            }

            var failure = new AggregateException(
                $"Failed to release UI context {context.GetType().Name}.",
                errors);
            context.RecordFailure(failure, false);
            return failure;
        }

        private static UILifecycleException CreateLifecycleException(
            BaseContext context,
            UIContextOperation operation,
            string phase,
            Exception exception)
        {
            return new UILifecycleException(
                context.GetType(),
                context.State,
                operation.Id,
                operation.Kind,
                phase,
                exception);
        }

        private UIContextOperation BeginContextOperation(
            BaseContext context,
            UIOperationKind kind,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
        {
            CancellationToken serviceToken;
            lock (_operationGate)
            {
                if (_shuttingDown && !allowDuringShutdown)
                {
                    throw new InvalidOperationException(
                        "UIManager is shutting down and cannot accept new operations.");
                }

                _inFlightOperationCount++;
                serviceToken = allowDuringShutdown
                    ? CancellationToken.None
                    : _serviceLifetimeCancellation.Token;
            }

            try
            {
                return context.BeginOperation(
                    kind,
                    cancellationToken,
                    serviceToken,
                    OnContextOperationDisposed);
            }
            catch
            {
                OnContextOperationDisposed();
                throw;
            }
        }

        private void OnContextOperationDisposed()
        {
            lock (_operationGate)
            {
                _inFlightOperationCount--;
                if (_inFlightOperationCount < 0)
                {
                    _inFlightOperationCount = 0;
                    throw new InvalidOperationException(
                        "UI operation tracking count became negative.");
                }
            }
        }

        private async UniTask WaitForInFlightOperationsAsync()
        {
            while (true)
            {
                lock (_operationGate)
                {
                    if (_inFlightOperationCount == 0)
                    {
                        return;
                    }
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        private void ClearPools(Type contextType)
        {
            if (_objectPool == null)
            {
                return;
            }

            var errors = new List<Exception>();
            void DestroyAndCollect(UIPooledObject pooled)
            {
                try
                {
                    DestroyPooledObject(pooled, false);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            if (contextType == null)
            {
                _objectPool.Clear(DestroyAndCollect);
            }
            else
            {
                _objectPool.Clear(contextType, DestroyAndCollect);
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    "One or more pooled UI contexts failed while clearing.",
                    errors);
            }
        }

        private IDisposable EnterNavigationCallbackType(Type contextType)
        {
            _navigationCallbackTypes.TryGetValue(contextType, out var count);
            _navigationCallbackTypes[contextType] = count + 1;
            return new NavigationCallbackScope(this, contextType);
        }

        private void ExitNavigationCallbackType(Type contextType)
        {
            if (!_navigationCallbackTypes.TryGetValue(contextType, out var count) || count <= 1)
            {
                _navigationCallbackTypes.Remove(contextType);
                return;
            }

            _navigationCallbackTypes[contextType] = count - 1;
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "UIManager is not initialized. Call Initialize(IResourceLoader) first.");
            }
        }

        private void EnsureCanInitialize()
        {
            lock (_shutdownGate)
            {
                if (_shutdownTask != null)
                {
                    throw new InvalidOperationException(
                        "UIManager shutdown has not completed.");
                }
            }

            if (_initialized)
            {
                throw new InvalidOperationException(
                    "UIManager is already initialized. Call ShutdownAsync before initializing it again.");
            }
        }

        private void EnsureAcceptingOperations()
        {
            lock (_operationGate)
            {
                if (!_acceptingOperations)
                {
                    throw new InvalidOperationException(
                        "UIManager is shutting down and cannot accept new operations.");
                }
            }
        }

        private string ResolvePrefabKey(BaseContext context, UIConfig config)
        {
            if (context != null && _contextPrefabKeys.TryGetValue(context, out var storedKey))
            {
                return storedKey;
            }

            return config?.PrefabKey ?? string.Empty;
        }

        private static UIView ResolveOrCreateView(BaseContext context, GameObject viewObject)
        {
            return context.View ?? viewObject.GetComponent<UIView>() ?? viewObject.AddComponent<UIView>();
        }

        private void DestroyPooledObject(
            UIPooledObject pooledObject,
            bool allowDuringShutdown = false)
        {
            if (pooledObject == null)
            {
                return;
            }

            DestroyContextInternal(
                pooledObject.Context,
                pooledObject.PrefabKey,
                allowDuringShutdown);
        }

        private void DestroyContextInternal(
            BaseContext context,
            string prefabKey,
            bool allowDuringShutdown = false)
        {
            if (context == null)
            {
                return;
            }

            context.CloseDisposition = UICloseDisposition.Release;
            if (!context.CurrentOperationId.IsValid)
            {
                using var operation = BeginContextOperation(
                    context,
                    UIOperationKind.Release,
                    CancellationToken.None,
                    allowDuringShutdown);
                ThrowReleaseError(context, prefabKey);
                return;
            }

            ThrowReleaseError(context, prefabKey);
        }

        private void ThrowReleaseError(BaseContext context, string prefabKey)
        {
            var releaseError = ReleaseContextInternal(context, prefabKey, true);
            if (releaseError != null)
            {
                throw releaseError;
            }
        }

        private static string BuildPrefabLoadErrorMessage(Type contextType, UIConfig config, string loaderType, string detail)
        {
            var message =
                $"加载 UI Prefab 失败: type={contextType.Name}, id={config.Id}, key={config.PrefabKey}, loader={loaderType}。{detail}";

            if (string.Equals(loaderType, nameof(ResourcesLoader), StringComparison.Ordinal))
            {
                var normalizedKey = ResourcePathUtility.NormalizeResourcesKey(config.PrefabKey);
                message +=
                    $" 如果使用 ResourcesLoader，请确认文件位于 Assets/Resources/{normalizedKey}.prefab，且 PrefabKey 不包含 Assets/Resources/ 和 .prefab。";
            }

            return message;
        }

        private async UniTask PlayShowTransitionAsync(
            BaseContext context,
            UIConfig config,
            CancellationToken cancellationToken)
        {
            if (context?.View?.RectTransform == null || !IsTransitionEnabled(config))
            {
                return;
            }

            await _transitionRunner.PlayShowAsync(
                context.View.RectTransform,
                config.ToTransitionOptions(),
                cancellationToken);
        }

        private async UniTask PlayHideTransitionAsync(
            BaseContext context,
            UIConfig config,
            CancellationToken cancellationToken)
        {
            if (context?.View?.RectTransform == null || !IsTransitionEnabled(config))
            {
                return;
            }

            await _transitionRunner.PlayHideAsync(
                context.View.RectTransform,
                config.ToTransitionOptions(),
                cancellationToken);
        }

        private static bool IsTransitionEnabled(UIConfig config)
        {
            return config != null && config.UseTransition && config.TransitionType != UITransitionType.None;
        }

        private bool ResolveModal(UIConfig config)
        {
            return config.UseLayerModalPolicy
                ? _rootRuntime.LayerProfile.Get(config.Layer).Modal
                : config.Modal;
        }

        private void ActivateContextRuntime(BaseContext context)
        {
            if (context == null)
            {
                return;
            }

            _rootRuntime.Interaction.SetVisible(context, true);
            _rootRuntime.Focus.Activate(context);
            _rootRuntime.Modals.Activate(context);
        }

        private void PrepareContextRuntime(BaseContext context)
        {
            if (context == null)
            {
                return;
            }

            _rootRuntime.Interaction.SetVisible(context, false);
        }

        private void HideContextRuntime(BaseContext context)
        {
            if (context == null || _rootRuntime == null)
            {
                return;
            }

            _rootRuntime.Modals.Deactivate(context);
            _rootRuntime.Interaction.SetVisible(context, false);
            _rootRuntime.Focus.Deactivate(context);
        }

        private void ReleaseContextRuntime(BaseContext context)
        {
            if (context == null || _rootRuntime == null)
            {
                return;
            }

            _rootRuntime.Modals.Deactivate(context);
            _rootRuntime.Interaction.Remove(context);
            _rootRuntime.Focus.Deactivate(context);
            context.SortingLease?.Dispose();
            context.SortingLease = null;
            _rootRuntime.Modals.Apply();
        }

        private sealed class NavigationCallbackScope : IDisposable
        {
            private UIManager _owner;
            private readonly Type _contextType;

            public NavigationCallbackScope(UIManager owner, Type contextType)
            {
                _owner = owner;
                _contextType = contextType;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.ExitNavigationCallbackType(_contextType);
            }
        }
    }
}
