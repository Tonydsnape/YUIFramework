using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    public sealed class UIRootAndInputPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var root in UnityEngine.Object.FindObjectsOfType<UIRoot>())
            {
                UnityEngine.Object.Destroy(root.gameObject);
            }

            foreach (var eventSystem in UnityEngine.Object.FindObjectsOfType<EventSystem>())
            {
                UnityEngine.Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator OwnedRuntime_CreatesCompleteRootInSafeOrderAndDestroysOwnedObjects()
        {
            var runtime = UIRootRuntime.CreateOwned();
            var rootObject = runtime.Root.gameObject;
            var eventObject = runtime.EventSystem.gameObject;

            Assert.That(rootObject.GetComponent<RectTransform>(), Is.Not.Null);
            Assert.That(rootObject.GetComponent<Canvas>(), Is.Not.Null);
            Assert.That(rootObject.GetComponent<CanvasScaler>(), Is.Not.Null);
            Assert.That(rootObject.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            Assert.That(runtime.EventSystem.GetComponent<StandaloneInputModule>(), Is.Not.Null);
            Assert.That(runtime.Root.GetLayerRoot(UILayer.Debug), Is.Not.Null);

            runtime.Dispose();
            yield return null;

            Assert.That(rootObject == null, Is.True);
            Assert.That(eventObject == null, Is.True);
        }

        [UnityTest]
        public IEnumerator ExternalRuntime_DoesNotDestroyInjectedRootOrEventSystem()
        {
            var rootObject = CreateRootObject("ExternalRoot");
            var eventObject = CreateEventSystemObject("ExternalEventSystem");
            var runtime = UIRootRuntime.CreateExternal(
                rootObject.GetComponent<UIRoot>(),
                eventObject.GetComponent<EventSystem>());

            runtime.Dispose();
            yield return null;

            Assert.That(rootObject, Is.Not.Null);
            Assert.That(eventObject, Is.Not.Null);
        }

        [Test]
        public void CameraMode_RequiresExplicitCameraWhileOverlayDoesNot()
        {
            var rootObject = CreateRootObject("CameraRoot");
            var eventObject = CreateEventSystemObject("CameraEventSystem");
            Assert.Throws<ArgumentException>(() => UIRootRuntime.CreateExternal(
                rootObject.GetComponent<UIRoot>(),
                eventObject.GetComponent<EventSystem>(),
                new UIRootRuntimeOptions { RenderMode = RenderMode.ScreenSpaceCamera }));
        }

        [Test]
        public void DuplicateRuntimeAndEventSystem_AreRejectedDeterministically()
        {
            var first = UIRootRuntime.CreateOwned();
            Assert.Throws<InvalidOperationException>(() => UIRootRuntime.CreateOwned());

            var secondRoot = CreateRootObject("SecondRoot");
            var secondEvent = CreateEventSystemObject("SecondEvent");
            Assert.Throws<InvalidOperationException>(() => UIRootRuntime.CreateExternal(
                secondRoot.GetComponent<UIRoot>(),
                secondEvent.GetComponent<EventSystem>()));
            first.Dispose();
        }

        [UnityTest]
        public IEnumerator Manager_ShutdownCanRebuildWithoutStaticResidue()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            var firstRoot = manager.RootRuntime.Root;
            yield return Await(manager.ShutdownAsync().AsTask());

            manager.Initialize(loader);
            Assert.That(manager.RootRuntime.Root, Is.Not.SameAs(firstRoot));
            Assert.That(UIRoot.Active, Is.SameAs(manager.RootRuntime.Root));
            Assert.That(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length, Is.EqualTo(1));
            yield return Await(manager.ShutdownAsync().AsTask());
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator DestroyedExternalRoot_FailsWithExplicitUnavailableException()
        {
            var rootObject = CreateRootObject("SceneRoot");
            var eventObject = CreateEventSystemObject("SceneEventSystem");
            var runtime = UIRootRuntime.CreateExternal(
                rootObject.GetComponent<UIRoot>(),
                eventObject.GetComponent<EventSystem>());

            UnityEngine.Object.Destroy(rootObject);
            yield return null;

            Assert.Throws<UIRootUnavailableException>(() =>
                runtime.LayerManager.GetLayer(UILayer.Normal));
            runtime.Dispose();
        }

        [UnityTest]
        public IEnumerator SortingLease_ReusesBoundedRangeAndBringToTopCompacts()
        {
            var runtime = UIRootRuntime.CreateOwned();
            var maxOrder = int.MinValue;
            for (var i = 0; i < 1000; i++)
            {
                var item = new GameObject($"Lease_{i}", typeof(RectTransform));
                var lease = runtime.LayerManager.AddToLayer(
                    UILayer.Normal,
                    item.GetComponent<RectTransform>());
                maxOrder = Math.Max(maxOrder, lease.SortingOrder);
                lease.Dispose();
                lease.Dispose();
                UnityEngine.Object.Destroy(item);
            }

            var first = new GameObject("First", typeof(RectTransform));
            var second = new GameObject("Second", typeof(RectTransform));
            var firstLease = runtime.LayerManager.AddToLayer(
                UILayer.Normal,
                first.GetComponent<RectTransform>());
            var secondLease = runtime.LayerManager.AddToLayer(
                UILayer.Normal,
                second.GetComponent<RectTransform>());
            runtime.LayerManager.BringToTop(firstLease);

            Assert.That(maxOrder, Is.EqualTo(2002));
            Assert.That(firstLease.SortingOrder, Is.GreaterThan(secondLease.SortingOrder));
            Assert.That(firstLease.SortingOrder, Is.LessThanOrEqualTo(2004));
            Assert.That(runtime.LayerManager.ActiveLeaseCount, Is.EqualTo(2));

            firstLease.Dispose();
            secondLease.Dispose();
            runtime.Dispose();
            UnityEngine.Object.Destroy(first);
            UnityEngine.Object.Destroy(second);
            yield return null;
        }

        [Test]
        public void SortingLease_ExhaustionThrowsWithoutWrapping()
        {
            var descriptors = new List<UILayerDescriptor>();
            foreach (var item in UILayerProfile.CreateDefault().Descriptors)
            {
                descriptors.Add(new UILayerDescriptor(
                    item.Layer,
                    item.SortingBase,
                    item.Layer == UILayer.Normal ? 2 : item.Capacity,
                    item.Interactable,
                    item.Modal));
            }

            var runtime = UIRootRuntime.CreateOwned(new UIRootRuntimeOptions
            {
                LayerProfile = new UILayerProfile(descriptors),
            });
            var first = new GameObject("CapacityFirst", typeof(RectTransform));
            var second = new GameObject("CapacitySecond", typeof(RectTransform));
            var overflow = new GameObject("CapacityOverflow", typeof(RectTransform));
            runtime.LayerManager.AddToLayer(UILayer.Normal, first.GetComponent<RectTransform>());
            runtime.LayerManager.AddToLayer(UILayer.Normal, second.GetComponent<RectTransform>());

            Assert.Throws<InvalidOperationException>(() =>
                runtime.LayerManager.AddToLayer(
                    UILayer.Normal,
                    overflow.GetComponent<RectTransform>()));
            Assert.That(runtime.LayerManager.ActiveLeaseCount, Is.EqualTo(2));

            runtime.Dispose();
            UnityEngine.Object.Destroy(first);
            UnityEngine.Object.Destroy(second);
            UnityEngine.Object.Destroy(overflow);
        }

        [Test]
        public void SortingLease_FailedCrossLayerTransferKeepsSourceLease()
        {
            var descriptors = new List<UILayerDescriptor>();
            foreach (var item in UILayerProfile.CreateDefault().Descriptors)
            {
                descriptors.Add(new UILayerDescriptor(
                    item.Layer,
                    item.SortingBase,
                    item.Layer == UILayer.Popup ? 2 : item.Capacity,
                    item.Interactable,
                    item.Modal));
            }

            var runtime = UIRootRuntime.CreateOwned(new UIRootRuntimeOptions
            {
                LayerProfile = new UILayerProfile(descriptors),
            });
            var source = new GameObject("Source", typeof(RectTransform));
            var first = new GameObject("PopupFirst", typeof(RectTransform));
            var second = new GameObject("PopupSecond", typeof(RectTransform));
            var sourceLease = runtime.LayerManager.AddToLayer(
                UILayer.Normal,
                source.GetComponent<RectTransform>());
            runtime.LayerManager.AddToLayer(UILayer.Popup, first.GetComponent<RectTransform>());
            runtime.LayerManager.AddToLayer(UILayer.Popup, second.GetComponent<RectTransform>());

            Assert.Throws<InvalidOperationException>(() =>
                runtime.LayerManager.AddToLayer(
                    UILayer.Popup,
                    source.GetComponent<RectTransform>()));
            Assert.That(sourceLease.IsDisposed, Is.False);
            Assert.That(
                source.transform.parent,
                Is.SameAs(runtime.Root.GetLayerRoot(UILayer.Normal)));
            Assert.That(runtime.LayerManager.GetActiveLeaseCount(UILayer.Normal), Is.EqualTo(1));

            runtime.Dispose();
            UnityEngine.Object.Destroy(source);
            UnityEngine.Object.Destroy(first);
            UnityEngine.Object.Destroy(second);
        }

        [UnityTest]
        public IEnumerator SortingLease_PoolAndFailureRollbackKeepCountsAndOrderConsistent()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<RollbackPage>(manager, UILayer.Normal);
            Register<PlainPage>(manager, UILayer.Normal, true);

            var firstTask = manager.OpenAsync<RollbackPage>().AsTask();
            yield return Await(firstTask);
            var secondTask = manager.OpenAsync<PlainPage>().AsTask();
            yield return Await(secondTask);
            var firstOrder = firstTask.Result.SortingLease.SortingOrder;
            var secondOrder = secondTask.Result.SortingLease.SortingOrder;

            var failedRefresh = manager.OpenAsync<RollbackPage>("fail").AsTask();
            yield return AwaitFailure(failedRefresh);
            Assert.That(firstTask.Result.SortingLease.SortingOrder, Is.EqualTo(firstOrder));
            Assert.That(secondTask.Result.SortingLease.SortingOrder, Is.EqualTo(secondOrder));

            yield return Await(manager.CloseAsync(secondTask.Result).AsTask());
            Assert.That(secondTask.Result.State, Is.EqualTo(UIContextState.Pooled));
            Assert.That(secondTask.Result.SortingLease, Is.Null);
            Assert.That(manager.LayerManager.ActiveLeaseCount, Is.EqualTo(1));

            yield return Await(manager.ShutdownAsync().AsTask());
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator NestedModal_OnlyTopReceivesRaycastsAndMaskIsReused()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<FirstDialog>(manager, UILayer.Popup);
            Register<SecondDialog>(manager, UILayer.Popup);

            var firstTask = manager.OpenAsync<FirstDialog>().AsTask();
            yield return Await(firstTask);
            var mask = manager.Modals.MaskObject;
            var secondTask = manager.OpenAsync<SecondDialog>().AsTask();
            yield return Await(secondTask);

            Assert.That(manager.Modals.Count, Is.EqualTo(2));
            Assert.That(manager.Modals.MaskObject, Is.SameAs(mask));
            Assert.That(firstTask.Result.View.GetComponent<GraphicRaycaster>().enabled, Is.False);
            Assert.That(secondTask.Result.View.GetComponent<GraphicRaycaster>().enabled, Is.True);
            Assert.That(
                mask.GetComponent<Canvas>().sortingOrder,
                Is.EqualTo(secondTask.Result.SortingLease.SortingOrder - 1));

            yield return Await(manager.CloseAsync(secondTask.Result).AsTask());
            Assert.That(firstTask.Result.View.GetComponent<GraphicRaycaster>().enabled, Is.True);
            Assert.That(manager.Modals.Count, Is.EqualTo(1));
            yield return Await(manager.CloseAsync(firstTask.Result).AsTask());
            Assert.That(mask.activeSelf, Is.False);

            yield return Await(manager.ShutdownAsync().AsTask());
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator Modal_PoolingFailureAndShutdownLeaveNoInteractionResidue()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<FirstDialog>(manager, UILayer.Popup, true);
            Register<FailingDialog>(manager, UILayer.Popup);

            var pooledTask = manager.OpenAsync<FirstDialog>().AsTask();
            yield return Await(pooledTask);
            yield return Await(manager.CloseAsync(pooledTask.Result).AsTask());
            Assert.That(manager.Modals.Count, Is.Zero);
            Assert.That(pooledTask.Result.SortingLease, Is.Null);
            Assert.That(manager.Modals.MaskObject.activeSelf, Is.False);

            var failedTask = manager.OpenAsync<FailingDialog>().AsTask();
            yield return AwaitFailure(failedTask);
            Assert.That(manager.Modals.Count, Is.Zero);
            Assert.That(manager.LayerManager.ActiveLeaseCount, Is.Zero);
            Assert.That(manager.Modals.MaskObject.activeSelf, Is.False);

            var reopenedTask = manager.OpenAsync<FirstDialog>().AsTask();
            yield return Await(reopenedTask);
            var mask = manager.Modals.MaskObject;
            yield return Await(manager.ShutdownAsync().AsTask());
            yield return null;
            Assert.That(mask == null, Is.True);
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator InputLocks_NestIntersectReleaseOutOfOrderAndReportShutdownLeak()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<FocusPage>(manager, UILayer.Normal);
            var pageTask = manager.OpenAsync<FocusPage>().AsTask();
            yield return Await(pageTask);
            var nestedCanvas = new GameObject(
                "NestedCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster));
            nestedCanvas.transform.SetParent(pageTask.Result.ViewObject.transform, false);

            var first = manager.InputLocks.Acquire(
                this,
                "first",
                UILayer.System,
                UILayer.Debug);
            var second = manager.InputLocks.Acquire(
                new object(),
                "second",
                UILayer.System);

            Assert.That(manager.InputLocks.ActiveLockCount, Is.EqualTo(2));
            Assert.That(manager.InputLocks.ActiveLocks, Has.Count.EqualTo(2));
            var observedFirstReason = false;
            foreach (var item in manager.InputLocks.ActiveLocks)
            {
                observedFirstReason |= item.Reason == "first";
            }
            Assert.That(observedFirstReason, Is.True);
            Assert.That(manager.InputLocks.IsLayerAllowed(UILayer.System), Is.True);
            Assert.That(manager.InputLocks.IsLayerAllowed(UILayer.Debug), Is.False);
            Assert.That(pageTask.Result.View.GetComponent<GraphicRaycaster>().enabled, Is.False);
            Assert.That(nestedCanvas.GetComponent<GraphicRaycaster>().enabled, Is.False);
            Assert.That(manager.RootRuntime.EventSystem.currentSelectedGameObject, Is.Null);
            first.Dispose();
            first.Dispose();
            Assert.That(manager.InputLocks.ActiveLockCount, Is.EqualTo(1));
            Assert.That(manager.InputLocks.IsLayerAllowed(UILayer.System), Is.True);
            second.Dispose();
            Assert.That(pageTask.Result.View.GetComponent<GraphicRaycaster>().enabled, Is.True);
            Assert.That(nestedCanvas.GetComponent<GraphicRaycaster>().enabled, Is.True);
            Assert.That(
                manager.RootRuntime.EventSystem.currentSelectedGameObject,
                Is.SameAs(pageTask.Result.DefaultFocus));

            var leaked = manager.InputLocks.Acquire(this, "leaked");

            yield return Await(manager.ShutdownAsync().AsTask());
            Assert.That(manager.LastShutdownInputLockLeakCount, Is.EqualTo(1));
            leaked.Dispose();
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator LockedTransitionView_IsNeverRaycastEligible()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            manager.Register<TransitionPage>(new UIConfig
            {
                Id = nameof(TransitionPage),
                PrefabKey = nameof(TransitionPage),
                Layer = UILayer.Normal,
                UseTransition = true,
                TransitionType = UITransitionType.Fade,
                ShowDuration = 10f,
            });
            using var inputLock = manager.InputLocks.Acquire(this, "transition");
            using var cancellation = new CancellationTokenSource();

            var openTask = manager.OpenAsync<TransitionPage>(
                cancellationToken: cancellation.Token).AsTask();
            GameObject instance = null;
            yield return new WaitUntil(() =>
            {
                instance = GameObject.Find($"Prefab_{nameof(TransitionPage)}(Clone)");
                return instance != null && instance.activeInHierarchy;
            });

            Assert.That(instance.GetComponent<GraphicRaycaster>().enabled, Is.False);
            cancellation.Cancel();
            while (!openTask.IsCompleted)
            {
                yield return null;
            }
            Assert.That(openTask.IsCanceled ||
                        openTask.Exception?.GetBaseException() is OperationCanceledException,
                Is.True);

            yield return Await(manager.ShutdownAsync().AsTask());
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator Focus_ModalCloseRestoresIdentityAndDestroyedTargetFallsBack()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<FocusPage>(manager, UILayer.Normal);
            Register<FirstDialog>(manager, UILayer.Popup);
            Register<SecondDialog>(manager, UILayer.Popup);

            var pageTask = manager.OpenAsync<FocusPage>().AsTask();
            yield return Await(pageTask);
            var pageFocus = pageTask.Result.DefaultFocus;
            Assert.That(manager.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(pageFocus));

            var dialogTask = manager.OpenAsync<FirstDialog>().AsTask();
            yield return Await(dialogTask);
            Assert.That(
                manager.RootRuntime.EventSystem.currentSelectedGameObject,
                Is.SameAs(dialogTask.Result.DefaultFocus));

            yield return Await(manager.CloseAsync(dialogTask.Result).AsTask());
            Assert.That(manager.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(pageFocus));

            var lowerDialogTask = manager.OpenAsync<FirstDialog>().AsTask();
            yield return Await(lowerDialogTask);
            var topDialogTask = manager.OpenAsync<SecondDialog>().AsTask();
            yield return Await(topDialogTask);
            yield return Await(manager.CloseAsync(lowerDialogTask.Result).AsTask());
            Assert.That(
                manager.RootRuntime.EventSystem.currentSelectedGameObject,
                Is.SameAs(topDialogTask.Result.DefaultFocus));
            yield return Await(manager.CloseAsync(topDialogTask.Result).AsTask());

            var secondDialogTask = manager.OpenAsync<FirstDialog>().AsTask();
            yield return Await(secondDialogTask);
            UnityEngine.Object.Destroy(pageFocus);
            yield return null;
            yield return Await(manager.CloseAsync(secondDialogTask.Result).AsTask());
            Assert.That(manager.RootRuntime.EventSystem.currentSelectedGameObject, Is.Null);

            yield return Await(manager.ShutdownAsync().AsTask());
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator Shutdown_IsSingleFlightForConcurrentCallers()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<FocusPage>(manager, UILayer.Normal);
            yield return Await(manager.OpenAsync<FocusPage>().AsTask());

            var first = manager.ShutdownAsync().AsTask();
            var second = manager.ShutdownAsync().AsTask();
            yield return Await(first);
            yield return Await(second);

            Assert.That(manager.IsInitialized, Is.False);
            Assert.That(manager.RootRuntime, Is.Null);
            loader.Dispose();
        }

        [UnityTest]
        public IEnumerator BackRouter_DeduplicatesRespectsBusyGuardAndInputLock()
        {
            var loader = new StageFourLoader();
            var manager = new UIManager();
            manager.Initialize(loader);
            Register<FocusPage>(manager, UILayer.Normal);
            Register<SecondPage>(manager, UILayer.Normal);
            yield return Await(manager.Navigator.PushAsync<FocusPage>().AsTask());
            yield return Await(manager.Navigator.PushAsync<SecondPage>().AsTask());

            var guard = new UniTaskCompletionSource<bool>();
            manager.Navigator.Guard = (_, _) => guard.Task;
            Assert.That(manager.Input.RequestBack(), Is.True);
            Assert.That(manager.Input.RequestBack(), Is.False);
            Assert.That(manager.Navigator.IsBusy, Is.True);
            guard.TrySetResult(false);
            yield return new WaitUntil(() => !manager.Input.IsBackInFlight);
            Assert.That(manager.Navigator.Count, Is.EqualTo(2));

            using (manager.InputLocks.Acquire(this, "locked"))
            {
                Assert.That(manager.Input.RequestBack(), Is.False);
            }

            manager.Navigator.Guard = (_, _) => UniTask.FromResult(true);
            Assert.That(manager.Input.RequestBack(), Is.True);
            yield return new WaitUntil(() => !manager.Input.IsBackInFlight);
            Assert.That(manager.Navigator.Count, Is.EqualTo(1));

            yield return Await(manager.ShutdownAsync().AsTask());
            loader.Dispose();
        }

        private static GameObject CreateRootObject(string name)
        {
            return new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(UIRoot));
        }

        private static GameObject CreateEventSystemObject(string name)
        {
            return new GameObject(
                name,
                typeof(EventSystem),
                typeof(StandaloneInputModule));
        }

        private static void Register<T>(
            UIManager manager,
            UILayer layer,
            bool cache = false)
            where T : BaseContext
        {
            manager.Register<T>(new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = typeof(T).Name,
                Layer = layer,
                CacheOnClose = cache,
                MaxPoolSize = cache ? 1 : 0,
            });
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsCanceled)
            {
                throw new TaskCanceledException(task);
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ??
                      new InvalidOperationException("Task failed.");
            }
        }

        private static IEnumerator AwaitFailure(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.That(task.IsFaulted, Is.True);
        }

        public sealed class RollbackPage : BasePageContext
        {
            protected override void HandleShow(object args)
            {
                if (Equals(args, "fail"))
                {
                    throw new InvalidOperationException("Expected refresh failure.");
                }
            }
        }

        public class PlainPage : BasePageContext
        {
        }

        public sealed class FocusPage : FocusContext
        {
        }

        public sealed class SecondPage : FocusContext
        {
        }

        public sealed class TransitionPage : FocusContext
        {
        }

        public sealed class FirstDialog : FocusDialogContext
        {
        }

        public sealed class SecondDialog : FocusDialogContext
        {
        }

        public sealed class FailingDialog : FocusDialogContext
        {
            protected override void HandleShow(object args)
            {
                throw new InvalidOperationException("Expected modal failure.");
            }
        }

        public abstract class FocusContext : BasePageContext
        {
            public override GameObject DefaultFocus =>
                ViewObject == null ? null : ViewObject.transform.Find("DefaultFocus")?.gameObject;
        }

        public abstract class FocusDialogContext : BaseDialogContext
        {
            public override GameObject DefaultFocus =>
                ViewObject == null ? null : ViewObject.transform.Find("DefaultFocus")?.gameObject;
        }

        private sealed class StageFourLoader : IResourceLoader, IDisposable
        {
            private readonly Dictionary<string, GameObject> _prefabs =
                new Dictionary<string, GameObject>();

            public UniTask<GameObject> LoadPrefabAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_prefabs.TryGetValue(key, out var prefab))
                {
                    prefab = new GameObject(
                        $"Prefab_{key}",
                        typeof(RectTransform),
                        typeof(UIView));
                    var focus = new GameObject(
                        "DefaultFocus",
                        typeof(RectTransform),
                        typeof(Image),
                        typeof(Button));
                    focus.transform.SetParent(prefab.transform, false);
                    prefab.SetActive(false);
                    prefab.hideFlags = HideFlags.DontSave;
                    _prefabs.Add(key, prefab);
                }

                return UniTask.FromResult(prefab);
            }

            public void Release(string key, GameObject instance)
            {
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            public void Dispose()
            {
                foreach (var prefab in _prefabs.Values)
                {
                    if (prefab != null)
                    {
                        UnityEngine.Object.Destroy(prefab);
                    }
                }

                _prefabs.Clear();
            }
        }
    }
}
