using System;

namespace YUIFramework
{
    /// <summary>
    /// 强类型资源键：package + location + assetType 三元组唯一标识一个可加载资源。
    /// 只有三者完全一致的请求才会被合并为同一次底层加载。
    /// </summary>
    public readonly struct UIResourceKey : IEquatable<UIResourceKey>
    {
        /// <summary>
        /// 构造资源键。<paramref name="packageName"/> 为空表示“使用资源服务的默认 UI package”，
        /// 由 <see cref="IUIResourceService"/> 在加载时解析为具体 package 名。
        /// </summary>
        public UIResourceKey(string location, Type assetType, string packageName = null)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Resource location must not be null or whitespace.", nameof(location));
            }

            if (assetType == null)
            {
                throw new ArgumentNullException(nameof(assetType));
            }

            if (!typeof(UnityEngine.Object).IsAssignableFrom(assetType))
            {
                throw new ArgumentException(
                    $"Resource asset type must derive from UnityEngine.Object, but was {assetType.FullName}.",
                    nameof(assetType));
            }

            Location = location;
            AssetType = assetType;
            PackageName = string.IsNullOrWhiteSpace(packageName) ? null : packageName;
        }

        /// <summary>所属 package 名；为 null 表示尚未解析的“默认 package”。</summary>
        public string PackageName { get; }

        /// <summary>资源在 package 内的定位地址。</summary>
        public string Location { get; }

        /// <summary>请求的资源类型。</summary>
        public Type AssetType { get; }

        /// <summary>是否已绑定到具体 package。</summary>
        public bool HasExplicitPackage => PackageName != null;

        /// <summary>该键是否已被正确构造（默认值结构体为 false）。</summary>
        public bool IsValid => Location != null && AssetType != null;

        /// <summary>按资源类型构造键。</summary>
        public static UIResourceKey Of<T>(string location, string packageName = null)
            where T : UnityEngine.Object
        {
            return new UIResourceKey(location, typeof(T), packageName);
        }

        /// <summary>返回绑定到指定 package 的等价键。</summary>
        public UIResourceKey WithPackage(string packageName)
        {
            return new UIResourceKey(Location, AssetType, packageName);
        }

        public bool Equals(UIResourceKey other)
        {
            return string.Equals(PackageName, other.PackageName, StringComparison.Ordinal)
                   && string.Equals(Location, other.Location, StringComparison.Ordinal)
                   && AssetType == other.AssetType;
        }

        public override bool Equals(object obj)
        {
            return obj is UIResourceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + (PackageName == null ? 0 : StringComparer.Ordinal.GetHashCode(PackageName));
                hash = (hash * 31) + (Location == null ? 0 : StringComparer.Ordinal.GetHashCode(Location));
                hash = (hash * 31) + (AssetType == null ? 0 : AssetType.GetHashCode());
                return hash;
            }
        }

        public static bool operator ==(UIResourceKey left, UIResourceKey right) => left.Equals(right);

        public static bool operator !=(UIResourceKey left, UIResourceKey right) => !left.Equals(right);

        public override string ToString()
        {
            if (!IsValid)
            {
                return "<invalid UIResourceKey>";
            }

            var package = PackageName ?? "<default>";
            return $"{package}:{Location}<{AssetType.Name}>";
        }
    }
}
