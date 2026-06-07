#if YUIFRAMEWORK_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace YUIFramework
{
    /// <summary>
    /// 可选 Addressables 加载器（仅在安装 Addressables 包后编译）。
    /// </summary>
    public sealed class AddressablesLoader : IResourceLoader
    {
        private sealed class AddressablePrefabHandle
        {
            public AsyncOperationHandle<GameObject> Handle;
            public int RefCount;
        }

        private readonly Dictionary<string, AddressablePrefabHandle> _handles =
            new Dictionary<string, AddressablePrefabHandle>();

        public async Task<GameObject> LoadPrefabAsync(string key)
        {
            var loaderType = nameof(AddressablesLoader);
            if (ResourcePathUtility.IsInvalidKey(key))
            {
                throw new ResourceLoadException(key, loaderType, "Addressables key 不能为空。");
            }

            if (_handles.TryGetValue(key, out var tracked))
            {
                tracked.RefCount++;
                try
                {
                    await tracked.Handle.Task;
                    if (tracked.Handle.Status == AsyncOperationStatus.Succeeded && tracked.Handle.Result != null)
                    {
                        return tracked.Handle.Result;
                    }
                }
                catch (Exception ex)
                {
                    throw new ResourceLoadException(key, loaderType, "Addressables 加载过程中发生异常。", ex);
                }

                tracked.RefCount--;
                if (tracked.RefCount <= 0)
                {
                    if (tracked.Handle.IsValid())
                    {
                        Addressables.Release(tracked.Handle);
                    }

                    _handles.Remove(key);
                }

                throw new ResourceLoadException(key, loaderType, "Addressables 返回失败状态，未获取到有效 Prefab。");
            }

            var handle = Addressables.LoadAssetAsync<GameObject>(key);
            tracked = new AddressablePrefabHandle
            {
                Handle = handle,
                RefCount = 1
            };
            _handles[key] = tracked;

            try
            {
                await handle.Task;
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    throw new ResourceLoadException(key, loaderType, "Addressables 返回失败状态，未获取到有效 Prefab。");
                }

                return handle.Result;
            }
            catch (ResourceLoadException)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                _handles.Remove(key);
                throw;
            }
            catch (Exception ex)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                _handles.Remove(key);
                throw new ResourceLoadException(key, loaderType, "Addressables 加载过程中发生异常。", ex);
            }
        }

        public void Release(string key, GameObject instance)
        {
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
            }

            if (ResourcePathUtility.IsInvalidKey(key))
            {
                return;
            }

            if (!_handles.TryGetValue(key, out var tracked))
            {
                return;
            }

            tracked.RefCount--;
            if (tracked.RefCount > 0)
            {
                return;
            }

            if (tracked.Handle.IsValid())
            {
                Addressables.Release(tracked.Handle);
            }

            _handles.Remove(key);
        }
    }
}
#endif
