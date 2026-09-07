using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 资源加载抽象接口，用于统一 UI 预制体的加载与释放（Y1 兼容契约）。
    /// key 为逻辑资源地址（resource location），示例：UI/Pages/MainMenuPage。
    /// 生产环境请改用 <see cref="IUIResourceService"/> 与 <c>YooAssetResourceProvider</c>。
    /// </summary>
    public interface IResourceLoader
    {
        /// <summary>
        /// 按逻辑资源地址加载 UI 预制体资源。
        /// </summary>
        UniTask<GameObject> LoadPrefabAsync(
            string key,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 释放由 LoadPrefabAsync 加载并实例化后的对象。
        /// </summary>
        void Release(string key, GameObject instance);
    }
}
