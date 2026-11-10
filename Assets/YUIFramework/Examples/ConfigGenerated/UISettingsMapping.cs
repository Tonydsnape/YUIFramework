namespace YUIFramework.ConfigGenerated
{
    // Hand-owned mapping beside the generator-owned immutable schema.
    public static class UISettingsMapping
    {
        public static UIConfig Convert(UISettingsRow row) => new UIConfig
        {
            Id = row.Id, PrefabKey = row.PrefabKey, PrefabPackage = row.PrefabPackage,
            Layer = (UILayer)row.Layer, CacheOnClose = row.CacheOnClose, MaxPoolSize = row.MaxPoolSize,
            PreloadCount = row.PreloadCount, PoolPriority = row.PoolPriority,
            PoolIdleTimeoutSeconds = row.PoolIdleTimeoutSeconds, FullScreen = row.FullScreen,
            UseLayerModalPolicy = row.UseLayerModalPolicy, Modal = row.Modal, UseTransition = row.UseTransition,
            TransitionType = (UITransitionType)row.TransitionType, ShowDuration = row.ShowDuration,
            HideDuration = row.HideDuration, IgnoreTransitionTimeScale = row.IgnoreTransitionTimeScale,
            SlideDistance = row.SlideDistance, StartScale = row.StartScale, CustomTransitionId = row.CustomTransitionId,
            RefreshTransitionBaselineOnReuse = row.RefreshTransitionBaselineOnReuse,
            SuspendWhenCovered = row.SuspendWhenCovered
        };
    }
}
