using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 基于 YooAsset 的 <see cref="IResourceLoader"/> 实现，作为热更层与 UI 框架的桥接。
    /// 使用方式：UIManager.Init(new YooAssetLoader())。
    ///
    /// 行为：
    /// - YooAsset 就绪且 location 被清单收录时，走 YooAsset 加载（可热更）。
    /// - 否则回退 Resources（key 规范化后 Resources.LoadAsync），保证示例在未打包时仍可运行。
    /// - 按 key 引用计数管理 YooAsset 句柄，最后一个实例释放时归还句柄。
    /// </summary>
    public sealed class YooAssetLoader : IResourceLoader
    {
        private sealed class LoadEntry
        {
            public AssetHandle Handle;   // YooAsset 句柄；回退 Resources 时为 null
            public int RefCount;
            public bool FromYoo;
        }

        private readonly Dictionary<string, LoadEntry> _entries = new Dictionary<string, LoadEntry>(StringComparer.Ordinal);

        public async Task<GameObject> LoadPrefabAsync(string key)
        {
            if (ResourcePathUtility.IsInvalidKey(key))
            {
                throw new ResourceLoadException(key, nameof(YooAssetLoader), "PrefabKey 不能为空。");
            }

            // 复用已加载条目（引用计数 +1）。
            if (_entries.TryGetValue(key, out var existing) && existing.RefCount > 0)
            {
                var cachedPrefab = existing.FromYoo
                    ? existing.Handle?.AssetObject as GameObject
                    : LoadFromResources(key);
                if (cachedPrefab != null)
                {
                    existing.RefCount++;
                    return cachedPrefab;
                }

                _entries.Remove(key);
            }

            // 优先 YooAsset。
            var handle = await HotUpdateManager.Instance.LoadAssetAsync<GameObject>(key);
            if (handle != null && handle.AssetObject is GameObject yooPrefab)
            {
                _entries[key] = new LoadEntry { Handle = handle, RefCount = 1, FromYoo = true };
                return yooPrefab;
            }

            // 回退 Resources。
            var prefab = await LoadFromResourcesAsync(key);
            if (prefab == null)
            {
                var message =
                    $"YooAsset 与 Resources 均无法加载 UI Prefab。key=\"{key}\"。" +
                    "请确认资源已被 YooAsset 收集器收录并构建，或已放入 Assets/Resources 下。";
                Debug.LogError($"[YooAssetLoader] {message}");
                throw new ResourceLoadException(key, nameof(YooAssetLoader), message);
            }

            _entries[key] = new LoadEntry { Handle = null, RefCount = 1, FromYoo = false };
            return prefab;
        }

        public void Release(string key, GameObject instance)
        {
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
            }

            if (ResourcePathUtility.IsInvalidKey(key) || !_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            entry.RefCount--;
            if (entry.RefCount > 0)
            {
                return;
            }

            if (entry.FromYoo && entry.Handle != null)
            {
                entry.Handle.Release();
            }

            _entries.Remove(key);
        }

        private static async Task<GameObject> LoadFromResourcesAsync(string key)
        {
            var normalizedKey = ResourcePathUtility.NormalizeResourcesKey(key);
            if (ResourcePathUtility.IsInvalidKey(normalizedKey))
            {
                return null;
            }

            var request = Resources.LoadAsync<GameObject>(normalizedKey);
            while (!request.isDone)
            {
                await Task.Yield();
            }

            return request.asset as GameObject;
        }

        private static GameObject LoadFromResources(string key)
        {
            var normalizedKey = ResourcePathUtility.NormalizeResourcesKey(key);
            return ResourcePathUtility.IsInvalidKey(normalizedKey)
                ? null
                : Resources.Load<GameObject>(normalizedKey);
        }
    }
}
