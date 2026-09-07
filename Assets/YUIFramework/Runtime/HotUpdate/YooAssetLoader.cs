using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap.YooAsset;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Initialize UIManager with YooAssetBootstrapComposition.ResourceService.")]
    public sealed class YooAssetLoader : IResourceLoader
    {
        private readonly IUIResourceService _resourceService;
        private readonly Dictionary<string, Queue<IUIAssetLease<GameObject>>> _leases =
            new Dictionary<string, Queue<IUIAssetLease<GameObject>>>(StringComparer.Ordinal);

        public YooAssetLoader(IUIResourceService resourceService = null)
        {
            _resourceService = resourceService;
        }

        public async UniTask<GameObject> LoadPrefabAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Resource key must not be empty.", nameof(key));
            }

            var service = _resourceService ?? LegacyBootstrapRuntime.Current.ResourceService;
            if (service == null)
            {
                throw new InvalidOperationException(
                    "YooAsset bootstrap must complete before using the compatibility loader.");
            }

            var lease = await service.LoadAssetAsync<GameObject>(key, cancellationToken: cancellationToken);
            if (!_leases.TryGetValue(key, out var queue))
            {
                queue = new Queue<IUIAssetLease<GameObject>>();
                _leases.Add(key, queue);
            }

            queue.Enqueue(lease);
            return lease.Asset;
        }

        public void Release(string key, GameObject instance)
        {
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
            }

            if (string.IsNullOrWhiteSpace(key) ||
                !_leases.TryGetValue(key, out var queue) ||
                queue.Count == 0)
            {
                return;
            }

            queue.Dequeue().Release();
            if (queue.Count == 0)
            {
                _leases.Remove(key);
            }
        }
    }
}
