using System;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 实例租约：表示对某个由资源实例化出来的 <see cref="GameObject"/> 的所有权。
    /// 实例租约与资源租约相互独立：释放实例只销毁该实例，并归还其内部持有的那一份资源租约，
    /// 不会影响同一资源上其他调用方的租约。<see cref="Release"/> 与 Dispose 均为幂等操作。
    /// </summary>
    public interface IUIInstanceLease : IDisposable
    {
        /// <summary>实例来源的资源键（package 已解析）。</summary>
        UIResourceKey Key { get; }

        /// <summary>实例对象；租约释放后为 null。</summary>
        GameObject Instance { get; }

        /// <summary>该实例所依赖的资源租约；租约释放后为 null。</summary>
        IUIAssetLease AssetLease { get; }

        /// <summary>租约是否已释放。</summary>
        bool IsReleased { get; }

        /// <summary>释放该实例租约（幂等）：销毁实例并归还资源租约。</summary>
        void Release();
    }
}
