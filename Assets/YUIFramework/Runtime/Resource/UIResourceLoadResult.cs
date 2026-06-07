using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 资源加载结果，预留给后续诊断与扩展使用。
    /// </summary>
    public readonly struct UIResourceLoadResult
    {
        public string Key { get; }
        public GameObject Prefab { get; }
        public bool Success => Prefab != null;
        public string Error { get; }

        public UIResourceLoadResult(string key, GameObject prefab, string error = null)
        {
            Key = key;
            Prefab = prefab;
            Error = error;
        }
    }
}
