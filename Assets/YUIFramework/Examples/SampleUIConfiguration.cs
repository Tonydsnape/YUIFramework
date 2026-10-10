using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using YUIFramework.Configuration;
using YUIFramework.ConfigGenerated;

namespace YUIFramework
{
    public static class SampleUIConfiguration
    {
        public static ConfigService Create(IConfigSource source, ConfigFormat format) =>
            new ConfigService(source, new ConfigCodec(), format, GeneratedConfigCatalog.Tables);

        public static IReadOnlyList<UIConfigRegistration> Map(ConfigSnapshot snapshot, string profile,
            Action<UIConfig> typedOverride = null)
        {
            var table = snapshot.Get(UISettings.Table);
            var configs = new Dictionary<string, UIConfig>(StringComparer.Ordinal);
            foreach (var row in table.Values)
            {
                var config = Convert(row);
                UIConfigRegistration.Validate(config); // Validate every profile before registering any record.
                if (row.Profile != profile) continue;
                typedOverride?.Invoke(config); // Curves/custom IDs stay explicitly owned by application code.
                UIConfigRegistration.Validate(config);
                configs.Add(row.Id, config);
            }
            var registrations = new List<UIConfigRegistration>();
            switch (profile)
            {
                case "bootstrap":
                    registrations.Add(UIConfigRegistration.For<SampleHelloPage>(Take(configs, "HelloPage")));
                    break;
                case "hello":
                    registrations.Add(UIConfigRegistration.For<SampleHelloPage>(Take(configs, "HelloPage")));
                    registrations.Add(UIConfigRegistration.For<SecondSamplePage>(Take(configs, "SecondSamplePage")));
                    registrations.Add(UIConfigRegistration.For<VirtualListSamplePage>(Take(configs, "VirtualListSamplePage")));
                    registrations.Add(UIConfigRegistration.For<MvvmSamplePage>(Take(configs, "MvvmSamplePage")));
                    break;
                case "y2":
                    registrations.Add(UIConfigRegistration.For<SampleHelloPage>(Take(configs, "SampleHelloPage")));
                    break;
                default: throw new ConfigDataException($"Unknown UI profile: {profile}");
            }
            if (configs.Count != 0) throw new ConfigDataException("UI table has records without an explicit typed mapping.");
            return registrations;
        }

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
        private static UIConfig Take(Dictionary<string, UIConfig> configs, string id)
        {
            if (!configs.TryGetValue(id, out var config)) throw new ConfigDataException($"UI record missing: {id}");
            configs.Remove(id);
            return config;
        }
    }

    // CodeView examples explicitly select Resources in Player; there is no hardcoded-config fallback.
    public sealed class SampleConfigOwner
    {
        private readonly UIResourceService _resources = null;
        public ConfigService Service { get; }
        public IUIResourceService SampleResources => _resources;
        public SampleConfigOwner()
        {
            _resources = new UIResourceService();
            _resources.Packages.Register(new ResourcesResourceProvider(), true);
#if UNITY_EDITOR
            Service = SampleUIConfiguration.Create(new EditorJsonConfigSource(), ConfigFormat.Json);
#else
            Service = SampleUIConfiguration.Create(
                new ResourceConfigSource(_resources, prefix: "YUIConfig/"), ConfigFormat.MessagePack);
#endif
        }
        public async UniTask ShutdownAsync()
        {
            try { await Service.ShutdownAsync(); }
            finally { if (_resources != null) await _resources.ShutdownAsync(); }
        }
    }
}
