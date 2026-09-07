using Cysharp.Threading.Tasks;

namespace YUIFramework
{
    /// <summary>
    /// 资源后端提供者：一个 provider 对应一个 package。
    /// YooAsset 为唯一生产实现；测试可以提供不依赖网络的伪实现。
    /// </summary>
    /// <remarks>
    /// <see cref="LoadAssetAsync"/> 刻意不接受 <see cref="System.Threading.CancellationToken"/>：
    /// 底层（YooAsset）的加载一旦发起就无法取消，而同一次加载会被多个调用方共享，
    /// 单个调用方的取消绝不能中断其他调用方。取消语义完全由
    /// <see cref="IUIResourceService"/> 在等待层实现；当所有等待者都取消时，
    /// 服务会在底层加载完成后立即释放该句柄。
    /// </remarks>
    public interface IUIResourceProvider
    {
        /// <summary>该 provider 提供的 package 名称。</summary>
        string PackageName { get; }

        /// <summary>
        /// 发起一次底层加载。必须最终完成（成功或失败），不可取消。
        /// 加载失败时应返回 null 或抛出异常。
        /// </summary>
        UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key);

        /// <summary>关闭该 provider，释放其自身持有的资源。</summary>
        UniTask ShutdownAsync();
    }
}
