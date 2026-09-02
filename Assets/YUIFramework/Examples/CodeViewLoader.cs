using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 示例专用：用代码构建可实例化的 UIView 占位体。
    /// </summary>
    public class CodeViewLoader : IResourceLoader
    {
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

        public UniTask<GameObject> LoadPrefabAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_prefabs.TryGetValue(key, out var prefab) || prefab == null)
            {
                prefab = BuildCodePrefab(key);
                _prefabs[key] = prefab;
            }

            return UniTask.FromResult(prefab);
        }

        public void Release(string key, GameObject instance)
        {
            if (instance != null)
            {
                Object.Destroy(instance);
            }
        }

        private static GameObject BuildCodePrefab(string key)
        {
            var go = new GameObject($"CodePrefab_{key}");
            go.AddComponent<RectTransform>();
            go.AddComponent<UIView>();
            go.SetActive(false);
            go.hideFlags = HideFlags.DontSave;
            return go;
        }
    }
}
