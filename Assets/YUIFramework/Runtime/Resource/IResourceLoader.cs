using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 资源加载抽象接口，用于统一 UI 预制体的加载与释放。
    /// P1 默认实现为 ResourcesLoader，后续可替换为 AddressablesLoader。
    /// </summary>
    public interface IResourceLoader
    {
        Task<GameObject> LoadPrefabAsync(string key);
        void Release(string key, GameObject instance);
    }
}
