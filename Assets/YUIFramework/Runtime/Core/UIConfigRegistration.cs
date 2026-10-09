using System;

namespace YUIFramework
{
    public sealed class UIConfigRegistration
    {
        private readonly UIConfig _config;
        private UIConfigRegistration(Type type, UIConfig config)
        {
            ContextType = type;
            _config = (config ?? throw new ArgumentNullException(nameof(config))).Copy();
            Validate(_config);
        }
        public Type ContextType { get; }
        internal UIConfig CreateSnapshot() => _config.Copy();
        public static UIConfigRegistration For<T>(UIConfig config) where T : BaseContext =>
            new UIConfigRegistration(typeof(T), config);

        public static void Validate(UIConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.Id) || string.IsNullOrWhiteSpace(config.PrefabKey))
                throw new ArgumentException("UI Id and PrefabKey must not be empty.");
            if (config.PrefabPackage != null && string.IsNullOrWhiteSpace(config.PrefabPackage))
                throw new ArgumentException("PrefabPackage must be null or a package name.");
            if (!Enum.IsDefined(typeof(UILayer), config.Layer) ||
                !Enum.IsDefined(typeof(UITransitionType), config.TransitionType))
                throw new ArgumentException("Undefined UI layer/transition type.");
            if (config.PreloadCount < 0 || config.MaxPoolSize < 0 ||
                config.PreloadCount > config.MaxPoolSize ||
                (config.PreloadCount > 0 && !config.CacheOnClose))
                throw new ArgumentException("Invalid UI pool/prewarm limits.");
            Nonnegative(config.ShowDuration, nameof(config.ShowDuration));
            Nonnegative(config.HideDuration, nameof(config.HideDuration));
            Nonnegative(config.PoolIdleTimeoutSeconds, nameof(config.PoolIdleTimeoutSeconds));
            Nonnegative(config.SlideDistance, nameof(config.SlideDistance));
            if (float.IsNaN(config.StartScale) || float.IsInfinity(config.StartScale) || config.StartScale <= 0)
                throw new ArgumentException("StartScale must be finite and positive.");
            if (config.CustomTransitionId != null && string.IsNullOrWhiteSpace(config.CustomTransitionId))
                throw new ArgumentException("CustomTransitionId must be null or nonempty.");
            if (config.UseTransition && config.TransitionType == UITransitionType.Custom &&
                string.IsNullOrWhiteSpace(config.CustomTransitionId))
                throw new ArgumentException("A custom transition requires a registered identifier.");
        }
        private static void Nonnegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
