namespace YUIFramework
{
    /// <summary>
    /// 底层资源句柄抽象（对应 YooAsset 的 AssetHandle）。
    /// 由 <see cref="IUIResourceProvider"/> 创建，生命周期由 <see cref="IUIResourceService"/> 统一管理。
    /// 框架保证对同一句柄最多调用一次有效的 <see cref="Release"/>，实现方仍应保证 Release 幂等。
    /// </summary>
    public interface IUINativeAssetHandle
    {
        /// <summary>底层资源对象；加载失败或已释放时为 null。</summary>
        UnityEngine.Object Asset { get; }

        /// <summary>句柄是否仍然有效。</summary>
        bool IsValid { get; }

        /// <summary>释放底层句柄（必须幂等）。</summary>
        void Release();
    }
}
