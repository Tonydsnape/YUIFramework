using System;

namespace YUIFramework
{
    /// <summary>
    /// UI 注册配置（Config 驱动）。
    /// </summary>
    [Serializable]
    public class UIConfig
    {
        /// <summary>唯一 ID，例如 HelloPage。</summary>
        public string Id;

        /// <summary>资源地址，传入 IResourceLoader。</summary>
        public string PrefabKey;

        /// <summary>目标层级。</summary>
        public UILayer Layer;

        /// <summary>
        /// 关闭后是否缓存（P1 支持基础隐藏缓存）。
        /// </summary>
        public bool CacheOnClose;

        /// <summary>
        /// 同一 UI 类型最大缓存数量。小于等于 0 视为不缓存。
        /// </summary>
        public int MaxPoolSize = 1;

        /// <summary>
        /// 预加载数量（P4 先保留配置字段，后续可扩展预热逻辑）。
        /// </summary>
        public int PreloadCount;

        /// <summary>
        /// 是否全屏（后续导航栈遮挡策略使用）。
        /// </summary>
        public bool FullScreen;
    }
}
