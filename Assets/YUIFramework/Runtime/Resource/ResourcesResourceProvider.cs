using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 基于 <see cref="Resources"/> 的资源 provider。
    /// 仅用于测试与最小化 Editor 兼容场景，但遵循与 YooAsset 完全相同的租约与引用计数模型。
    /// 生产环境请使用 YooAsset provider。
    /// </summary>
    public sealed class ResourcesResourceProvider : IUIResourceProvider
    {
        /// <summary>该 provider 的默认 package 名。</summary>
        public const string DefaultPackageName = "Resources";

        public ResourcesResourceProvider(string packageName = DefaultPackageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                throw new ArgumentException("Package name must not be null or whitespace.", nameof(packageName));
            }

            PackageName = packageName;
        }

        public string PackageName { get; }

        public async UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
        {
            var normalizedKey = ResourcePathUtility.NormalizeResourcesKey(key.Location);
            if (ResourcePathUtility.IsInvalidKey(normalizedKey))
            {
                throw new ResourceLoadException(
                    key.Location,
                    nameof(ResourcesResourceProvider),
                    ResourcePathUtility.BuildResourcesPathHint(key.Location));
            }

            var request = Resources.LoadAsync(normalizedKey, key.AssetType);
            await request;

            var asset = request.asset;
            if (asset == null)
            {
                throw new ResourceLoadException(
                    normalizedKey,
                    nameof(ResourcesResourceProvider),
                    $"Resources.LoadAsync returned null for {key}.");
            }

            return new ResourcesNativeHandle(asset);
        }

        public UniTask ShutdownAsync()
        {
            // Resources 由 Unity 自行管理，无需额外释放；卸载交由 Resources.UnloadUnusedAssets。
            return UniTask.CompletedTask;
        }

        private sealed class ResourcesNativeHandle : IUINativeAssetHandle
        {
            private UnityEngine.Object _asset;

            public ResourcesNativeHandle(UnityEngine.Object asset)
            {
                _asset = asset;
            }

            public UnityEngine.Object Asset => _asset;

            public bool IsValid => _asset != null;

            public void Release()
            {
                // Resources 加载的资源不做单体卸载（Unity 不支持按对象卸载 Resources 资源），
                // 这里只放弃引用，实际回收由 Resources.UnloadUnusedAssets 触发。
                _asset = null;
            }
        }
    }
}
