using System;

namespace YUIFramework
{
    /// <summary>
    /// UI 资源加载异常，包含 loader 类型与 key 方便在 Console 定位问题。
    /// </summary>
    public sealed class ResourceLoadException : Exception
    {
        public string Key { get; }
        public string LoaderType { get; }

        public ResourceLoadException(string key, string loaderType, string message)
            : base(BuildMessage(key, loaderType, message))
        {
            Key = key;
            LoaderType = loaderType;
        }

        public ResourceLoadException(string key, string loaderType, string message, Exception innerException)
            : base(BuildMessage(key, loaderType, message), innerException)
        {
            Key = key;
            LoaderType = loaderType;
        }

        private static string BuildMessage(string key, string loaderType, string message)
        {
            var safeLoader = string.IsNullOrWhiteSpace(loaderType) ? "UnknownLoader" : loaderType;
            var safeKey = string.IsNullOrWhiteSpace(key) ? "<empty>" : key;
            return $"[{safeLoader}] 资源加载失败，key=\"{safeKey}\"。{message}";
        }
    }
}
