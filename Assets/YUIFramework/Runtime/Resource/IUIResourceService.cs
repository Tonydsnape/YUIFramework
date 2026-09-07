using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 资源服务：统一负责资源的共享加载、引用计数、租约发放、取消、预加载与释放。
    /// </summary>
    /// <remarks>
    /// 所有权模型：
    /// <list type="bullet">
    /// <item>同一 key 的并发加载只会触发一次底层加载，每个调用方获得彼此独立的租约。</item>
    /// <item>单个调用方取消只取消它自己的等待，不影响其他等待者与底层加载。</item>
    /// <item>当所有等待者都取消时，由于底层加载不可取消，服务会在其完成后立即释放句柄。</item>
    /// <item>资源租约与实例租约分离：实例租约额外拥有一个 GameObject 实例。</item>
    /// <item>最后一份租约释放后条目会作为“无引用缓存”保留，可由
    /// <see cref="TrimUnused"/>/<see cref="HandleLowMemory"/>/<see cref="ShutdownAsync"/> 回收。</item>
    /// </list>
    /// </remarks>
    public interface IUIResourceService
    {
        /// <summary>package 注册表。</summary>
        IUIResourcePackageRegistry Packages { get; }

        /// <summary>服务是否已关闭。</summary>
        bool IsShutDown { get; }

        /// <summary>加载资源并获得一份强类型租约。</summary>
        UniTask<IUIAssetLease<T>> LoadAssetAsync<T>(
            UIResourceKey key,
            CancellationToken cancellationToken = default)
            where T : Object;

        /// <summary>按 location 加载资源并获得一份强类型租约。</summary>
        UniTask<IUIAssetLease<T>> LoadAssetAsync<T>(
            string location,
            string packageName = null,
            CancellationToken cancellationToken = default)
            where T : Object;

        /// <summary>加载资源并获得一份弱类型租约。</summary>
        UniTask<IUIAssetLease> LoadAssetAsync(
            UIResourceKey key,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 加载并实例化一个 GameObject，返回实例租约。
        /// 实例租约释放时销毁实例并归还其内部持有的资源租约。
        /// </summary>
        UniTask<IUIInstanceLease> InstantiateAsync(
            UIResourceKey key,
            Transform parent = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 预加载一组资源：加载完成后立即释放租约，使其成为无引用缓存条目，
        /// 后续加载可直接命中而不再触发底层加载。返回成功预加载的数量。
        /// </summary>
        UniTask<int> PreloadAsync(
            IEnumerable<UIResourceKey> keys,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量加载：部分失败不会影响已成功的项，成功项的所有权通过
        /// <see cref="UIResourceBatchResult.Leases"/> 明确交给调用方。
        /// </summary>
        UniTask<UIResourceBatchResult> LoadBatchAsync(
            IEnumerable<UIResourceKey> keys,
            CancellationToken cancellationToken = default);

        /// <summary>显式释放一个无引用缓存条目；仍被引用时不做任何事并返回 false。</summary>
        bool ReleaseUnused(UIResourceKey key);

        /// <summary>释放全部无引用缓存条目，返回释放数量。</summary>
        int TrimUnused();

        /// <summary>低内存回调：只清理无引用缓存，不影响仍被持有的资源。</summary>
        int HandleLowMemory();

        /// <summary>获取诊断快照（泄漏检查、引用计数观察）。</summary>
        UIResourceDiagnosticsSnapshot GetDiagnostics();

        /// <summary>关闭服务：等待进行中的加载完成、释放全部句柄、关闭所有 provider。</summary>
        UniTask ShutdownAsync();
    }
}
