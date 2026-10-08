using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private readonly Dictionary<BaseContext, IUIInstanceLease> _contextInstanceLeases =
            new Dictionary<BaseContext, IUIInstanceLease>();
        private readonly Dictionary<BaseContext, UIPoolScope> _contextScopes =
            new Dictionary<BaseContext, UIPoolScope>();
        private readonly Dictionary<Guid, PoolScopeState> _poolScopes =
            new Dictionary<Guid, PoolScopeState>();
        private readonly Dictionary<int, UIPoolScope> _sceneScopes =
            new Dictionary<int, UIPoolScope>();
        private readonly Dictionary<Type, int> _navigationCallbackTypes = new Dictionary<Type, int>();
        private readonly Dictionary<Type, BaseContext> _transitioningContexts =
            new Dictionary<Type, BaseContext>();
        private readonly object _operationGate = new object();
        private readonly object _shutdownGate = new object();
        private readonly IUITransitionClock _transitionClock;

        private IResourceLoader _resourceLoader;
        private IUIResourceService _resourceService;
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
        public UITransitionRunner Transitions => _transitionRunner;
        [Obsolete("Use Transitions. TransitionRunner will be removed after the Y2 migration window.")]
        public UITransitionRunner TransitionRunner => Transitions;
        public UIRootRuntime RootRuntime => _rootRuntime;
        /// <summary>
        /// 阶段 5 资源所有权服务；仅在使用 <see cref="Initialize(IUIResourceService, IUIObjectPool)"/>
        /// 注入时非空。使用旧版 <see cref="IResourceLoader"/> 初始化时为 null。
        /// </summary>
        public IUIResourceService ResourceService => _resourceService;
        public UILayerManager LayerManager => _layerManager;
        public UIInputLockService InputLocks => _rootRuntime?.InputLocks;
        public UIInputRouter Input => _rootRuntime?.Input;
        public UIFocusService Focus => _rootRuntime?.Focus;
        public UIModalService Modals => _rootRuntime?.Modals;
        public int LastShutdownInputLockLeakCount { get; private set; }
        public UIPoolDiagnosticsSnapshot PoolDiagnostics =>
            _objectPool?.GetDiagnostics() ??
            new UIPoolDiagnosticsSnapshot(
                Array.Empty<UIPoolEntrySnapshot>(),
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        public bool IsInitialized => _initialized;
        internal CancellationToken ServiceLifetimeToken =>
            _serviceLifetimeCancellation?.Token ?? CancellationToken.None;
        internal bool IsNavigationCallbackType(Type contextType) =>
            contextType != null && _navigationCallbackTypes.ContainsKey(contextType);

        IUINavigator IUIService.Navigator => Navigator;
        IUIMessageBus IUIService.MessageBus => MessageCenter;

        public UIManager(IUITransitionClock transitionClock = null)
        {
            _transitionClock = transitionClock;
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
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            InitializeCore(loader, null, rootRuntime, pool);
        }

        /// <summary>
        /// 使用阶段 5 资源所有权服务初始化。资源与实例租约由 <see cref="IUIResourceService"/> 管理，
        /// UIManager 为每个 context 明确持有一份实例租约。
        /// </summary>
        public void Initialize(IUIResourceService resourceService, IUIObjectPool pool = null)
        {
            EnsureCanInitialize();
            var runtime = UIRootRuntime.CreateCompatible();
            try
            {
                Initialize(resourceService, runtime, pool);
            }
            catch
            {
                runtime.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 使用阶段 5 资源所有权服务与指定 UIRoot 初始化。
        /// </summary>
        public void Initialize(
            IUIResourceService resourceService,
            UIRootRuntime rootRuntime,
            IUIObjectPool pool = null)
        {
            if (resourceService == null)
            {
                throw new ArgumentNullException(nameof(resourceService));
            }

            InitializeCore(null, resourceService, rootRuntime, pool);
        }

        private void InitializeCore(
            IResourceLoader loader,
            IUIResourceService resourceService,
            UIRootRuntime rootRuntime,
            IUIObjectPool pool)
        {
            EnsureCanInitialize();

            _resourceLoader = loader;
            _resourceService = resourceService;
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
            _transitionRunner = new UITransitionRunner(_transitionClock);
            LastShutdownInputLockLeakCount = 0;
            Application.lowMemory += OnApplicationLowMemory;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _initialized = true;
        }

        public UniTask InitializeAsync(
            IUIResourceService resourceService,
            IUIObjectPool pool = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Initialize(resourceService, pool);
            return UniTask.CompletedTask;
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

            if (config.PreloadCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(config),
                    "UIConfig.PreloadCount cannot be negative.");
            }

            if (config.PreloadCount > 0 &&
                (!config.CacheOnClose || config.MaxPoolSize <= 0))
            {
                throw new ArgumentException(
                    "Instance prewarming requires CacheOnClose and a positive MaxPoolSize.",
                    nameof(config));
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

        public UIPoolScope CreateModuleScope(string moduleName)
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            var scope = UIPoolScope.CreateModule(moduleName);
            _poolScopes.Add(
                scope.Id,
                new PoolScopeState(scope, _serviceLifetimeCancellation.Token));
            return scope;
        }

        public UIPoolScope GetSceneScope(Scene scene)
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            if (!scene.IsValid())
            {
                throw new ArgumentException("A valid scene is required.", nameof(scene));
            }

            if (_sceneScopes.TryGetValue(scene.handle, out var existing))
            {
                return existing;
            }

            var scope = UIPoolScope.CreateScene(scene.handle, scene.name);
            _sceneScopes.Add(scene.handle, scope);
            _poolScopes[scope.Id] =
                new PoolScopeState(scope, _serviceLifetimeCancellation.Token);
            return scope;
        }

        public UniTask<int> PrewarmAsync<T>(
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            return PrewarmAsync<T>(UIPoolScope.Global, cancellationToken);
        }

        public UniTask<int> PrewarmAsync<T>(
            UIPoolScope scope,
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            cancellationToken.ThrowIfCancellationRequested();
            scope = NormalizeScope(scope);
            EnsureScopeAlive(scope);
            if (!_configRegistry.TryGetValue(typeof(T), out var config))
            {
                throw new KeyNotFoundException($"未注册 UI Context: {typeof(T).Name}");
            }

            return PrewarmTypeAsync(typeof(T), config, scope, cancellationToken);
        }

        public async UniTask<int> PrewarmRegisteredAsync(
            UIPoolScope scope = default,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            scope = NormalizeScope(scope);
            EnsureScopeAlive(scope);

            var total = 0;
            var registrations = new List<KeyValuePair<Type, UIConfig>>(_configRegistry);
            foreach (var registration in registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (registration.Value.PreloadCount <= 0)
                {
                    continue;
                }

                total += await PrewarmTypeAsync(
                    registration.Key,
                    registration.Value,
                    scope,
                    cancellationToken);
            }

            return total;
        }

        private UniTask<int> PrewarmTypeAsync(
            Type contextType,
            UIConfig config,
            UIPoolScope scope,
            CancellationToken cancellationToken)
        {
            return _coordinator.EnqueueAsync(
                contextType,
                "Prewarm",
                ct => PrewarmTypeCoreAsync(contextType, config, scope, ct),
                cancellationToken);
        }

        private async UniTask<int> PrewarmTypeCoreAsync(
            Type contextType,
            UIConfig config,
            UIPoolScope scope,
            CancellationToken cancellationToken)
        {
            EvictExpiredPoolEntries();
            var target = Math.Min(
                Math.Min(config.PreloadCount, config.MaxPoolSize),
                _objectPool.GetDiagnostics().GlobalCapacity);
            var missing = Math.Max(0, target - _objectPool.Count(contextType, scope));
            if (missing == 0)
            {
                return 0;
            }

            var created = new List<UIPooledObject>(missing);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                GetScopeServiceToken(scope));
            try
            {
                for (var index = 0; index < missing; index++)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    EnsureScopeAlive(scope);
                    var entry = await CreatePrewarmedContextAsync(
                        contextType,
                        config,
                        scope,
                        linked.Token);
                    created.Add(entry);
                }

                return created.Count;
            }
            catch (Exception original)
            {
                var errors = new List<Exception>();
                foreach (var entry in created)
                {
                    if (!_objectPool.Remove(entry))
                    {
                        continue;
                    }

                    try
                    {
                        DestroyPooledObject(entry);
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }

                if (errors.Count > 0)
                {
                    errors.Insert(0, original);
                    throw new AggregateException(
                        "Prewarm rollback did not clean every created instance.",
                        errors);
                }

                throw;
            }
        }

        public UniTask<T> OpenAsync<T>(
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            return OpenInScopeAsync<T>(
                UIPoolScope.Global,
                args,
                cancellationToken);
        }

        public bool RequestTransitionInterruption<T>(
            UITransitionInterruption interruption)
            where T : BaseContext
        {
            EnsureInitialized();
            if (!_acceptingOperations)
            {
                return false;
            }

            var type = typeof(T);
            if (!_transitioningContexts.TryGetValue(type, out var context))
            {
                _activeContexts.TryGetValue(type, out context);
            }

            return RequestTransitionInterruption(context, interruption);
        }

        public bool RequestTransitionInterruption(
            BaseContext context,
            UITransitionInterruption interruption)
        {
            if (!_initialized || !_acceptingOperations ||
                context == null ||
                context.View == null ||
                context.View.RectTransform == null)
            {
                return false;
            }

            return _transitionRunner.RequestInterruption(
                context.View.RectTransform,
                interruption);
        }

        public void RefreshTransitionBaseline(BaseContext context)
        {
            EnsureInitialized();
            if (context == null ||
                context.View == null ||
                context.View.RectTransform == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _transitionRunner.CaptureBaseline(context.View.RectTransform, true);
        }

        public UniTask<T> OpenInScopeAsync<T>(
            UIPoolScope scope,
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            cancellationToken.ThrowIfCancellationRequested();
            scope = NormalizeScope(scope);
            EnsureScopeAlive(scope);

            return OpenCoordinatedAsync<T>(scope, args, cancellationToken);
        }

        private UniTask<T> OpenCoordinatedAsync<T>(
            UIPoolScope scope,
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
                scope,
                !_activeContexts.ContainsKey(contextType) &&
                (_objectPool == null || _objectPool.Count(contextType, scope) == 0),
                (markFirstCreation, ct) => OpenRoutedAsync<T>(
                    contextType,
                    config,
                    scope,
                    args,
                    markFirstCreation,
                    ct),
                cancellationToken,
                GetScopeServiceToken(scope));
        }

        private async UniTask<T> OpenRoutedAsync<T>(
            Type contextType,
            UIConfig config,
            UIPoolScope scope,
            object args,
            Action markFirstCreation,
            CancellationToken cancellationToken)
            where T : BaseContext
        {
            if (_activeContexts.TryGetValue(contextType, out var cachedContext))
            {
                if (ResolveContextScope(cachedContext) != scope)
                {
                    throw new InvalidOperationException(
                        $"{contextType.Name} is already active in scope " +
                        $"{ResolveContextScope(cachedContext)} and cannot be reopened in {scope}.");
                }

                return await OpenExistingAsync<T>(
                    cachedContext,
                    config,
                    args,
                    cancellationToken);
            }

            // 池化实例可能被框架外部销毁，这类条目会被对象池在 TryGet 时静默丢弃，
            // 其实例租约必须在这里回收，否则引用计数永远不会归零。
            ReclaimOrphanedInstanceLeases();

            if (_objectPool.TryGet(
                    contextType,
                    scope,
                    FinalizeInvalidPooledObject,
                    out var pooled))
            {
                return await OpenPooledAsync<T>(
                    pooled,
                    config,
                    scope,
                    args,
                    cancellationToken);
            }

            // Only a genuinely brand-new instantiation is eligible for single-flight
            // merging with a later equivalent concurrent Open request.
            markFirstCreation();
            return await OpenNewAsync<T>(config, scope, args, cancellationToken);
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
                return await OpenCoordinatedAsync<T>(
                    UIPoolScope.Global,
                    args,
                    cancellationToken);
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
            var scope = ResolveContextScope(ctx);
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
                _objectPool != null &&
                IsScopeAlive(scope);
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
                        await PlayHideTransitionAsync(
                            ctx,
                            config,
                            operation.Id,
                            operation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        NormalizeTransitionVisual(ctx);
                        ctx.CloseDisposition = UICloseDisposition.None;
                        ctx.TransitionTo(UIContextState.Opened);
                        throw;
                    }

                    ThrowDisplayCleanupError(ctx);
                    ctx.OnHide();
                    DeactivateView(ctx);

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
                            scope,
                            "Open",
                            _contextInstanceLeases.ContainsKey(ctx),
                            out var overflow,
                            out var rejection))
                    {
                        try
                        {
                            DestroyOverflow(overflow, false);
                        }
                        catch
                        {
                            _objectPool.Remove(pooledObject);
                            throw;
                        }

                        ctx.TransitionTo(UIContextState.Pooled);
                        return;
                    }

                    if (overflow != null)
                    {
                        ctx.CloseDisposition = UICloseDisposition.Release;
                        DestroyContextInternal(overflow.Context, overflow.PrefabKey);
                        return;
                    }

                    throw new InvalidOperationException(
                        $"Pool rejected {contextType.Name}: {rejection}.");
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
            Application.lowMemory -= OnApplicationLowMemory;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

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
            _contextScopes.Clear();

            // 兜底：关闭时释放任何仍被持有的实例租约，保证引用计数归零。
            if (_contextInstanceLeases.Count > 0)
            {
                foreach (var lease in new List<IUIInstanceLease>(_contextInstanceLeases.Values))
                {
                    try
                    {
                        lease.Release();
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }

                _contextInstanceLeases.Clear();
            }
            _navigationCallbackTypes.Clear();
            foreach (var scope in _poolScopes.Values)
            {
                try
                {
                    scope.Dispose();
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }

            _poolScopes.Clear();
            _sceneScopes.Clear();
            LastShutdownInputLockLeakCount = _rootRuntime?.InputLocks.ActiveLockCount ?? 0;
            try
            {
                _rootRuntime?.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                _transitionRunner?.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            _resourceLoader = null;
            _resourceService = null;
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
            EnsureAcceptingOperations();
            if (_coordinator.IsBusy(typeof(T)))
            {
                throw new InvalidOperationException(
                    $"Cannot synchronously clear {typeof(T).Name} while its operation lane is busy. " +
                    "Use ClearPoolAsync<T> instead.");
            }

            ClearPools(typeof(T));
        }

        public void ClearAllPools()
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            foreach (var contextType in _configRegistry.Keys)
            {
                if (_coordinator.IsBusy(contextType))
                {
                    throw new InvalidOperationException(
                        "Cannot synchronously clear all pools while an operation lane is busy.");
                }
            }

            ClearPools(null);
        }

        public UniTask ClearPoolAsync<T>(
            CancellationToken cancellationToken = default)
            where T : BaseContext
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            return _coordinator.EnqueueAsync(
                typeof(T),
                "ClearPool",
                _ =>
                {
                    ClearPools(typeof(T));
                    return UniTask.CompletedTask;
                },
                cancellationToken);
        }

        public async UniTask ReleaseScopeAsync(
            UIPoolScope scope,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            scope = NormalizeScope(scope);
            if (scope.Kind == UIPoolScopeKind.Global)
            {
                throw new InvalidOperationException("The global UI pool scope cannot be released.");
            }

            if (!_poolScopes.TryGetValue(scope.Id, out var state) || state.Ended)
            {
                return;
            }

            var errors = new List<Exception>();
            try
            {
                state.End();
            }
            catch (Exception exception)
            {
                AddFlattened(errors, exception);
            }
            try
            {
                var registeredTypes = new List<Type>(_configRegistry.Keys);
                foreach (var contextType in registeredTypes)
                {
                    try
                    {
                        await _coordinator.EnqueueAsync(
                            contextType,
                            "ReleaseScopeBarrier",
                            _ => UniTask.CompletedTask,
                            CancellationToken.None);
                    }
                    catch (Exception exception)
                    {
                        AddFlattened(errors, exception);
                    }
                }

                try
                {
                    _objectPool.ClearScope(
                        scope,
                        pooled => DestroyPooledObject(pooled));
                }
                catch (Exception exception)
                {
                    AddFlattened(errors, exception);
                }
            }
            finally
            {
                try
                {
                    state.Dispose();
                }
                catch (Exception exception)
                {
                    AddFlattened(errors, exception);
                }

                if (_poolScopes.TryGetValue(scope.Id, out var current) &&
                    ReferenceEquals(current, state))
                {
                    _poolScopes.Remove(scope.Id);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (errors.Count > 0)
            {
                throw new AggregateException(
                    $"One or more pooled contexts failed while releasing scope {scope}.",
                    errors);
            }
        }

        public int EvictExpiredPoolEntries()
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            var errors = new List<Exception>();
            var count = 0;
            try
            {
                count = _objectPool.EvictExpired(
                    pooled => DestroyPooledObject(pooled));
            }
            catch (Exception exception)
            {
                AddFlattened(errors, exception);
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    "One or more expired pooled contexts failed cleanup.",
                    errors);
            }

            return count;
        }

        public int HandleLowMemory()
        {
            EnsureInitialized();
            EnsureAcceptingOperations();
            var errors = new List<Exception>();
            var released = 0;
            try
            {
                released += _objectPool.EvictIdle(
                    pooled => DestroyPooledObject(pooled));
            }
            catch (Exception exception)
            {
                AddFlattened(errors, exception);
            }

            if (_resourceService != null)
            {
                try
                {
                    released += _resourceService.HandleLowMemory();
                }
                catch (Exception exception)
                {
                    AddFlattened(errors, exception);
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(
                    "Low-memory cleanup completed with one or more failures.",
                    errors);
            }

            return released;
        }

        /// <summary>
        /// 供导航器等内部调用方使用：在不触发关闭生命周期的前提下隐藏一个已打开的
        /// Context。和 Open/Close 一样经由该 Context 类型对应的 key 队列 FIFO 执行，
        /// 不会绕过队列产生竞态。
        /// </summary>
        internal UniTask HideCoreAsync(BaseContext ctx, CancellationToken cancellationToken = default)
        {
            return HideCoreAsync(ctx, cancellationToken, false);
        }

        private UniTask HideCoreAsync(
            BaseContext ctx,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
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
                ct => HideCoreExecuteAsync(ctx, ct, allowDuringShutdown),
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

        internal async UniTask HideForNavigationRollbackAsync(BaseContext context)
        {
            if (context == null)
            {
                return;
            }

            using (EnterNavigationCallbackType(context.GetType()))
            {
                await HideCoreAsync(context, CancellationToken.None, true);
            }
        }

        private async UniTask HideCoreExecuteAsync(
            BaseContext ctx,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
        {
            if (ctx.State == UIContextState.Hidden)
            {
                return;
            }

            using var operation = BeginContextOperation(
                ctx,
                UIOperationKind.Hide,
                cancellationToken,
                allowDuringShutdown);
            try
            {
                ctx.TransitionTo(UIContextState.Hiding);
                _configRegistry.TryGetValue(ctx.GetType(), out var config);
                await PlayHideTransitionAsync(
                    ctx,
                    config,
                    operation.Id,
                    operation.Token);
                ThrowDisplayCleanupError(ctx);
                ctx.OnHide();
                DeactivateView(ctx);

                ctx.TransitionTo(UIContextState.Hidden);
                HideContextRuntime(ctx);
            }
            catch (OperationCanceledException)
            {
                NormalizeTransitionVisual(ctx);
                ctx.CloseDisposition = UICloseDisposition.None;
                if (ctx.State == UIContextState.Hiding)
                {
                    ctx.TransitionTo(UIContextState.Opened);
                }

                ActivateContextRuntime(ctx);
                throw;
            }
            catch (Exception exception)
            {
                ctx.RecordFailure(exception, false);
                NormalizeTransitionVisual(ctx);
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
        }

        /// <summary>
        /// 供导航器等内部调用方使用：在不触发打开生命周期（不重新 Init/不走池化解析）
        /// 的前提下重新显示一个已存在的 Context。同样经由该 Context 类型对应的 key
        /// 队列 FIFO 执行。
        /// </summary>
        internal UniTask ShowCoreAsync(BaseContext ctx, object args = null, CancellationToken cancellationToken = default)
        {
            return ShowCoreAsync(ctx, args, cancellationToken, false);
        }

        private UniTask ShowCoreAsync(
            BaseContext ctx,
            object args,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
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
                ct => ShowCoreExecuteAsync(ctx, args, ct, allowDuringShutdown),
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

        internal async UniTask ShowForNavigationRollbackAsync(
            BaseContext context,
            object args)
        {
            if (context == null)
            {
                return;
            }

            using (EnterNavigationCallbackType(context.GetType()))
            {
                await ShowCoreAsync(context, args, CancellationToken.None, true);
            }
        }

        private async UniTask ShowCoreExecuteAsync(
            BaseContext ctx,
            object args,
            CancellationToken cancellationToken,
            bool allowDuringShutdown)
        {
            if (ctx.State == UIContextState.Opened)
            {
                return;
            }

            using var operation = BeginContextOperation(
                ctx,
                UIOperationKind.Show,
                cancellationToken,
                allowDuringShutdown);
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

                ctx.BeginDisplayScope(args);
                ctx.OnShow(args);
                BlockUnderlyingInputDuringShow(ctx);
                _configRegistry.TryGetValue(ctx.GetType(), out var config);
                await PlayShowTransitionAsync(
                    ctx,
                    config,
                    operation.Id,
                    UITransitionRollbackState.Hidden,
                    operation.Token);
                ctx.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(ctx);
            }
            catch (OperationCanceledException)
            {
                DeactivateView(ctx);
                HideContextRuntime(ctx);
                if (ctx.State == UIContextState.Opening)
                {
                    ctx.TransitionTo(UIContextState.Hidden);
                }

                var cleanupError = ctx.EndDisplayScope();
                if (cleanupError != null)
                {
                    throw CreateLifecycleException(
                        ctx,
                        operation,
                        "show-cancel-cleanup",
                        cleanupError);
                }

                throw;
            }
            catch (Exception exception)
            {
                ctx.RecordFailure(exception, false);
                DeactivateView(ctx);
                HideContextRuntime(ctx);

                if (ctx.State == UIContextState.Opening)
                {
                    ctx.TransitionTo(UIContextState.Hidden);
                }

                var cleanupError = ctx.EndDisplayScope();
                throw CreateLifecycleException(
                    ctx,
                    operation,
                    "show",
                    cleanupError == null
                        ? exception
                        : new AggregateException(exception, cleanupError));
            }
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
                if (stableState != UIContextState.Opened)
                {
                    context.BeginDisplayScope(args);
                }
                context.OnShow(args);
                BlockUnderlyingInputDuringShow(context);
                await PlayShowTransitionAsync(
                    context,
                    config,
                    operation.Id,
                    stableState == UIContextState.Opened
                        ? UITransitionRollbackState.Visible
                        : UITransitionRollbackState.Hidden,
                    operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureScopeAlive(ResolveContextScope(context));
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
            UIPoolScope scope,
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
                if (IsTransitionEnabled(config) &&
                    config.RefreshTransitionBaselineOnReuse)
                {
                    _transitionRunner.CaptureBaseline(view.RectTransform, true);
                }
                viewObject.SetActive(true);
                context.BeginDisplayScope(args);
                context.OnShow(args);
                BlockUnderlyingInputDuringShow(context);
                await PlayShowTransitionAsync(
                    context,
                    config,
                    operation.Id,
                    UITransitionRollbackState.Hidden,
                    operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureScopeAlive(scope);
                context.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(context);

                _activeContexts[contextType] = context;
                _contextPrefabKeys[context] = pooled.PrefabKey;
                _contextScopes[context] = scope;
                return (T)context;
            }
            catch (OperationCanceledException cancellation)
            {
                var rollbackError = TryRollbackPooledOpen(
                    contextType,
                    context,
                    pooled.PrefabKey,
                    config,
                    scope);
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

        private async UniTask<UIPooledObject> CreatePrewarmedContextAsync(
            Type contextType,
            UIConfig config,
            UIPoolScope scope,
            CancellationToken cancellationToken)
        {
            var context = Activator.CreateInstance(contextType) as BaseContext
                ?? throw new InvalidOperationException(
                    $"{contextType.Name} could not be created as a BaseContext.");
            var resourceAcquired = false;
            var prefabKey = config.PrefabKey;
            UIPooledObject entry = null;
            using var operation = BeginContextOperation(
                context,
                UIOperationKind.Prewarm,
                cancellationToken,
                false);

            try
            {
                context.TransitionTo(UIContextState.Loading);
                GameObject instance;
                if (_resourceService != null)
                {
                    var lease = await _resourceService.InstantiateAsync(
                        UIResourceKey.Of<GameObject>(prefabKey),
                        null,
                        operation.Token);
                    instance = lease.Instance;
                    if (instance == null)
                    {
                        lease.Release();
                        throw new InvalidOperationException(
                            $"Resource service returned a null instance for {prefabKey}.");
                    }

                    _contextInstanceLeases.Add(context, lease);
                    resourceAcquired = true;
                }
                else
                {
                    var prefab = await _resourceLoader.LoadPrefabAsync(
                        prefabKey,
                        operation.Token);
                    if (prefab == null)
                    {
                        throw new InvalidOperationException(
                            BuildPrefabLoadErrorMessage(
                                contextType,
                                config,
                                _resourceLoader.GetType().Name,
                                "Loader returned a null prefab."));
                    }

                    resourceAcquired = true;
                    instance = UnityEngine.Object.Instantiate(prefab);
                }

                instance.SetActive(false);
                var view = instance.GetComponent<UIView>() ?? instance.AddComponent<UIView>();
                context.BindRuntime(this, config.Id, config.Layer, view, instance);
                context.IsModal = ResolveModal(config);
                context.CloseDisposition = UICloseDisposition.Pool;
                view.Context = context;
                operation.Token.ThrowIfCancellationRequested();
                EnsureScopeAlive(scope);
                context.TransitionTo(UIContextState.Initializing);
                context.OnInit();
                operation.Token.ThrowIfCancellationRequested();
                EnsureScopeAlive(scope);

                _contextPrefabKeys[context] = prefabKey;
                _contextScopes[context] = scope;
                context.TransitionTo(UIContextState.Pooled);
                entry = new UIPooledObject(contextType, prefabKey, context, instance);
                var accepted = _objectPool.TryRelease(
                    contextType,
                    entry,
                    UIPoolPolicy.FromConfig(config),
                    scope,
                    "Prewarm",
                    _contextInstanceLeases.ContainsKey(context),
                    out var overflow,
                    out var rejection);
                if (!accepted)
                {
                    throw new InvalidOperationException(
                        $"Pool rejected prewarmed {contextType.Name}: {rejection}.");
                }

                DestroyOverflow(overflow, false);
                return entry;
            }
            catch (Exception original)
            {
                if (entry != null)
                {
                    _objectPool.Remove(entry);
                }

                var cleanupError = ReleaseContextInternal(
                    context,
                    prefabKey,
                    resourceAcquired);
                if (cleanupError != null)
                {
                    throw new AggregateException(
                        $"Prewarm failed and cleanup also failed for {contextType.Name}.",
                        original,
                        cleanupError);
                }

                throw;
            }
        }

        private async UniTask<T> OpenNewAsync<T>(
            UIConfig config,
            UIPoolScope scope,
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
                GameObject instance;
                var loaderType = _resourceService != null
                    ? nameof(UIResourceService)
                    : _resourceLoader?.GetType().Name ?? "UnknownLoader";

                if (_resourceService != null)
                {
                    IUIInstanceLease instanceLease;
                    try
                    {
                        instanceLease = await _resourceService.InstantiateAsync(
                            UIResourceKey.Of<GameObject>(prefabKey),
                            null,
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

                    instance = instanceLease.Instance;
                    if (instance == null)
                    {
                        instanceLease.Release();
                        throw new InvalidOperationException(
                            BuildPrefabLoadErrorMessage(
                                contextType,
                                config,
                                loaderType,
                                "Resource service returned a null instance."));
                    }

                    // 先登记租约再检查取消，保证任何后续失败都能在回滚路径释放它。
                    resourceAcquired = true;
                    _contextInstanceLeases[context] = instanceLease;
                    operation.Token.ThrowIfCancellationRequested();
                }
                else
                {
                    GameObject prefab;
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
                    instance = UnityEngine.Object.Instantiate(prefab);
                }

                var view = instance.GetComponent<UIView>() ?? instance.AddComponent<UIView>();

                context.TransitionTo(UIContextState.Initializing);
                context.BindRuntime(this, config.Id, config.Layer, view, instance);
                context.IsModal = ResolveModal(config);
                context.CloseDisposition = UICloseDisposition.None;
                view.Context = context;
                context.SortingLease = _layerManager.AddToLayer(config.Layer, view.RectTransform);
                PrepareContextRuntime(context);
                context.OnInit();
                if (IsTransitionEnabled(config))
                {
                    _transitionRunner.CaptureBaseline(view.RectTransform);
                }

                context.TransitionTo(UIContextState.Opening);
                operation.Token.ThrowIfCancellationRequested();
                instance.SetActive(true);
                context.BeginDisplayScope(args);
                context.OnShow(args);
                BlockUnderlyingInputDuringShow(context);
                await PlayShowTransitionAsync(
                    context,
                    config,
                    operation.Id,
                    UITransitionRollbackState.Hidden,
                    operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureScopeAlive(scope);
                context.TransitionTo(UIContextState.Opened);
                ActivateContextRuntime(context);

                _activeContexts[contextType] = context;
                _contextPrefabKeys[context] = prefabKey;
                _contextScopes[context] = scope;
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
                ThrowDisplayCleanupError(context);
                context.OnHide();
                DeactivateView(context);

                context.TransitionTo(UIContextState.Hidden);
                HideContextRuntime(context);
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
            UIConfig config,
            UIPoolScope scope)
        {
            try
            {
                context.TransitionTo(UIContextState.Hiding);
                ThrowDisplayCleanupError(context);
                context.OnHide();
                DeactivateView(context);

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
                        scope,
                        "OpenRollback",
                        _contextInstanceLeases.ContainsKey(context),
                        out var overflow,
                        out _))
                {
                    try
                    {
                        DestroyOverflow(overflow, false);
                    }
                    catch
                    {
                        _objectPool.Remove(rollbackEntry);
                        throw;
                    }

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

        private Exception TryRollbackNewOpen(BaseContext context)
        {
            if (context.State != UIContextState.Opening)
            {
                return null;
            }

            try
            {
                context.TransitionTo(UIContextState.Hiding);
                ThrowDisplayCleanupError(context);
                context.OnHide();
                DeactivateView(context);

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

        /// <summary>
        /// 回收孤儿实例租约：池化实例可能被框架外部销毁，对象池会在 TryGet 时静默丢弃这些条目，
        /// 使它们再也不会走到 ReleaseContextInternal。若不回收，其资源引用计数将永远不归零，
        /// TrimUnused/低内存回收也无法生效。
        /// </summary>
        private void ReclaimOrphanedInstanceLeases()
        {
            _objectPool.RemoveInvalid(pooled =>
            {
                var context = pooled.Context;
                if (context == null)
                {
                    return;
                }

                _contextPrefabKeys.TryGetValue(context, out var prefabKey);
                var cleanupError = ReleaseContextInternal(context, prefabKey, true);
                if (cleanupError != null)
                {
                    Debug.LogWarning(
                        $"[UIManager] Failed to finalize an invalid pooled context: {cleanupError.Message}");
                }
            });

            if (_contextInstanceLeases.Count == 0)
            {
                return;
            }

            List<BaseContext> orphans = null;
            foreach (var pair in _contextInstanceLeases)
            {
                var context = pair.Key;
                var lease = pair.Value;
                if (!lease.IsReleased && lease.Instance != null)
                {
                    continue;
                }

                // 仍然是活动 context 时不回收，避免打断正在进行的生命周期。
                if (context != null
                    && _activeContexts.TryGetValue(context.GetType(), out var active)
                    && ReferenceEquals(active, context))
                {
                    continue;
                }

                if (orphans == null)
                {
                    orphans = new List<BaseContext>();
                }

                orphans.Add(context);
            }

            if (orphans == null)
            {
                return;
            }

            foreach (var context in orphans)
            {
                _contextPrefabKeys.TryGetValue(context, out var prefabKey);
                var cleanupError = ReleaseContextInternal(context, prefabKey, true);
                if (cleanupError != null)
                {
                    Debug.LogWarning(
                        $"[UIManager] Failed to reclaim an orphaned context: {cleanupError.Message}");
                }
            }
        }

        /// <summary>
        /// 归还该 context 持有的实例租约（幂等）。仅在阶段 5 资源服务模式下存在租约。
        /// </summary>
        private Exception ReleaseOwnedInstanceLease(BaseContext context, bool releaseResource)        {
            if (!releaseResource || !_contextInstanceLeases.TryGetValue(context, out var lease))
            {
                return null;
            }

            _contextInstanceLeases.Remove(context);
            try
            {
                // 幂等：销毁实例并归还其持有的资源租约。
                lease.Release();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private Exception ReleaseContextInternal(
            BaseContext context,
            string prefabKey,
            bool releaseResource)
        {
            if (context == null)
            {
                return null;
            }

            if (context.State == UIContextState.Released)
            {
                // 回滚可能已经把 context 推进到 Released，但实例租约是 UIManager 自己的
                // 所有权记账，必须补偿归还，否则会泄漏一份引用计数。
                var compensation = ReleaseOwnedInstanceLease(context, releaseResource);
                return compensation == null
                    ? null
                    : new AggregateException(
                        $"Failed to release UI resources for {context.GetType().Name}.",
                        compensation);
            }

            var errors = new List<Exception>();
            if (context.View != null && context.View.RectTransform != null)
            {
                _transitionRunner?.Forget(context.View.RectTransform);
            }

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

            if (_contextInstanceLeases.ContainsKey(context))
            {
                var leaseError = ReleaseOwnedInstanceLease(context, releaseResource);
                if (leaseError != null)
                {
                    errors.Add(leaseError);
                }
            }
            else if (releaseResource && _resourceLoader != null)
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
            _contextScopes.Remove(context);
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
            UIOperationId operationId,
            UITransitionRollbackState rollbackState,
            CancellationToken cancellationToken)
        {
            if (context == null ||
                context.View == null ||
                context.View.RectTransform == null ||
                !IsTransitionEnabled(config))
            {
                return;
            }

            var type = context.GetType();
            _transitioningContexts[type] = context;
            try
            {
                await _transitionRunner.PlayShowAsync(
                    context.View.RectTransform,
                    config.ToTransitionOptions(),
                    operationId,
                    rollbackState,
                    type,
                    IsNavigationCallbackType(type),
                    cancellationToken);
            }
            finally
            {
                if (_transitioningContexts.TryGetValue(type, out var active) &&
                    ReferenceEquals(active, context))
                {
                    _transitioningContexts.Remove(type);
                }
            }
        }

        private async UniTask PlayHideTransitionAsync(
            BaseContext context,
            UIConfig config,
            UIOperationId operationId,
            CancellationToken cancellationToken)
        {
            if (context == null ||
                context.View == null ||
                context.View.RectTransform == null ||
                !IsTransitionEnabled(config))
            {
                return;
            }

            var type = context.GetType();
            _transitioningContexts[type] = context;
            try
            {
                await _transitionRunner.PlayHideAsync(
                    context.View.RectTransform,
                    config.ToTransitionOptions(),
                    operationId,
                    type,
                    IsNavigationCallbackType(type),
                    cancellationToken);
            }
            finally
            {
                if (_transitioningContexts.TryGetValue(type, out var active) &&
                    ReferenceEquals(active, context))
                {
                    _transitioningContexts.Remove(type);
                }
            }
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

            context.SetRuntimeVisibility(true);
            _rootRuntime.Interaction.SetVisible(context, true);
            _rootRuntime.Focus.Activate(context);
            _rootRuntime.Modals.Activate(context);
        }

        private void BlockUnderlyingInputDuringShow(BaseContext context)
        {
            if (context == null ||
                context.ViewObject == null ||
                !context.IsModal)
            {
                return;
            }

            _rootRuntime.Modals.Activate(context);
            _rootRuntime.Interaction.Apply();
        }

        private void PrepareContextRuntime(BaseContext context)
        {
            if (context == null)
            {
                return;
            }

            context.SetRuntimeVisibility(false);
            _rootRuntime.Interaction.SetVisible(context, false);
        }

        private void HideContextRuntime(BaseContext context)
        {
            if (context == null || _rootRuntime == null)
            {
                return;
            }

            _rootRuntime.Modals.Deactivate(context);
            context.SetRuntimeVisibility(false);
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
            context.ClearVisibility();
            context.SortingLease?.Dispose();
            context.SortingLease = null;
            _rootRuntime.Modals.Apply();
        }

        internal void SetNavigationCoverage(
            BasePageContext context,
            bool covered)
        {
            if (context == null)
            {
                return;
            }

            var suspended =
                covered &&
                _configRegistry.TryGetValue(context.GetType(), out var config) &&
                config.SuspendWhenCovered;
            context.SetNavigationVisibility(covered, suspended);
            _rootRuntime?.Interaction.Apply();
        }

        private void DeactivateView(BaseContext context)
        {
            if (context == null || context.ViewObject == null)
            {
                return;
            }

            context.ViewObject.SetActive(false);
            NormalizeTransitionVisual(context);
        }

        private void NormalizeTransitionVisual(BaseContext context)
        {
            if (context != null &&
                context.View != null &&
                context.View.RectTransform != null)
            {
                _transitionRunner?.NormalizeVisible(context.View.RectTransform);
            }
        }

        private void FinalizeInvalidPooledObject(UIPooledObject pooledObject)
        {
            if (pooledObject == null)
            {
                return;
            }

            Debug.LogWarning(
                $"[UIManager] Finalizing externally destroyed pooled context " +
                $"{pooledObject.ContextType.Name} ({pooledObject.EntryId}).");
            DestroyPooledObject(pooledObject);
        }

        private void DestroyOverflow(
            UIPooledObject overflow,
            bool allowDuringShutdown)
        {
            if (overflow != null)
            {
                DestroyPooledObject(overflow, allowDuringShutdown);
            }
        }

        private static void ThrowDisplayCleanupError(BaseContext context)
        {
            var cleanupError = context?.EndDisplayScope();
            if (cleanupError != null)
            {
                throw cleanupError;
            }
        }

        private UIPoolScope NormalizeScope(UIPoolScope scope)
        {
            return scope.Kind == UIPoolScopeKind.Global
                ? UIPoolScope.Global
                : scope;
        }

        private UIPoolScope ResolveContextScope(BaseContext context)
        {
            return context != null && _contextScopes.TryGetValue(context, out var scope)
                ? scope
                : UIPoolScope.Global;
        }

        private bool IsScopeAlive(UIPoolScope scope)
        {
            scope = NormalizeScope(scope);
            return scope.Kind == UIPoolScopeKind.Global ||
                   (_poolScopes.TryGetValue(scope.Id, out var state) && !state.Ended);
        }

        private void EnsureScopeAlive(UIPoolScope scope)
        {
            if (!scope.IsValid)
            {
                throw new ArgumentException("A valid UI pool scope is required.", nameof(scope));
            }

            if (!IsScopeAlive(scope))
            {
                throw new InvalidOperationException(
                    $"UI pool scope has ended and cannot accept work: {scope}.");
            }
        }

        private CancellationToken GetScopeServiceToken(UIPoolScope scope)
        {
            scope = NormalizeScope(scope);
            if (scope.Kind == UIPoolScopeKind.Global)
            {
                return _serviceLifetimeCancellation.Token;
            }

            EnsureScopeAlive(scope);
            return _poolScopes[scope.Id].Token;
        }

        private void OnApplicationLowMemory()
        {
            if (!_initialized || _shuttingDown)
            {
                return;
            }

            try
            {
                HandleLowMemory();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (!_initialized || !_sceneScopes.TryGetValue(scene.handle, out var scope))
            {
                return;
            }

            _sceneScopes.Remove(scene.handle);
            ReleaseScopeAsync(scope).Forget(Debug.LogException);
        }

        private static void AddFlattened(List<Exception> errors, Exception exception)
        {
            if (exception is AggregateException aggregate)
            {
                errors.AddRange(aggregate.Flatten().InnerExceptions);
            }
            else
            {
                errors.Add(exception);
            }
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

        private sealed class PoolScopeState : IDisposable
        {
            private readonly CancellationTokenSource _cancellation;

            public PoolScopeState(
                UIPoolScope scope,
                CancellationToken serviceLifetimeToken)
            {
                Scope = scope;
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    serviceLifetimeToken);
            }

            public UIPoolScope Scope { get; }
            public CancellationToken Token => _cancellation.Token;
            public bool Ended { get; private set; }

            public void End()
            {
                if (Ended)
                {
                    return;
                }

                Ended = true;
                if (!_cancellation.IsCancellationRequested)
                {
                    _cancellation.Cancel();
                }
            }

            public void Dispose()
            {
                try
                {
                    End();
                }
                finally
                {
                    _cancellation.Dispose();
                }
            }
        }
    }
}
