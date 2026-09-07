using System;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace YUIFramework.Bootstrap.YooAsset
{
    /// <summary>
    /// 基于 YooAsset 的资源 provider，是 Y2.0 唯一的生产资源后端。
    /// </summary>
    /// <remarks>
    /// 该适配器只依赖注入进来的 <see cref="ResourcePackage"/>，
    /// 不依赖 <see cref="HotUpdateManager"/> 单例，因此可以被独立构造与测试，
    /// 也支持在同一个进程中注册多个 package。
    /// </remarks>
    public sealed class YooAssetResourceProvider : IUIResourceProvider
    {
        private readonly ResourcePackage _package;

        public YooAssetResourceProvider(ResourcePackage package, string packageName = null)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            PackageName = string.IsNullOrWhiteSpace(packageName) ? package.PackageName : packageName;

            if (string.IsNullOrWhiteSpace(PackageName))
            {
                throw new ArgumentException(
                    "Resource package name must not be null or whitespace.",
                    nameof(packageName));
            }
        }

        public string PackageName { get; }

        public async UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
        {
            var assetInfo = _package.GetAssetInfo(key.Location, key.AssetType);
            if (assetInfo == null || !assetInfo.IsValid)
            {
                throw new ResourceLoadException(
                    key.Location,
                    nameof(YooAssetResourceProvider),
                    $"Location is not present in the manifest of package \"{PackageName}\" for {key}.");
            }

            var handle = _package.LoadAssetAsync(assetInfo);

            // 底层加载不可取消：必须等待其自然完成，取消语义由 UIResourceService 在等待层处理。
            await handle;

            if (handle.Status != EOperationStatus.Succeeded)
            {
                var error = handle.Error;
                handle.Release();
                throw new ResourceLoadException(
                    key.Location,
                    nameof(YooAssetResourceProvider),
                    $"YooAsset failed to load {key}: {error}");
            }

            return new YooAssetNativeHandle(handle);
        }

        public UniTask ShutdownAsync()
        {
            // ResourcePackage 的生命周期由 BootstrapRunner/YooAssetBootstrapBackend 负责，
            // provider 只负责它所发放的句柄，这些句柄已由 UIResourceService 统一释放。
            return UniTask.CompletedTask;
        }

        private sealed class YooAssetNativeHandle : IUINativeAssetHandle
        {
            private AssetHandle _handle;

            public YooAssetNativeHandle(AssetHandle handle)
            {
                _handle = handle;
            }

            public UnityEngine.Object Asset => _handle != null && _handle.IsValid ? _handle.AssetObject : null;

            public bool IsValid => _handle != null && _handle.IsValid;

            public void Release()
            {
                // 幂等：只对同一个底层句柄释放一次。
                var handle = _handle;
                _handle = null;
                if (handle != null && handle.IsValid)
                {
                    handle.Release();
                }
            }
        }
    }
}
