using System;

namespace YUIFramework
{
    /// <summary>
    /// UI 对象池策略。
    /// </summary>
    public sealed class UIPoolPolicy
    {
        public int MaxPoolSize { get; }
        public bool CacheOnClose { get; }
        public int Priority { get; }
        public double IdleTimeoutSeconds { get; }

        public UIPoolPolicy(
            bool cacheOnClose,
            int maxPoolSize,
            int priority = 0,
            double idleTimeoutSeconds = 0)
        {
            CacheOnClose = cacheOnClose;
            MaxPoolSize = Math.Max(0, maxPoolSize);
            Priority = priority;
            IdleTimeoutSeconds = Math.Max(0, idleTimeoutSeconds);
        }

        public static UIPoolPolicy FromConfig(UIConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return new UIPoolPolicy(
                config.CacheOnClose,
                config.MaxPoolSize,
                config.PoolPriority,
                config.PoolIdleTimeoutSeconds);
        }
    }
}
