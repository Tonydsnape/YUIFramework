using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace YUIFramework
{
    public interface IUIService : IUIRegistry
    {
        bool IsInitialized { get; }
        IUINavigator Navigator { get; }
        IUIMessageBus MessageBus { get; }
        UIRootRuntime RootRuntime { get; }
        UIInputLockService InputLocks { get; }
        UIPoolDiagnosticsSnapshot PoolDiagnostics { get; }
        UITransitionRunner Transitions { get; }

        void Initialize(IResourceLoader loader, IUIObjectPool pool = null);
        void Initialize(
            IResourceLoader loader,
            UIRootRuntime rootRuntime,
            IUIObjectPool pool = null);

        UniTask InitializeAsync(
            IResourceLoader loader,
            IUIObjectPool pool = null,
            CancellationToken cancellationToken = default);

        UniTask InitializeAsync(
            IResourceLoader loader,
            UIRootRuntime rootRuntime,
            IUIObjectPool pool = null,
            CancellationToken cancellationToken = default);

        UniTask<T> OpenAsync<T>(
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask<T> OpenInScopeAsync<T>(
            UIPoolScope scope,
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask<int> PrewarmAsync<T>(
            CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask<int> PrewarmAsync<T>(
            UIPoolScope scope,
            CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask<int> PrewarmRegisteredAsync(
            UIPoolScope scope = default,
            CancellationToken cancellationToken = default);

        UniTask<UIHandle<T>> OpenHandleAsync<T>(
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask CloseAsync<T>(CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask CloseAsync(BaseContext context, CancellationToken cancellationToken = default);
        bool RequestTransitionInterruption<T>(UITransitionInterruption interruption)
            where T : BaseContext;
        bool RequestTransitionInterruption(
            BaseContext context,
            UITransitionInterruption interruption);
        void RefreshTransitionBaseline(BaseContext context);
        T Get<T>() where T : BaseContext;
        bool IsOpen<T>() where T : BaseContext;
        void ClearPool<T>() where T : BaseContext;
        UniTask ClearPoolAsync<T>(CancellationToken cancellationToken = default)
            where T : BaseContext;
        void ClearAllPools();
        UIPoolScope CreateModuleScope(string moduleName);
        UIPoolScope GetSceneScope(Scene scene);
        UniTask ReleaseScopeAsync(
            UIPoolScope scope,
            CancellationToken cancellationToken = default);
        int EvictExpiredPoolEntries();
        int HandleLowMemory();
        UniTask ShutdownAsync(CancellationToken cancellationToken = default);
    }
}
