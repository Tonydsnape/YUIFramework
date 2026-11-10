using System;
using System.Threading;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YUIFramework.Configuration
{
    public sealed class EditorJsonConfigSource : IConfigSource
    {
        private readonly string _root;
        public EditorJsonConfigSource(string root = null)
        { _root = (root ?? FindSampleRoot()).TrimEnd('/') + "/"; }
        public static string FindSampleRoot()
        {
            const string original = "Assets/YUIFramework/ConfigData/Editor/json/";
            if (AssetDatabase.LoadAssetAtPath<TextAsset>(original + "UISettings.json")) return original;
            var matches = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
                path.EndsWith("/ConfigData/Editor/json/UISettings.json", StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new ConfigDataException("Import exactly one Complete UI flow sample or pass an explicit Editor JSON root.");
            return matches[0].Substring(0, matches[0].Length - "UISettings.json".Length);
        }
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
