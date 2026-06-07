using System;

namespace YUIFramework
{
    /// <summary>
    /// 资源 key 诊断与规范化工具。
    /// </summary>
    public static class ResourcePathUtility
    {
        private const string ResourcesPrefix = "Assets/Resources/";

        public static bool IsInvalidKey(string key)
        {
            return string.IsNullOrWhiteSpace(key);
        }

        public static bool LooksLikeResourcesAssetPath(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var normalized = key.Replace('\\', '/').TrimStart('/');
            return normalized.StartsWith(ResourcesPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool ContainsPrefabExtension(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return key.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string NormalizeResourcesKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var normalized = key.Replace('\\', '/').Trim();

            while (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(1);
            }

            if (normalized.StartsWith(ResourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(ResourcesPrefix.Length);
            }

            if (normalized.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - ".prefab".Length);
            }

            return normalized;
        }

        public static string BuildResourcesPathHint(string key)
        {
            var normalizedKey = NormalizeResourcesKey(key);
            if (string.IsNullOrEmpty(normalizedKey))
            {
                normalizedKey = "UI/Pages/MainMenuPage";
            }

            return
                $"PrefabKey 应写为 \"{normalizedKey}\"，资源文件示例：Assets/Resources/{normalizedKey}.prefab。";
        }
    }
}
