using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public interface IResourceLoader
    {
        Task<GameObject> LoadPrefabAsync(string key);
        void Release(string key, GameObject instance);
    }
}
