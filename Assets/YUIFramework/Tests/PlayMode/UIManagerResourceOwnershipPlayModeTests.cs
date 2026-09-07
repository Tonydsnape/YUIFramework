using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    /// <summary>
    /// 阶段 5：UIManager 与资源所有权体系的集成。
    /// 覆盖打开成功、取消、加载失败、池化保留、清池与关闭时的租约计数。
    /// </summary>
    public sealed class UIManagerResourceOwnershipPlayModeTests
    {
        private const string UiPackage = "UIPackage";

        private FakeResourceProvider _provider;
        private UIResourceService _service;
        private UIManager _manager;
        private GameObject _rootObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rootObject = new GameObject(
                "TestUIRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _rootObject.AddComponent<UIRoot>();

            _provider = new FakeResourceProvider(UiPackage);
            _service = new UIResourceService();
            _service.Packages.Register(_provider, isDefault: true);

            _manager = new UIManager();
            _manager.Initialize(_service);
            _manager.Navigator.Clear();
            _manager.MessageCenter.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null && _manager.IsInitialized)
            {
                yield return ResourceOwnershipPlayModeTests.Await(_manager.ShutdownAsync().AsTask());
            }

            if (_service != null)
            {
                _provider?.CompleteAllPending();
                yield return ResourceOwnershipPlayModeTests.Await(_service.ShutdownAsync().AsTask());
            }

            _provider?.DestroyRemainingAssets();

            if (_rootObject != null)
            {
                Object.Destroy(_rootObject);
            }

            foreach (var eventSystem in Object.FindObjectsOfType<EventSystem>())
            {
                Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator OpenPage_HoldsExplicitInstanceLease()
        {
            Register<OwnershipPageA>("UI/Ownership/A", false);

            var openTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(openTask);

            Assert.That(_provider.LoadCallCount, Is.EqualTo(1));
            Assert.That(openTask.Result.ViewObject, Is.Not.Null);

            var snapshot = _service.GetDiagnostics();
            Assert.That(snapshot.TotalLeases, Is.EqualTo(1), "打开的 context 必须明确持有一份租约");
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CloseDestructive_ReleasesInstanceLeaseExactlyOnce()
        {
            Register<OwnershipPageA>("UI/Ownership/A", false);

            var openTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(openTask);
            var page = openTask.Result;

            yield return ResourceOwnershipPlayModeTests.Await(_manager.CloseAsync(page).AsTask());

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero, "销毁式关闭必须归还租约");
            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1), "不得二次释放");
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True);
        }

        [UnityTest]
        public IEnumerator CancelOpen_DoesNotLeakResources()
        {
            Register<OwnershipPageA>("UI/Ownership/Cancel", false);
            _provider.ManualCompletion = true;

            using var cts = new CancellationTokenSource();
            var openTask = _manager.OpenAsync<OwnershipPageA>(null, cts.Token).AsTask();

            // 打开命令要经过 FIFO 协调器，必须等底层加载真正开始，
            // 否则测试覆盖的就不是“加载进行中被取消”这个场景。
            var guard = 0;
            while (_provider.LoadCallCount == 0 && guard++ < 300)
            {
                yield return null;
            }

            Assert.That(_provider.LoadCallCount, Is.EqualTo(1), "底层加载应已开始");

            cts.Cancel();

            // 必须先观察到取消，此时底层加载仍在进行中：
            // 这样才能确定性地覆盖“加载进行中被取消”而不是与完成竞争。
            yield return ResourceOwnershipPlayModeTests.AwaitCancellation(openTask);

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero, "取消的打开不得留下租约");

            // 底层不可取消，随后自然完成；此时已无等待者，必须立即释放。
            _provider.CompleteAllPending();
            yield return null;
            yield return null;

            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1), "取消后底层句柄必须被释放");
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1), "不得二次释放");
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True);
        }

        [UnityTest]
        public IEnumerator LoadFailure_DoesNotLeakOrHoldLease()
        {
            Register<OwnershipPageA>("UI/Ownership/Broken", false);
            _provider.FailingLocations.Add("UI/Ownership/Broken");

            var openTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.AwaitFailure(openTask);

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.That(_service.GetDiagnostics().EntryCount, Is.Zero, "失败的加载不得留下条目");
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True);
        }

        [UnityTest]
        public IEnumerator PooledClose_RetainsLease_AndClearPoolReleasesIt()
        {
            Register<OwnershipPageA>("UI/Ownership/Pooled", true);

            var openTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(openTask);
            var page = openTask.Result;

            yield return ResourceOwnershipPlayModeTests.Await(_manager.CloseAsync(page).AsTask());

            Assert.That(page.State, Is.EqualTo(UIContextState.Pooled));
            Assert.That(_service.GetDiagnostics().TotalLeases, Is.EqualTo(1), "池化期间必须保留实例租约");
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero);

            // 池化命中不应重新加载。
            var reopenTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(reopenTask);
            Assert.That(reopenTask.Result, Is.SameAs(page));
            Assert.That(_provider.LoadCallCount, Is.EqualTo(1));
            Assert.That(_service.GetDiagnostics().TotalLeases, Is.EqualTo(1));

            yield return ResourceOwnershipPlayModeTests.Await(_manager.CloseAsync(page).AsTask());
            _manager.ClearPool<OwnershipPageA>();

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero, "清池后必须释放实例租约");
            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Shutdown_ReleasesEveryOutstandingLease()
        {
            Register<OwnershipPageA>("UI/Ownership/A", false);
            Register<OwnershipPageB>("UI/Ownership/B", false);

            yield return ResourceOwnershipPlayModeTests.Await(_manager.OpenAsync<OwnershipPageA>().AsTask());
            yield return ResourceOwnershipPlayModeTests.Await(_manager.OpenAsync<OwnershipPageB>().AsTask());

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.EqualTo(2));

            yield return ResourceOwnershipPlayModeTests.Await(_manager.ShutdownAsync().AsTask());

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero, "关闭后不得残留任何租约");
            Assert.That(_service.TrimUnused(), Is.EqualTo(2));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(2));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1));
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True);
        }

        [UnityTest]
        public IEnumerator ExternallyDestroyedPooledInstance_ReclaimsOrphanedLease()
        {
            Register<OwnershipPageA>("UI/Ownership/Orphan", true);

            var openTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(openTask);
            var page = openTask.Result;
            var viewObject = page.ViewObject;

            yield return ResourceOwnershipPlayModeTests.Await(_manager.CloseAsync(page).AsTask());
            Assert.That(_service.GetDiagnostics().TotalLeases, Is.EqualTo(1), "池化期间保留实例租约");

            // 框架外部销毁池中实例：对象池会在下次 TryGet 时静默丢弃该条目，
            // 它再也不会走到 ReleaseContextInternal。
            Object.DestroyImmediate(viewObject);
            yield return null;

            var reopenTask = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(reopenTask);

            Assert.That(
                _service.GetDiagnostics().TotalLeases,
                Is.EqualTo(1),
                "孤儿租约必须被回收，只应保留新实例的那一份");
            Assert.That(
                _provider.LoadCallCount,
                Is.EqualTo(1),
                "资源本身仍在无引用缓存/被引用中，重开不应触发新的底层加载");

            // 回收之后资源仍可正常归零。
            yield return ResourceOwnershipPlayModeTests.Await(
                _manager.CloseAsync(reopenTask.Result).AsTask());
            _manager.ClearPool<OwnershipPageA>();

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1), "不得二次释放");
        }

        [UnityTest]
        public IEnumerator OpeningDifferentType_FinalizesInvalidPooledEntry()
        {
            Register<OwnershipPageA>("UI/Ownership/OrphanA", true);
            Register<OwnershipPageB>("UI/Ownership/OrphanB", false);

            var first = _manager.OpenAsync<OwnershipPageA>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(first);
            var orphan = first.Result;
            yield return ResourceOwnershipPlayModeTests.Await(_manager.CloseAsync(orphan).AsTask());
            Object.DestroyImmediate(orphan.ViewObject);
            yield return null;

            var second = _manager.OpenAsync<OwnershipPageB>().AsTask();
            yield return ResourceOwnershipPlayModeTests.Await(second);

            Assert.That(
                _service.GetDiagnostics().TotalLeases,
                Is.EqualTo(1),
                "不同类型的打开也必须清理所有失效池条目，只保留当前页面租约");
            _manager.ClearPool<OwnershipPageA>();
            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CancelDuringShowTransition_ReturnsLeaseOnceOperationUnwinds()
        {
            // 转场开启后，实例租约登记之后仍存在挂起点。阶段 3 的操作协调器会在
            // 节点体回滚完成之前就把取消抛给调用方，因此租约归还是“最终一致”的，
            // 而不是与 OperationCanceledException 同步发生。这里明确验证该语义，
            // 并确保最终既不泄漏也不二次释放。
            _manager.Register<OwnershipPageA>(new UIConfig
            {
                Id = nameof(OwnershipPageA),
                PrefabKey = "UI/Ownership/Transition",
                Layer = UILayer.Normal,
                CacheOnClose = false,
                MaxPoolSize = 0,
                FullScreen = true,
                UseTransition = true,
                TransitionType = UITransitionType.Fade,
                ShowDuration = 2f
            });

            using var cts = new CancellationTokenSource();
            var openTask = _manager.OpenAsync<OwnershipPageA>(null, cts.Token).AsTask();

            var guard = 0;
            while (_service.GetDiagnostics().TotalLeases == 0 && guard++ < 300)
            {
                yield return null;
            }

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.EqualTo(1), "转场期间应已持有实例租约");

            cts.Cancel();
            yield return ResourceOwnershipPlayModeTests.AwaitCancellation(openTask);

            guard = 0;
            while (_service.GetDiagnostics().TotalLeases > 0 && guard++ < 300)
            {
                yield return null;
            }

            Assert.That(_service.GetDiagnostics().TotalLeases, Is.Zero, "回滚完成后不得残留租约");
            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1), "不得二次释放");
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True);
        }

        private void Register<T>(string key, bool cacheOnClose) where T : BaseContext
        {
            _manager.Register<T>(new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = key,
                Layer = UILayer.Normal,
                CacheOnClose = cacheOnClose,
                MaxPoolSize = cacheOnClose ? 1 : 0,
                FullScreen = true
            });
        }

        private class OwnershipPageA : BaseContext
        {
            public override UILayer DefaultLayer => UILayer.Normal;
        }

        private class OwnershipPageB : BaseContext
        {
            public override UILayer DefaultLayer => UILayer.Normal;
        }
    }
}
