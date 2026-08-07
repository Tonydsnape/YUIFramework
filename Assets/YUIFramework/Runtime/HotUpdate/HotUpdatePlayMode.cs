namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 热更运行模式（概念性三态，映射到 YooAsset 的初始化参数）。
    /// </summary>
    public enum HotUpdatePlayMode
    {
        /// <summary>编辑器模拟：资源直接来自工程，不读 StreamingAssets/CDN，仅用于开发。</summary>
        EditorSimulate,

        /// <summary>离线：只读安装包内置（StreamingAssets）资源，不联网。</summary>
        Offline,

        /// <summary>联机：包内命中则用包内，否则从 CDN 下载到缓存。</summary>
        Host,
    }
}
