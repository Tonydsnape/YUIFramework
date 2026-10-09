using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YUIFramework.Configuration
{
    public sealed class EditorJsonConfigSource : IConfigSource
    {
        private readonly string _root;
        public EditorJsonConfigSource(string root = "Assets/YUIFramework/ConfigData/Editor/json/")
        { _root = root.TrimEnd('/') + "/"; }
        public UniTask<IConfigAsset> LoadAsync(string table, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(_root + table + ".json");
            if (asset == null) throw new ConfigDataException($"Editor config missing: {_root}{table}.json");
            return UniTask.FromResult<IConfigAsset>(new Asset(asset.bytes));
        }
        private sealed class Asset : IConfigAsset
        {
            private byte[] _bytes;
            internal Asset(byte[] bytes) { _bytes = bytes; }
            public byte[] Bytes => _bytes ?? throw new ObjectDisposedException(nameof(Asset));
            public void Dispose() { _bytes = null; }
        }
    }
}
