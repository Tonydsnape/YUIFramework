using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    public sealed class PoolingGovernancePlayModeTests
    {
        private const string PackageName = "PoolPackage";
        private static readonly UIMessageTopic<UIMessageUnit> PermanentTopic =
            new UIMessageTopic<UIMessageUnit>("pool.permanent");
        private static readonly UIMessageTopic<UIMessageUnit> DisplayTopic =
            new UIMessageTopic<UIMessageUnit>("pool.display");

        private FakeResourceProvider _provider;
        private UIResourceService _resources;
        private UIManager _manager;
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            PoolContext.Reset();
            FailingPrewarmContext.Reset();
            _root = new GameObject(
                "PoolTestRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _root.AddComponent<UIRoot>();
            _provider = new FakeResourceProvider(PackageName);
            _resources = new UIResourceService();
            _resources.Packages.Register(_provider, isDefault: true);
            _manager = new UIManager();
            _manager.Initialize(_resources, new UIObjectPool(globalCapacity: 16));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _provider?.CompleteAllPending();
            if (_manager != null && _manager.IsInitialized)
            {
                yield return ResourceOwnershipPlayModeTests.Await(
                    _manager.ShutdownAsync().AsTask());
            }

            if (_resources != null)
            {
                yield return ResourceOwnershipPlayModeTests.Await(
                    _resources.ShutdownAsync().AsTask());
            }

            _provider?.DestroyRemainingAssets();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }

            foreach (var eventSystem in UnityEngine.Object.FindObjectsOfType<EventSystem>())
            {
                UnityEngine.Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PrewarmCount_CreatesDistinctHiddenInstances_AndOpenHitsPool()
        {
            Register<PoolContext>("UI/Pool/Prewarm", preloadCount: 3, capacity: 3);

            var prewarm = _manager.PrewarmAsync<PoolContext>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(prewarm);

            Assert.That(prewarm.Result, Is.EqualTo(3));
            Assert.That(PoolContext.Created.Count, Is.EqualTo(3));
            Assert.That(new HashSet<GameObject>(PoolContext.Created), Has.Count.EqualTo(3));
            Assert.That(PoolContext.InitCount, Is.EqualTo(3));
            Assert.That(PoolContext.ShowCount, Is.Zero);
            Assert.That(PoolContext.HideCount, Is.Zero);
            Assert.That(_manager.Get<PoolContext>(), Is.Null);
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.EqualTo(3));
            Assert.That(_manager.PoolDiagnostics.PrewarmCount, Is.EqualTo(3));
            Assert.That(_manager.PoolDiagnostics.IdleLeasedCount, Is.EqualTo(3));
            foreach (var instance in PoolContext.Created)
            {
                Assert.That(instance.activeSelf, Is.False);
            }

            var open = _manager.OpenAsync<PoolContext>("first").AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(open);
            Assert.That(PoolContext.ShowCount, Is.EqualTo(1));
            Assert.That(_manager.PoolDiagnostics.HitCount, Is.EqualTo(1));
            Assert.That(_provider.LoadCallCount, Is.EqualTo(1));
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.CloseAsync(open.Result).AsTask());
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator PrewarmCount_IsCappedByGlobalCapacity()
        {
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.ShutdownAsync().AsTask());
            _manager = new UIManager();
            _manager.Initialize(_resources, new UIObjectPool(globalCapacity: 1));
            Register<PoolContext>("UI/Pool/GlobalCap", preloadCount: 3, capacity: 3);

            var prewarm = _manager.PrewarmAsync<PoolContext>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(prewarm);

            Assert.That(prewarm.Result, Is.EqualTo(1));
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.EqualTo(1));
            Assert.That(PoolContext.Created.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PrewarmFailure_RollsBackEveryInstanceCreatedByTheCall()
        {
            Register<FailingPrewarmContext>(
                "UI/Pool/Fail",
                preloadCount: 3,
                capacity: 3);
            FailingPrewarmContext.FailOnInitNumber = 2;

            var prewarm = _manager.PrewarmAsync<FailingPrewarmContext>().AsTask();
            yield return ResourceOwnershipPlayModeTests.AwaitFailure(prewarm);

            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.That(FailingPrewarmContext.DestroyCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ScopeRelease_CancelsInflightPrewarm_AndPreventsResurrection()
        {
            Register<PoolContext>("UI/Pool/Scoped", preloadCount: 2, capacity: 2);
            var scope = _manager.CreateModuleScope("Inventory");
            _provider.ManualCompletion = true;

            var prewarm = _manager.PrewarmAsync<PoolContext>(scope).AsTask();
            var guard = 0;
            while (_provider.LoadCallCount == 0 && guard++ < 300)
            {
                yield return null;
            }

            var release = _manager.ReleaseScopeAsync(scope).AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(release);
            yield return ResourceOwnershipPlayModeTests.AwaitCancellation(prewarm);
            _provider.CompleteAllPending();
            yield return null;
            yield return null;

            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.Throws<InvalidOperationException>(() =>
                _manager.PrewarmAsync<PoolContext>(scope));
        }

        [UnityTest]
        public IEnumerator LegacyPrewarm_ScopeCancellationAfterLoad_ReleasesInstantiatedObject()
        {
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.ShutdownAsync().AsTask());
            var loader = new DelayedLegacyLoader();
            _manager = new UIManager();
            _manager.Initialize(loader, new UIObjectPool());
            Register<PoolContext>("UI/Pool/LegacyCancel", preloadCount: 1, capacity: 1);
            var scope = _manager.CreateModuleScope("Legacy");

            var prewarm = _manager.PrewarmAsync<PoolContext>(scope).AsTask();
            var guard = 0;
            while (!loader.Started && guard++ < 300)
            {
                yield return null;
            }

            var release = _manager.ReleaseScopeAsync(scope).AsTask();
            loader.Complete();
            yield return ResourceOwnershipPlayModeTests.AwaitCancellation(prewarm);
            yield return ResourceOwnershipPlayModeTests.Await(release);

            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
            Assert.That(loader.ReleasedInstanceWasNonNull, Is.True);
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.Zero);
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator SceneUnload_ReleasesOnlyItsIdleScope()
        {
            Register<PoolContext>("UI/Pool/Scene", preloadCount: 1, capacity: 2);
            var scene = SceneManager.CreateScene("PoolScopeScene");
            var sceneScope = _manager.GetSceneScope(scene);

            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.PrewarmAsync<PoolContext>(sceneScope).AsTask());
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.PrewarmAsync<PoolContext>().AsTask());
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.EqualTo(2));

            var unload = SceneManager.UnloadSceneAsync(scene);
            yield return unload;
            var guard = 0;
            while (_manager.PoolDiagnostics.IdleCount != 1 && guard++ < 300)
            {
                yield return null;
            }

            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.EqualTo(1));
            Assert.That(
                _manager.PoolDiagnostics.Entries[0].Scope,
                Is.EqualTo(UIPoolScope.Global));
        }

        [UnityTest]
        public IEnumerator DisplayScope_CleansTransientState_ButKeepsLifetimeAndInitState()
        {
            Register<PoolContext>("UI/Pool/Display", preloadCount: 0, capacity: 1);
            var first = _manager.OpenAsync<PoolContext>("first").AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(first);
            var context = first.Result;
            var lifetime = context.LifetimeToken;
            var firstDisplay = context.CapturedDisplayToken;

            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(2));
            Assert.That(_manager.InputLocks.ActiveLockCount, Is.EqualTo(1));
            Assert.That(context.DisplayArguments, Is.EqualTo("first"));
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.CloseAsync(context).AsTask());

            Assert.That(firstDisplay.IsCancellationRequested, Is.True);
            Assert.That(lifetime.IsCancellationRequested, Is.False);
            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(1));
            Assert.That(_manager.InputLocks.ActiveLockCount, Is.Zero);
            Assert.That(context.DisplayResourceDisposeCount, Is.EqualTo(1));
            Assert.That(context.DisplayArguments, Is.Null);

            var second = _manager.OpenAsync<PoolContext>("second").AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(second);
            Assert.That(second.Result, Is.SameAs(context));
            Assert.That(context.ResetCount, Is.EqualTo(2));
            Assert.That(context.InitCountForInstance, Is.EqualTo(1));
            Assert.That(context.DisplayArguments, Is.EqualTo("second"));
        }

        [UnityTest]
        public IEnumerator LowMemory_EvictsIdleBeforeResources_AndPreservesActiveLease()
        {
            Register<PoolContext>("UI/Pool/Low", preloadCount: 1, capacity: 1, priority: -10);
            Register<HighPriorityContext>(
                "UI/Pool/High",
                preloadCount: 1,
                capacity: 1,
                priority: 10);
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.PrewarmRegisteredAsync().AsTask());
            var active = _manager.OpenAsync<HighPriorityContext>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(active);

            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.EqualTo(2));
            var released = _manager.HandleLowMemory();

            Assert.That(released, Is.GreaterThanOrEqualTo(2));
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.Zero);
            Assert.That(active.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(active.Result.ViewObject, Is.Not.Null);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
            Assert.That(
                _resources.GetDiagnostics().LeasedEntries[0].Key.Location,
                Is.EqualTo("UI/Pool/High"));
        }

        [UnityTest]
        public IEnumerator ReuseStress_OneThousandCycles_KeepsInstanceAndNativeCountsStable()
        {
            Register<PoolContext>("UI/Pool/Stress", preloadCount: 1, capacity: 1);
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.PrewarmAsync<PoolContext>().AsTask());

            for (var index = 0; index < 1000; index++)
            {
                var open = _manager.OpenAsync<PoolContext>(index).AsTask();
                yield return ResourceOwnershipPlayModeTests.Await(open);
                yield return ResourceOwnershipPlayModeTests.Await(
                    _manager.CloseAsync(open.Result).AsTask());
            }

            Assert.That(PoolContext.Created.Count, Is.EqualTo(1));
            Assert.That(_provider.LoadCallCount, Is.EqualTo(1));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
            _manager.ClearPool<PoolContext>();
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.That(_resources.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1));
        }

        private void Register<T>(
            string prefabKey,
            int preloadCount,
            int capacity,
            int priority = 0)
            where T : BaseContext
        {
            _manager.Register<T>(new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = prefabKey,
                Layer = UILayer.Normal,
                CacheOnClose = true,
                MaxPoolSize = capacity,
                PreloadCount = preloadCount,
                PoolPriority = priority,
                FullScreen = true
            });
        }

        private sealed class PoolContext : BaseContext
        {
            public static readonly List<GameObject> Created = new List<GameObject>();
            public static int InitCount;
            public static int ShowCount;
            public static int HideCount;

            public int ResetCount { get; private set; }
            public int InitCountForInstance { get; private set; }
            public int DisplayResourceDisposeCount { get; private set; }
            public CancellationToken CapturedDisplayToken { get; private set; }

            public override UILayer DefaultLayer => UILayer.Normal;

            public static void Reset()
            {
                Created.Clear();
                InitCount = 0;
                ShowCount = 0;
                HideCount = 0;
            }

            protected override void HandleInit()
            {
                InitCount++;
                InitCountForInstance++;
                Created.Add(ViewObject);
                SubscribeMessage(PermanentTopic, _ => { });
            }

            protected override void HandleResetForReuse(object previousArgs, object nextArgs)
            {
                ResetCount++;
            }

            protected override void HandleShow(object args)
            {
                ShowCount++;
                CapturedDisplayToken = DisplayToken;
                SubscribeDisplayMessage(DisplayTopic, _ => { });
                AcquireDisplayInputLock("pool display");
                TrackDisplayResource(new CallbackDisposable(
                    () => DisplayResourceDisposeCount++));
            }

            protected override void HandleHide()
            {
                HideCount++;
            }
        }

        private sealed class FailingPrewarmContext : BaseContext
        {
            public static int InitCount;
            public static int DestroyCount;
            public static int FailOnInitNumber;

            public override UILayer DefaultLayer => UILayer.Normal;

            public static void Reset()
            {
                InitCount = 0;
                DestroyCount = 0;
                FailOnInitNumber = 0;
            }

            protected override void HandleInit()
            {
                InitCount++;
                if (InitCount == FailOnInitNumber)
                {
                    throw new InvalidOperationException("Forced prewarm init failure.");
                }
            }

            protected override void HandleDestroy()
            {
                DestroyCount++;
            }
        }

        private sealed class HighPriorityContext : BaseContext
        {
            public override UILayer DefaultLayer => UILayer.Normal;
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _callback;

            public CallbackDisposable(Action callback)
            {
                _callback = callback;
            }

            public void Dispose()
            {
                var callback = Interlocked.Exchange(ref _callback, null);
                callback?.Invoke();
            }
        }

        private sealed class DelayedLegacyLoader : IResourceLoader, IDisposable
        {
            private readonly UniTaskCompletionSource<GameObject> _completion =
                new UniTaskCompletionSource<GameObject>();
            private GameObject _prefab;

            public bool Started { get; private set; }
            public int ReleaseCount { get; private set; }
            public bool ReleasedInstanceWasNonNull { get; private set; }

            public UniTask<GameObject> LoadPrefabAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                Started = true;
                return _completion.Task;
            }

            public void Release(string key, GameObject instance)
            {
                ReleaseCount++;
                ReleasedInstanceWasNonNull |= instance != null;
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            public void Complete()
            {
                _prefab = new GameObject(
                    "LegacyDelayedPrefab",
                    typeof(RectTransform),
                    typeof(UIView));
                _prefab.SetActive(false);
                _completion.TrySetResult(_prefab);
            }

            public void Dispose()
            {
                if (_prefab != null)
                {
                    UnityEngine.Object.DestroyImmediate(_prefab);
                    _prefab = null;
                }
            }
        }
    }
}
