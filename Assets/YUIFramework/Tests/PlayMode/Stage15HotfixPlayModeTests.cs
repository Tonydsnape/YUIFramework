using System;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework.Bootstrap;
using YUIFramework.CodeLoading;
using YUIFramework.Hotfix.Sample;

namespace YUIFramework.Tests
{
    public sealed class Stage15HotfixPlayModeTests
    {
        private sealed class Package : IBootstrapPackageHandle
        {
            public string PackageName => "Code";
            public object NativePackage => null;
            public string ActiveVersion => "fixture";
            public bool IsManifestVerified { get; set; } = true;
        }
        private sealed class Handle : IUINativeAssetHandle
        {
            public UnityEngine.Object Asset { get; private set; }
            public bool IsValid => Asset;
            private readonly Action _release;
            internal Handle(UnityEngine.Object asset, Action release) { Asset = asset; _release = release; }
            public void Release() { if (Asset) { Asset = null; _release(); } }
        }
        private sealed class Provider : IUIResourceProvider
        {
            internal TextAsset Asset = new TextAsset("explicit mock DLL payload (not executable bytes)");
            internal int Loads, Releases, Shutdowns;
            internal UniTaskCompletionSource<IUINativeAssetHandle> Pending;
            public string PackageName => "Code";
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                Loads++;
                return Pending != null ? Pending.Task : UniTask.FromResult<IUINativeAssetHandle>(NewHandle());
            }
            internal IUINativeAssetHandle NewHandle() => new Handle(Asset, () => Releases++);
            public UniTask ShutdownAsync() { Shutdowns++; return UniTask.CompletedTask; }
        }
        private sealed class Runtime : IHotfixAssemblyRuntime
        {
            internal int Calls;
            internal Action OnLoad;
            internal Assembly Assembly = typeof(HotfixSampleEntry).Assembly;
            public Assembly Load(byte[] bytes, string expected) { Calls++; OnLoad?.Invoke(); return Assembly; }
        }
        public sealed class ConstructorEntry : IHotfixEntry
        {
            public static Action Constructing;
            public static int Calls;
            public ConstructorEntry() { Calls++; Constructing?.Invoke(); }
            public UniTask<BaseContext> EnterAsync(IUIService ui, UIConfig config, CancellationToken token) =>
                throw new NotSupportedException("Constructor-boundary test only.");
        }
        private static BootstrapReadyContext Ready(bool verified = true) => new BootstrapReadyContext(
            Guid.NewGuid(), new[] { new Package { IsManifestVerified = verified } }, false, false);
        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        private static HotfixAssemblySpec Spec(Provider provider, string type = "YUIFramework.Hotfix.Sample.HotfixSampleEntry",
            string hash = null, string identity = null) => new HotfixAssemblySpec("Code", "Hotfix.dll",
                identity ?? typeof(HotfixSampleEntry).Assembly.FullName, type, hash ?? Hash(provider.Asset.bytes));

        [UnityTest]
        public IEnumerator Stage15_EntryConstructorCancellationIsRetainedAndConstructorFailureCannotRetry() => Constructor().ToCoroutine();
        private static async UniTask Constructor()
        {
            foreach (var throws in new[] { false, true })
            {
                var resources = new UIResourceService(); var provider = new Provider();
                resources.Packages.Register(provider, true); using var cancellation = new CancellationTokenSource();
                ConstructorEntry.Calls = 0;
                ConstructorEntry.Constructing = () =>
                { if (throws) throw new InvalidOperationException("constructor failed"); cancellation.Cancel(); };
                var runtime = new Runtime { Assembly = typeof(ConstructorEntry).Assembly };
                var loader = new ResourceHotfixCodeLoader(Spec(provider, typeof(ConstructorEntry).FullName,
                    identity: runtime.Assembly.FullName), _ => resources, runtime);
                try
                {
                    Exception failure = null;
                    try { await loader.LoadAsync(Ready(), cancellation.Token); }
                    catch (Exception error) { failure = error; }
                    Assert.IsNotNull(failure);
                    if (throws)
                    {
                        Assert.AreSame(failure, loader.LastFailure);
                        Assert.IsInstanceOf<TargetInvocationException>(failure); Assert.IsNull(loader.Entry);
                        Assert.AreEqual("constructor failed", failure.InnerException.Message);
                        var rejected = false;
                        try { await loader.LoadAsync(Ready(), default); } catch (InvalidOperationException) { rejected = true; }
                        Assert.IsTrue(rejected);
                    }
                    else
                    {
                        Assert.IsInstanceOf<OperationCanceledException>(failure);
                        Assert.IsInstanceOf<OperationCanceledException>(loader.LastFailure);
                        Assert.AreEqual(cancellation.Token, ((OperationCanceledException)failure).CancellationToken);
                        Assert.IsNotNull(loader.Entry);
                        var entry = loader.Entry; await loader.LoadAsync(Ready(), default); Assert.AreSame(entry, loader.Entry);
                    }
                    Assert.AreEqual(1, ConstructorEntry.Calls); Assert.AreEqual(1, runtime.Calls);
                    Assert.AreEqual(0, resources.GetDiagnostics().TotalLeases);
                }
                finally
                {
                    ConstructorEntry.Constructing = null;
                    await resources.ShutdownAsync(); UnityEngine.Object.Destroy(provider.Asset);
                }
            }
        }

        [UnityTest]
        public IEnumerator Stage15_PublicHotfixEntryOpensAndPrewarmsThroughBothActivatorPaths() => Run().ToCoroutine();
        private static async UniTask Run()
        {
            var ui = new UIManager();
            using var views = new CodeViewLoader();
            var resources = new UIResourceService(); var provider = new Provider();
            resources.Packages.Register(provider, true);
            ui.Initialize(views);
            var runtime = new Runtime();
            runtime.OnLoad = () => Assert.AreEqual(0, resources.GetDiagnostics().TotalLeases);
            var loader = new ResourceHotfixCodeLoader(Spec(provider), _ => resources, runtime);
            var game = new HotfixGameEntry(loader, _ => ui, new UIConfig
            { Id = "Hotfix", PrefabKey = "Hotfix", Layer = UILayer.Normal, CacheOnClose = true, MaxPoolSize = 1, PreloadCount = 1 });
            try
            {
                await loader.LoadAsync(Ready(), default);
                await game.EnterAsync(Ready(), default);
                var page = (HotfixSamplePage)game.Page;
                Assert.AreEqual(UIContextState.Opened, page.State);
                page.ViewObject.GetComponentInChildren<Button>().onClick.Invoke();
                await UniTask.NextFrame();
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
                Assert.AreEqual(1, page.Count); Assert.AreEqual(page.DefaultFocus, ui.RootRuntime.EventSystem.currentSelectedGameObject);
                await ui.CloseAsync(page); await ui.ClearPoolAsync<HotfixSamplePage>();
                Assert.AreEqual(UIContextState.Released, page.State);
                Assert.AreEqual(1, await ui.PrewarmAsync<HotfixSamplePage>());
                var warmed = await ui.OpenAsync<HotfixSamplePage>();
                Assert.AreNotSame(page, warmed);
                Assert.AreEqual(UIContextState.Opened, warmed.State);
                await ui.CloseAsync(warmed);
                await loader.LoadAsync(Ready(), default);
                Assert.AreEqual(1, runtime.Calls); Assert.AreEqual(1, provider.Loads);
                Assert.AreEqual(8, await HotfixAotWitness.ExerciseAsync());
                await ui.ShutdownAsync();
                Assert.AreEqual(UIContextState.Released, warmed.State);
                Assert.AreEqual(0, resources.GetDiagnostics().TotalLeases);
                resources.TrimUnused(); Assert.AreEqual(1, provider.Releases);
                Assert.AreEqual(0, provider.Shutdowns, "Code loader borrows resource owner.");
            }
            finally { await ui.ShutdownAsync(); await resources.ShutdownAsync(); UnityEngine.Object.Destroy(provider.Asset); }
        }
        [UnityTest]
        public IEnumerator Stage15_HashReadinessIdentityAndEntryFaultNeverPublishAndReleaseLeases() => Faults().ToCoroutine();
        private static async UniTask Faults()
        {
            foreach (var mode in new[] { "unverified", "hash", "identity", "entry", "constructor" })
            {
                var resources = new UIResourceService(); var provider = new Provider();
                resources.Packages.Register(provider, true);
                var runtime = new Runtime();
                var spec = Spec(provider, mode == "entry" ? "Not.A.Real.Type" :
                    mode == "constructor" ? typeof(HotfixSamplePage).FullName : typeof(HotfixSampleEntry).FullName,
                    mode == "hash" ? new string('0', 64) : null,
                    mode == "identity" ? "Wrong, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" : null);
                var loader = new ResourceHotfixCodeLoader(spec, _ => resources, runtime);
                try
                {
                    var failed = false;
                    try { await loader.LoadAsync(Ready(mode != "unverified"), default); }
                    catch (Exception) { failed = true; }
                    Assert.IsTrue(failed, mode); Assert.IsNull(loader.Entry);
                    Assert.AreEqual(0, resources.GetDiagnostics().TotalLeases);
                    Assert.AreEqual(mode == "unverified" || mode == "hash" ? 0 : 1, runtime.Calls);
                    resources.TrimUnused(); Assert.AreEqual(provider.Loads, provider.Releases);
                    if (mode == "identity" || mode == "entry" || mode == "constructor")
                    {
                        failed = false;
                        try { await loader.LoadAsync(Ready(), default); } catch (InvalidOperationException) { failed = true; }
                        Assert.IsTrue(failed); Assert.AreEqual(1, runtime.Calls);
                    }
                }
                finally { await resources.ShutdownAsync(); UnityEngine.Object.Destroy(provider.Asset); }
            }
        }
        [UnityTest]
        public IEnumerator Stage15_CancelAfterIrreversibleLoadResumesWithoutLoadingAssemblyTwice() => AfterLoad().ToCoroutine();
        private static async UniTask AfterLoad()
        {
            var resources = new UIResourceService(); var provider = new Provider();
            resources.Packages.Register(provider, true);
            using var cancellation = new CancellationTokenSource();
            var runtime = new Runtime { OnLoad = cancellation.Cancel };
            var loader = new ResourceHotfixCodeLoader(Spec(provider), _ => resources, runtime);
            try
            {
                var canceled = false;
                try { await loader.LoadAsync(Ready(), cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Assert.IsTrue(canceled); Assert.IsNotNull(loader.LoadedAssembly); Assert.IsNull(loader.Entry);
                await loader.LoadAsync(Ready(), default);
                Assert.IsNotNull(loader.Entry); Assert.AreEqual(1, runtime.Calls);
                Assert.AreEqual(1, provider.Loads); Assert.AreEqual(0, resources.GetDiagnostics().TotalLeases);
            }
            finally { await resources.ShutdownAsync(); UnityEngine.Object.Destroy(provider.Asset); }
        }
        [UnityTest]
        public IEnumerator Stage15_CanceledNoncooperativeAssetLoadDrainsLateHandleWithoutLoadingCode() => Late().ToCoroutine();
        private static async UniTask Late()
        {
            var resources = new UIResourceService();
            var provider = new Provider { Pending = new UniTaskCompletionSource<IUINativeAssetHandle>() };
            resources.Packages.Register(provider, true); using var cancellation = new CancellationTokenSource();
            var runtime = new Runtime();
            var loader = new ResourceHotfixCodeLoader(Spec(provider), _ => resources, runtime);
            try
            {
                var task = loader.LoadAsync(Ready(), cancellation.Token).AsTask();
                cancellation.Cancel();
                try { await task; Assert.Fail("Expected cancellation."); } catch (OperationCanceledException) { }
                Assert.AreEqual(0, runtime.Calls); Assert.IsNull(loader.Entry);
                provider.Pending.TrySetResult(provider.NewHandle());
                await UniTask.Yield();
                resources.TrimUnused();
                Assert.AreEqual(0, resources.GetDiagnostics().TotalLeases);
                Assert.AreEqual(1, provider.Releases);
            }
            finally
            {
                if (provider.Pending.Task.Status == UniTaskStatus.Pending)
                    provider.Pending.TrySetResult(provider.NewHandle());
                await resources.ShutdownAsync(); UnityEngine.Object.Destroy(provider.Asset);
            }
        }
    }
}
