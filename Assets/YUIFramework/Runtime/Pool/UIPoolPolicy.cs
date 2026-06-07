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

        public UIPoolPolicy(bool cacheOnClose, int maxPoolSize)
        {
            CacheOnClose = cacheOnClose;
            MaxPoolSize = Math.Max(0, maxPoolSize);
        }

        public static UIPoolPolicy FromConfig(UIConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            return new UIPoolPolicy(config.CacheOnClose, config.MaxPoolSize);
        }
    }
}
