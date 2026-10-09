using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework.Configuration
{
    public sealed class ResourceConfigSource : IConfigSource
    {
        private readonly IUIResourceService _resources;
        private readonly string _package;
        private readonly string _prefix;
        private readonly string _suffix;
        public ResourceConfigSource(IUIResourceService resources, string package = null,
            string prefix = "", string suffix = "")
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _package = package;
            _prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            _suffix = suffix ?? throw new ArgumentNullException(nameof(suffix));
        }
        public async UniTask<IConfigAsset> LoadAsync(string table, CancellationToken cancellationToken)
        {
            var lease = await _resources.LoadAssetAsync<TextAsset>(
                UIResourceKey.Of<TextAsset>(_prefix + table + _suffix, _package), cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lease.Asset == null) throw new ConfigDataException($"Missing TextAsset for {table}.");
                var result = new Asset(lease);
                lease = null;
                return result;
            }
            finally { lease?.Dispose(); }
        }
        private sealed class Asset : IConfigAsset
        {
            private IUIAssetLease<TextAsset> _lease;
            internal Asset(IUIAssetLease<TextAsset> lease) { _lease = lease; }
            public byte[] Bytes => _lease != null ? _lease.Asset.bytes : throw new ObjectDisposedException(nameof(Asset));
            public void Dispose() { var lease = _lease; _lease = null; lease?.Dispose(); }
        }
    }
}
