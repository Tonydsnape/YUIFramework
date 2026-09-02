using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 基于 Resources 的默认加载器。
    /// 后续可增加 AddressablesLoader 并在 Init 注入替换。
    /// </summary>
    public sealed class ResourcesLoader : IResourceLoader
    {
        public async UniTask<GameObject> LoadPrefabAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedKey = ResourcePathUtility.NormalizeResourcesKey(key);
            if (ResourcePathUtility.IsInvalidKey(normalizedKey))
            {
                var invalidMessage = $"PrefabKey 非法。{ResourcePathUtility.BuildResourcesPathHint(key)}";
                Debug.LogError($"[ResourcesLoader] {invalidMessage}");
                throw new ResourceLoadException(key, nameof(ResourcesLoader), invalidMessage);
            }

            if (!string.Equals(key, normalizedKey, System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[ResourcesLoader] 检测到非规范 PrefabKey，已自动修正：original=\"{key}\", normalized=\"{normalizedKey}\"。{ResourcePathUtility.BuildResourcesPathHint(key)}");
            }

            var request = Resources.LoadAsync<GameObject>(normalizedKey);
            await request.ToUniTask(cancellationToken: cancellationToken);

            var prefab = request.asset as GameObject;
            if (prefab == null)
            {
                var loadError =
                    $"无法通过 Resources.LoadAsync 加载 UI Prefab。originalKey=\"{key}\"，normalizedKey=\"{normalizedKey}\"。示例：PrefabKey=\"UI/Pages/MainMenuPage\"，文件应位于 Assets/Resources/UI/Pages/MainMenuPage.prefab。";
                Debug.LogError($"[ResourcesLoader] {loadError}");
                throw new ResourceLoadException(normalizedKey, nameof(ResourcesLoader), loadError);
            }

            return prefab;
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
