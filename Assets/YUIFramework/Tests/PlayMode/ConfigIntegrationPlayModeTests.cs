using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework.Configuration;
using YUIFramework.ConfigGenerated;

namespace YUIFramework.Tests
{
    public sealed class ConfigIntegrationPlayModeTests
    {
        private UIManager _ui;
        private UIResourceService _resources;
        private ConfigService _configs;

        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async () =>
        {
            _resources = new UIResourceService();
            _resources.Packages.Register(new ResourcesResourceProvider(), true);
            _configs = SampleUIConfiguration.Create(
                new ResourceConfigSource(_resources, prefix: "YUIConfig/"), ConfigFormat.MessagePack);
            _ui = new UIManager();
            await _ui.InitializeAsync(new CodeViewLoader());
        });
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            try { if (_ui.IsInitialized) await _ui.ShutdownAsync(); }
            finally
            {
                try { await _configs.ShutdownAsync(); }
                finally { await _resources.ShutdownAsync(); }
            }
            await UniTask.Yield();
        });
        private static IEnumerator Await(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Config integration timed out.");
            task.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator AllMigratedProfilesLoadRealBytesBeforeEnteringAndPreserveSemantics() => Await(Profiles());
        private async Task Profiles()
        {
            var snapshot = await _configs.InitializeAsync();
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            foreach (var profile in new[] { "bootstrap", "hello", "y2" })
            {
                var entered = false;
                await ConfigUIStartup.EnterAsync(_configs, _ui,
                    value => SampleUIConfiguration.Map(value, profile), async token =>
                    {
                        entered = true;
                        Assert.That(_ui.IsRegistered<SampleHelloPage>(), Is.True);
                        var page = await _ui.Navigator.PushAsync<SampleHelloPage>("config", cancellationToken: token);
                        Assert.That(page.ViewObject.activeSelf, Is.True);
                        if (profile == "hello")
                        {
                            var next = page.ViewObject.transform.Find("Panel/NextButton").GetComponent<Button>();
                            next.onClick.Invoke();
                            var deadline = Time.realtimeSinceStartup + 3;
                            while (_ui.Navigator.IsBusy && Time.realtimeSinceStartup < deadline) await UniTask.Yield();
                            Assert.That(_ui.Navigator.IsBusy, Is.False);
                            await _ui.Navigator.BackAsync();
                            var mvvm = await _ui.Navigator.PushAsync<MvvmSamplePage>();
                            Assert.That(mvvm.ViewObject.activeSelf, Is.True);
                            await _ui.Navigator.BackAsync();
                        }
                        else Assert.That(page.ViewObject.transform.Find("Panel/NextButton").GetComponent<Button>().interactable, Is.False);
                        await _ui.CloseAsync(page);
                    });
                Assert.That(entered, Is.True);
                Assert.That(_ui.TryGetConfig(typeof(SampleHelloPage), out var config), Is.True);
                var expected = snapshot.Get(UISettings.Table).Get(profile, profile == "y2" ? "SampleHelloPage" : "HelloPage");
                Assert.That(config.UseTransition, Is.EqualTo(expected.UseTransition));
                Assert.That(config.PreloadCount, Is.EqualTo(expected.PreloadCount));
                Assert.That(config.PoolPriority, Is.EqualTo(expected.PoolPriority));
                Assert.That(config.PoolIdleTimeoutSeconds, Is.EqualTo(expected.PoolIdleTimeoutSeconds));
                await _ui.ShutdownAsync();
                await _ui.InitializeAsync(new CodeViewLoader());
            }
            Assert.That(_resources.IsShutDown, Is.False);
        }

        [UnityTest]
        public IEnumerator BatchIsAtomicCopiesOverridesAndRejectsActiveReregistration() => Await(Batch());
        private async Task Batch()
        {
            var snapshot = await _configs.InitializeAsync();
            Assert.Throws<ArgumentException>(() => _ui.RegisterBatch(new[]
            {
                UIConfigRegistration.For<PackagePage>(new UIConfig
                { Id = "UnsupportedPackage", PrefabKey = "Prefab", PrefabPackage = "Explicit" })
            }));
            Assert.That(_ui.IsRegistered<PackagePage>(), Is.False);
            var registrations = SampleUIConfiguration.Map(snapshot, "hello",
                config => config.TransitionCurve = AnimationCurve.Linear(0, 0, 1, 1));
            var duplicate = new List<UIConfigRegistration>(registrations) { registrations[0] };
            Assert.Throws<ArgumentException>(() => _ui.RegisterBatch(duplicate));
            Assert.That(_ui.IsRegistered<SampleHelloPage>(), Is.False);
            Assert.That(_ui.IsRegistered<SecondSamplePage>(), Is.False);
            _ui.RegisterBatch(registrations);
            var page = await _ui.OpenAsync<SampleHelloPage>();
            Assert.Throws<ArgumentException>(() => _ui.RegisterBatch(registrations));
            Assert.That(page.ViewObject, Is.Not.Null);
            Assert.That(_ui.TryGetConfig(typeof(SampleHelloPage), out var config), Is.True);
            Assert.That(config.TransitionCurve.Evaluate(.5f), Is.EqualTo(.5f).Within(.001f));
            await _ui.CloseAsync(page);
        }

        [UnityTest]
        public IEnumerator MissingConfigAndInvalidMappingNeverRegisterOrEnter() => Await(Failure());
        private async Task Failure()
        {
            var bad = SampleUIConfiguration.Create(
                new ResourceConfigSource(_resources, prefix: "missing-config/"), ConfigFormat.MessagePack);
            var entered = false;
            try
            {
                await ConfigUIStartup.EnterAsync(bad, _ui,
                    snapshot => SampleUIConfiguration.Map(snapshot, "hello"),
                    token => { entered = true; return UniTask.CompletedTask; });
                Assert.Fail("Missing config succeeded.");
            }
            catch (ConfigDataException) { }
            Assert.That(entered, Is.False);
            Assert.That(_ui.IsRegistered<SampleHelloPage>(), Is.False);
            try
            {
                await ConfigUIStartup.EnterAsync(_configs, _ui,
                    snapshot => SampleUIConfiguration.Map(snapshot, "unknown"),
                    token => { entered = true; return UniTask.CompletedTask; });
                Assert.Fail("Unknown mapping succeeded.");
            }
            catch (ConfigDataException) { }
            Assert.That(entered, Is.False);
            Assert.That(_ui.IsRegistered<SampleHelloPage>(), Is.False);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            await bad.ShutdownAsync();
        }

        [UnityTest]
        public IEnumerator PackageIsPropagatedToPrewarmAndOpenAndBorrowedServiceSurvives() => Await(Packages());
        private async Task Packages()
        {
            await _ui.ShutdownAsync();
            var provider = new PrefabProvider();
            var resources = new UIResourceService();
            resources.Packages.Register(new ResourcesResourceProvider(), true);
            resources.Packages.Register(provider);
            try
            {
                await _ui.InitializeAsync(resources);
                var config = new UIConfig { Id = "Package", PrefabKey = "Prefab", PrefabPackage = "Explicit",
                    CacheOnClose = true, MaxPoolSize = 1, PreloadCount = 1, Layer = UILayer.Normal };
                var badPackage = config.Copy(); badPackage.PrefabPackage = "NotInstalled";
                Assert.Throws<ArgumentException>(() => _ui.RegisterBatch(new[] { UIConfigRegistration.For<PackagePage>(badPackage) }));
                Assert.That(_ui.IsRegistered<PackagePage>(), Is.False);
                var registration = UIConfigRegistration.For<PackagePage>(config);
                config.PrefabPackage = "MutatedAfterSnapshot";
                _ui.RegisterBatch(new[] { registration });
                await _ui.PrewarmRegisteredAsync();
                Assert.That(provider.Calls, Is.EqualTo(1));
                var page = await _ui.OpenAsync<PackagePage>();
                Assert.That(provider.Calls, Is.EqualTo(1));
                await _ui.CloseAsync(page);
                _ui.ClearPool<PackagePage>();
                resources.TrimUnused();
                await _ui.OpenAsync<PackagePage>();
                Assert.That(provider.Calls, Is.EqualTo(2));
                await _ui.ShutdownAsync();
                Assert.That(resources.IsShutDown, Is.False);
                Assert.That(resources.GetDiagnostics().TotalLeases, Is.Zero);
            }
            finally { await resources.ShutdownAsync(); provider.Dispose(); }
        }

        [UnityTest]
        public IEnumerator ResourceSourceCancellationAndCodecFailureReleaseNativeOwnership() => Await(ResourceCancellation());
        private async Task ResourceCancellation()
        {
            var provider = new TextProvider();
            var resources = new UIResourceService();
            resources.Packages.Register(provider, true);
            var configs = SampleUIConfiguration.Create(new ResourceConfigSource(resources), ConfigFormat.MessagePack);
            try
            {
                var loading = configs.InitializeAsync().AsTask();
                var shutdown = configs.ShutdownAsync().AsTask();
                try { await loading; Assert.Fail("Shutdown did not cancel config initialization."); }
                catch (OperationCanceledException) { }
                await shutdown;
                Assert.That(configs.Snapshot, Is.Null);
                Assert.That(resources.IsShutDown, Is.False);
                provider.Complete();
                await UniTask.Yield();
                Assert.That(resources.GetDiagnostics().TotalLeases, Is.Zero);
                Assert.That(provider.Releases, Is.EqualTo(1));
                try { await configs.InitializeAsync(); Assert.Fail("Invalid MessagePack succeeded."); }
                catch (ConfigDataException) { }
                Assert.That(resources.GetDiagnostics().TotalLeases, Is.Zero);
                resources.TrimUnused();
                Assert.That(provider.Releases, Is.EqualTo(2));
            }
            finally
            {
                provider.Complete();
                await configs.ShutdownAsync();
                await resources.ShutdownAsync();
                provider.Dispose();
            }
        }
        private sealed class TextProvider : IUIResourceProvider, IDisposable
        {
            private readonly TextAsset _asset = new TextAsset("not messagepack");
            private readonly UniTaskCompletionSource _gate = new UniTaskCompletionSource();
            internal int Releases;
            public string PackageName => "ConfigTest";
            internal void Complete() => _gate.TrySetResult();
            public async UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                await _gate.Task;
                return new Handle(this, _asset);
            }
            public UniTask ShutdownAsync() => UniTask.CompletedTask;
            public void Dispose() => UnityEngine.Object.Destroy(_asset);
            private sealed class Handle : IUINativeAssetHandle
            {
                private readonly TextProvider _owner;
                internal Handle(TextProvider owner, TextAsset asset) { _owner = owner; Asset = asset; }
                public UnityEngine.Object Asset { get; private set; }
                public bool IsValid => Asset != null;
                public void Release() { Assert.That(Asset, Is.Not.Null); Asset = null; _owner.Releases++; }
            }
        }
        public sealed class PackagePage : BaseContext
        {
            public override UILayer DefaultLayer => UILayer.Normal;
        }
        private sealed class PrefabProvider : IUIResourceProvider, IDisposable
        {
            internal int Calls;
            private readonly GameObject _prefab = new GameObject("PackagePrefab", typeof(RectTransform));
            public string PackageName => "Explicit";
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                Assert.That(key.PackageName, Is.EqualTo(PackageName)); Calls++;
                return UniTask.FromResult<IUINativeAssetHandle>(new Handle(_prefab));
            }
            public UniTask ShutdownAsync() => UniTask.CompletedTask;
            public void Dispose() => UnityEngine.Object.Destroy(_prefab);
            private sealed class Handle : IUINativeAssetHandle
            {
                public Handle(GameObject asset) { Asset = asset; }
                public UnityEngine.Object Asset { get; private set; }
                public bool IsValid => Asset != null;
                public void Release() { Asset = null; }
            }
        }
    }
}
