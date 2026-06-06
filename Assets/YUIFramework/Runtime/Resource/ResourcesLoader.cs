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

            return request.asset as GameObject;
        }

        public void Release(string key, GameObject instance)
        {
            if (instance != null)
            {
                Object.Destroy(instance);
            }
        }
    }
}
