using System.Collections.Generic;

namespace YUIFramework
{
    /// <summary>
    /// package 注册表：把 package 名映射到具体的 <see cref="IUIResourceProvider"/>，
    /// 并明确指定默认 UI package。多 package 为可选能力。
    /// </summary>
    public interface IUIResourcePackageRegistry
    {
        /// <summary>默认 UI package 名；未注册任何 package 时为 null。</summary>
        string DefaultPackageName { get; }

        /// <summary>已注册的 package 名集合。</summary>
        IReadOnlyList<string> PackageNames { get; }

        /// <summary>注册表是否已冻结；资源服务首次使用后不可再替换 provider。</summary>
        bool IsFrozen { get; }

        /// <summary>注册一个 provider。重复注册同名 package 会抛出异常。</summary>
        void Register(IUIResourceProvider provider, bool isDefault = false);

        /// <summary>注销指定 package。</summary>
        bool Unregister(string packageName);

        /// <summary>解析 package；传入 null 或空串表示默认 package。未注册时抛出异常。</summary>
        IUIResourceProvider Resolve(string packageName);

        /// <summary>尝试解析 package；传入 null 或空串表示默认 package。</summary>
        bool TryResolve(string packageName, out IUIResourceProvider provider);

        /// <summary>当前已注册的全部 provider 快照。</summary>
        IReadOnlyList<IUIResourceProvider> GetProviders();

        /// <summary>幂等冻结注册表，防止缓存建立后替换同名 provider。</summary>
        void Freeze();
    }
}
