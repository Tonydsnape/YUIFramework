using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 资源加载抽象接口，用于统一 UI 预制体的加载与释放。
    /// ResourcesLoader 的 key 示例：UI/Pages/MainMenuPage。
    /// AddressablesLoader 的 key 示例：Addressables Address（如 UI/Pages/MainMenuPage）。
    /// </summary>
    public interface IResourceLoader
    {
        /// <summary>
        /// 按逻辑资源地址加载 UI 预制体资源。
        /// </summary>
        Task<GameObject> LoadPrefabAsync(string key);

        /// <summary>
        /// 释放由 LoadPrefabAsync 加载并实例化后的对象。
        /// </summary>
        void Release(string key, GameObject instance);
    }
}
