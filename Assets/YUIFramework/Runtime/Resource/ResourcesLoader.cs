using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 基于 Resources 的默认加载器。
    /// 后续可增加 AddressablesLoader 并在 Init 注入替换。
    /// </summary>
    public class ResourcesLoader : IResourceLoader
    {
        public async Task<GameObject> LoadPrefabAsync(string key)
        {
            var request = Resources.LoadAsync<GameObject>(key);
            while (!request.isDone)
            {
                await Task.Yield();
            }

            var prefab = request.asset as GameObject;
            if (prefab == null)
            {
                Debug.LogError(
                    $"[YUIFramework] ResourcesLoader.LoadPrefabAsync 加载失败: key=\"{key}\"。请确认使用 Resources 相对路径，例如 \"UI/Pages/MainMenuPage\"，且资源文件位于 \"Assets/Resources/UI/Pages/MainMenuPage.prefab\"。");
            }

            return prefab;
        }

        public void Release(string key, GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            Object.Destroy(instance);
        }
    }
}
