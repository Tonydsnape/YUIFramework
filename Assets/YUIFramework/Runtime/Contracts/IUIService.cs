using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    public interface IUIService : IUIRegistry
    {
        bool IsInitialized { get; }
        IUINavigator Navigator { get; }
        IUIMessageBus MessageBus { get; }
        UIRootRuntime RootRuntime { get; }
        UIInputLockService InputLocks { get; }

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

        UniTask<UIHandle<T>> OpenHandleAsync<T>(
            object args = null,
            CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask CloseAsync<T>(CancellationToken cancellationToken = default)
            where T : BaseContext;

        UniTask CloseAsync(BaseContext context, CancellationToken cancellationToken = default);
        T Get<T>() where T : BaseContext;
        bool IsOpen<T>() where T : BaseContext;
        void ClearPool<T>() where T : BaseContext;
        void ClearAllPools();
        UniTask ShutdownAsync(CancellationToken cancellationToken = default);
    }
}
