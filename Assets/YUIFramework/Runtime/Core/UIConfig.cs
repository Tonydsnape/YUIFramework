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

        public UIKey Key => new UIKey(Id);

        /// <summary>资源地址，传入 IResourceLoader。</summary>
        public string PrefabKey;

        /// <summary>Null uses the resource service's default package.</summary>
        public string PrefabPackage;

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
        /// 实例预热数量。由 UIManager.PrewarmAsync 消费，且受 MaxPoolSize 限制。
        /// </summary>
        public int PreloadCount;

        /// <summary>闲置实例的淘汰优先级；数值越低越先淘汰。</summary>
        public int PoolPriority;

        /// <summary>闲置超时秒数；小于等于 0 表示不按时间过期。</summary>
        public float PoolIdleTimeoutSeconds;

        /// <summary>
        /// 是否全屏（后续导航栈遮挡策略使用）。
        /// </summary>
        public bool FullScreen;

        /// <summary>True 时使用层 profile 的模态策略；false 时使用 Modal。</summary>
        public bool UseLayerModalPolicy = true;

        /// <summary>UseLayerModalPolicy=false 时的 Context 模态覆盖。</summary>
        public bool Modal;

        /// <summary>
        /// 是否启用 UI 转场动画。默认关闭以保持兼容。
        /// </summary>
        public bool UseTransition;

        /// <summary>
        /// 转场类型（Fade / Scale / Slide 等）。
        /// </summary>
        public UITransitionType TransitionType = UITransitionType.None;

        /// <summary>
        /// 打开动画时长（秒）。
        /// </summary>
        public float ShowDuration = 0.2f;

        /// <summary>
        /// 关闭动画时长（秒）。
        /// </summary>
        public float HideDuration = 0.15f;

        /// <summary>
        /// 是否忽略 Time.timeScale，默认 true。
        /// </summary>
        public bool IgnoreTransitionTimeScale = true;

        /// <summary>
        /// Slide 位移距离。
        /// </summary>
        public float SlideDistance = 800f;

        /// <summary>
        /// Scale 起始缩放（> 0）。
        /// </summary>
        public float StartScale = 0.9f;

        /// <summary>Optional easing curve. Null or empty uses the framework ease-out curve.</summary>
        public UnityEngine.AnimationCurve TransitionCurve;

        /// <summary>Identifier registered through UITransitionRunner.RegisterCustom.</summary>
        public string CustomTransitionId;

        /// <summary>Refresh the stable transform/alpha baseline when a pooled view is rebound.</summary>
        public bool RefreshTransitionBaselineOnReuse = true;

        /// <summary>Pause framework-managed refresh work while this page is covered.</summary>
        public bool SuspendWhenCovered = true;

        /// <summary>
        /// 转换为运行时转场配置。
        /// </summary>
        public UITransitionOptions ToTransitionOptions()
        {
            var options = new UITransitionOptions
            {
                Type = TransitionType,
                ShowDuration = ShowDuration,
                HideDuration = HideDuration,
                IgnoreTimeScale = IgnoreTransitionTimeScale,
                SlideDistance = SlideDistance,
                StartScale = StartScale,
                Curve = TransitionCurve,
                CustomTransitionId = CustomTransitionId
            };
            options.Normalize();
            return options;
        }

        public UIConfig Copy()
        {
            var copy = (UIConfig)MemberwiseClone();
            if (TransitionCurve != null)
                copy.TransitionCurve = new UnityEngine.AnimationCurve(TransitionCurve.keys)
                { preWrapMode = TransitionCurve.preWrapMode, postWrapMode = TransitionCurve.postWrapMode };
            return copy;
        }
    }
}
