using System;

namespace YUIFramework
{
    /// <summary>
    /// 资源租约：表示对某个已加载资源的一份所有权。
    /// 每个调用方各自持有独立租约，释放自己的租约不会影响其他持有者。
    /// <see cref="Release"/> 与 <see cref="IDisposable.Dispose"/> 均为幂等操作。
    /// </summary>
    public interface IUIAssetLease : IDisposable
    {
        /// <summary>该租约对应的资源键（package 已解析）。</summary>
        UIResourceKey Key { get; }

        /// <summary>所属 package 名。</summary>
        string PackageName { get; }

        /// <summary>资源定位地址。</summary>
        string Location { get; }

        /// <summary>资源类型。</summary>
        Type AssetType { get; }

        /// <summary>底层资源对象；租约释放后为 null。</summary>
        UnityEngine.Object Asset { get; }

        /// <summary>租约是否已释放。</summary>
        bool IsReleased { get; }

        /// <summary>释放该租约（幂等）。</summary>
        void Release();
    }

    /// <summary>强类型资源租约。</summary>
    public interface IUIAssetLease<out T> : IUIAssetLease
        where T : UnityEngine.Object
    {
        /// <summary>强类型资源对象；租约释放后为 null。</summary>
        new T Asset { get; }
    }
}
