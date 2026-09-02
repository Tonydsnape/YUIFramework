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
    public sealed class UIManagerCharacterizationTests
    {
        private CharacterizationResourceLoader _loader;
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

            _loader = new CharacterizationResourceLoader();
            _manager = new UIManager();
            _manager.Initialize(_loader, new UIObjectPool());
            _manager.Navigator.Clear();
            _manager.MessageCenter.Clear();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return CloseIfOpen<TestPageA>();
            yield return CloseIfOpen<TestPageB>();
            yield return CloseIfOpen<TestPageC>();
            yield return CloseIfOpen<MessagingPage>();
            yield return CloseIfOpen<StateTrackingPage>();
            yield return CloseIfOpen<FailingRefreshPage>();
            yield return CloseIfOpen<FailingHidePage>();
            yield return CloseIfOpen<FailingDestroyPage>();

            if (_manager != null && _manager.IsInitialized)
            {
                yield return Await(_manager.ShutdownAsync().AsTask());
            }

            _loader?.Dispose();
            if (_rootObject != null)
            {
                UnityEngine.Object.Destroy(_rootObject);
            }

            foreach (var eventSystem in UnityEngine.Object.FindObjectsOfType<EventSystem>())
            {
                UnityEngine.Object.Destroy(eventSystem.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RegisterOpenClose_DrivesExpectedLifecycle()
        {
            Register<TestPageA>("Tests/OpenClose", false);

            var openTask = _manager.OpenAsync<TestPageA>("first").AsTask();
            yield return Await(openTask);
            var page = openTask.Result;

            Assert.That(page.InitCount, Is.EqualTo(1));
            Assert.That(page.ShowCount, Is.EqualTo(1));
            Assert.That(page.LastArgs, Is.EqualTo("first"));
            Assert.That(page.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(_manager.IsOpen<TestPageA>(), Is.True);

            var closeTask = _manager.CloseAsync(page).AsTask();
            yield return Await(closeTask);

            Assert.That(page.HideCount, Is.EqualTo(1));
            Assert.That(page.CloseCount, Is.EqualTo(1));
            Assert.That(page.DestroyCount, Is.EqualTo(1));
            Assert.That(page.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_loader.ReleaseCount, Is.EqualTo(1));
            Assert.That(_manager.Get<TestPageA>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator ReopenActivePage_ReusesContextAndRunsShowAgain()
        {
            Register<TestPageA>("Tests/Reopen", false);

            var firstTask = _manager.OpenAsync<TestPageA>("first").AsTask();
            yield return Await(firstTask);
            var secondTask = _manager.OpenAsync<TestPageA>("second").AsTask();
            yield return Await(secondTask);

            Assert.That(secondTask.Result, Is.SameAs(firstTask.Result));
            Assert.That(secondTask.Result.InitCount, Is.EqualTo(1));
            Assert.That(secondTask.Result.ShowCount, Is.EqualTo(2));
            Assert.That(secondTask.Result.LastArgs, Is.EqualTo("second"));
            Assert.That(_loader.LoadCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CloseCachedPage_StoresAndRestoresSameContext()
        {
            Register<TestPageA>("Tests/Pool", true);

            var firstTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            var first = firstTask.Result;
            yield return Await(_manager.CloseAsync(first).AsTask());

            Assert.That(first.State, Is.EqualTo(UIContextState.Pooled));
            Assert.That(first.ViewObject.activeSelf, Is.False);
            Assert.That(_loader.ReleaseCount, Is.Zero);

            var secondTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(secondTask);

            Assert.That(secondTask.Result, Is.SameAs(first));
            Assert.That(secondTask.Result.InitCount, Is.EqualTo(1));
            Assert.That(secondTask.Result.ShowCount, Is.EqualTo(2));
            Assert.That(_loader.LoadCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator NavigatorPushAndBack_RestoresPreviousPage()
        {
            Register<TestPageA>("Tests/NavA", false);
            Register<TestPageB>("Tests/NavB", false);

            var firstTask = _manager.Navigator.PushAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            var secondTask = _manager.Navigator.PushAsync<TestPageB>().AsTask();
            yield return Await(secondTask);

            Assert.That(_manager.Navigator.Count, Is.EqualTo(2));
            Assert.That(_manager.Navigator.CurrentPage, Is.SameAs(secondTask.Result));
            Assert.That(firstTask.Result.State, Is.EqualTo(UIContextState.Hidden));
            Assert.That(firstTask.Result.ViewObject.activeSelf, Is.False);

            var backTask = _manager.Navigator.BackAsync().AsTask();
            yield return Await(backTask);

            Assert.That(backTask.Result, Is.True);
            Assert.That(_manager.Navigator.Count, Is.EqualTo(1));
            Assert.That(_manager.Navigator.CurrentPage, Is.SameAs(firstTask.Result));
            Assert.That(firstTask.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(firstTask.Result.ViewObject.activeSelf, Is.True);
            Assert.That(secondTask.Result.State, Is.EqualTo(UIContextState.Released));
        }

        [UnityTest]
        public IEnumerator NavigatorReplace_ClosesCurrentAndKeepsOneEntry()
        {
            Register<TestPageA>("Tests/ReplaceA", false);
            Register<TestPageC>("Tests/ReplaceC", false);

            var firstTask = _manager.Navigator.PushAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            var replaceTask = _manager.Navigator.ReplaceAsync<TestPageC>().AsTask();
            yield return Await(replaceTask);

            Assert.That(firstTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_manager.Navigator.Count, Is.EqualTo(1));
            Assert.That(_manager.Navigator.CurrentPage, Is.SameAs(replaceTask.Result));
        }

        [UnityTest]
        public IEnumerator NavigatorPushExistingPage_BringsItBackToTop()
        {
            Register<TestPageA>("Tests/BringA", false);
            Register<TestPageB>("Tests/BringB", false);

            var firstTask = _manager.Navigator.PushAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            var secondTask = _manager.Navigator.PushAsync<TestPageB>().AsTask();
            yield return Await(secondTask);
            var bringTask = _manager.Navigator.PushAsync<TestPageA>(
                "restored",
                new UINavigateOptions { BringExistingPageToTop = true }).AsTask();
            yield return Await(bringTask);

            Assert.That(bringTask.Result, Is.SameAs(firstTask.Result));
            Assert.That(bringTask.Result.LastArgs, Is.EqualTo("restored"));
            Assert.That(secondTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_manager.Navigator.Count, Is.EqualTo(1));
            Assert.That(_manager.Navigator.CurrentPage, Is.SameAs(firstTask.Result));
        }

        [Test]
        public void Register_RejectsMissingId()
        {
            Assert.Throws<ArgumentException>(() => _manager.Register<TestPageA>(new UIConfig
            {
                PrefabKey = "Tests/Invalid"
            }));
        }

        [Test]
        public void Register_RejectsMissingPrefabKey()
        {
            Assert.Throws<ArgumentException>(() => _manager.Register<TestPageA>(new UIConfig
            {
                Id = "Invalid"
            }));
        }

        [Test]
        public void Initialize_WhenAlreadyInitialized_IsRejected()
        {
            Assert.Throws<InvalidOperationException>(() =>
                _manager.Initialize(_loader, new UIObjectPool()));
        }

        [Test]
        public void InitializeAsync_WhenCanceled_DoesNotInitialize()
        {
            var service = new UIManager();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                service.InitializeAsync(
                    new CharacterizationResourceLoader(),
                    cancellationToken: cancellation.Token));
            Assert.That(service.IsInitialized, Is.False);
        }

        [UnityTest]
        public IEnumerator CloseGeneric_WhenUninitialized_IsRejected()
        {
            var uninitializedService = new UIManager();
            Assert.Throws<InvalidOperationException>(() =>
                uninitializedService.CloseAsync<TestPageA>());

            yield return Await(_manager.ShutdownAsync().AsTask());

            Assert.Throws<InvalidOperationException>(() =>
                _manager.CloseAsync<TestPageA>());
        }

        [UnityTest]
        public IEnumerator OpenHandle_ClosesThroughOwningService()
        {
            Register<TestPageA>("Tests/Handle", false);

            var openTask = _manager.OpenHandleAsync<TestPageA>().AsTask();
            yield return Await(openTask);
            var handle = openTask.Result;

            Assert.That(handle.Key, Is.EqualTo(new UIKey(nameof(TestPageA))));
            Assert.That(handle.IsOpen, Is.True);

            yield return Await(handle.CloseAsync().AsTask());

            Assert.That(handle.IsOpen, Is.False);
            Assert.That(handle.Context.State, Is.EqualTo(UIContextState.Released));
        }

        [UnityTest]
        public IEnumerator StaleHandle_DoesNotCloseNewContextOfSameType()
        {
            Register<TestPageA>("Tests/StaleHandle", false);

            var firstHandleTask = _manager.OpenHandleAsync<TestPageA>().AsTask();
            yield return Await(firstHandleTask);
            var staleHandle = firstHandleTask.Result;
            yield return Await(staleHandle.CloseAsync().AsTask());

            var currentHandleTask = _manager.OpenHandleAsync<TestPageA>().AsTask();
            yield return Await(currentHandleTask);
            yield return Await(staleHandle.CloseAsync().AsTask());

            Assert.That(staleHandle.IsOpen, Is.False);
            Assert.That(currentHandleTask.Result.IsOpen, Is.True);
            Assert.That(
                _manager.Get<TestPageA>(),
                Is.SameAs(currentHandleTask.Result.Context));
        }

        [UnityTest]
        public IEnumerator ContextMessaging_UsesOwningInjectedService()
        {
            Register<MessagingPage>("Tests/Messaging", false);

            var openTask = _manager.OpenAsync<MessagingPage>().AsTask();
            yield return Await(openTask);
            _manager.MessageCenter.Publish("tests.value", 42);

            Assert.That(openTask.Result.ReceivedValue, Is.EqualTo(42));
            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LegacyInit_ForwardsToTheSameRuntimeImplementation()
        {
            yield return Await(_manager.ShutdownAsync().AsTask());
            var legacyService = new UIManager();
            var legacyLoader = new CharacterizationResourceLoader();
#pragma warning disable CS0618
            legacyService.Init(legacyLoader, new UIObjectPool());
#pragma warning restore CS0618

            Assert.That(legacyService.IsInitialized, Is.True);
            Assert.That(legacyService.Navigator, Is.Not.Null);
            Assert.That(legacyService.MessageCenter, Is.Not.Null);

            yield return Await(legacyService.ShutdownAsync().AsTask());
            legacyLoader.Dispose();
        }

        [UnityTest]
        public IEnumerator Shutdown_ReleasesContextsPoolsAndRegistry()
        {
            Register<TestPageA>("Tests/ShutdownA", false);
            Register<TestPageB>("Tests/ShutdownB", true);

            var activeTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(activeTask);
            var pooledTask = _manager.OpenAsync<TestPageB>().AsTask();
            yield return Await(pooledTask);
            yield return Await(_manager.CloseAsync(pooledTask.Result).AsTask());

            yield return Await(_manager.ShutdownAsync().AsTask());

            Assert.That(_manager.IsInitialized, Is.False);
            Assert.That(_manager.IsRegistered<TestPageA>(), Is.False);
            Assert.That(_manager.IsRegistered<TestPageB>(), Is.False);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(2));

            _manager.Initialize(_loader, new UIObjectPool());
            Assert.That(_manager.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator CancelNewOpenDuringTransition_ReleasesInstanceAndResource()
        {
            var config = Register<TestPageA>("Tests/CancelNew", false);
            config.UseTransition = true;
            config.TransitionType = UITransitionType.Fade;
            config.ShowDuration = 10f;
            using var cancellation = new CancellationTokenSource();

            var openTask = _manager.OpenAsync<TestPageA>(
                cancellationToken: cancellation.Token).AsTask();
            yield return null;
            cancellation.Cancel();
            yield return AwaitCancellation(openTask);

            Assert.That(IsCancellation(openTask), Is.True);
            Assert.That(_manager.Get<TestPageA>(), Is.Null);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CancelPooledOpenDuringTransition_ReturnsContextToPool()
        {
            var config = Register<TestPageA>("Tests/CancelPool", true);
            config.UseTransition = true;
            config.TransitionType = UITransitionType.Fade;
            config.ShowDuration = 0f;

            var firstTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            var original = firstTask.Result;
            yield return Await(_manager.CloseAsync(original).AsTask());

            config.ShowDuration = 10f;
            using var cancellation = new CancellationTokenSource();
            var canceledTask = _manager.OpenAsync<TestPageA>(
                cancellationToken: cancellation.Token).AsTask();
            yield return null;
            cancellation.Cancel();
            yield return AwaitCancellation(canceledTask);

            config.ShowDuration = 0f;
            var restoredTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(restoredTask);

            Assert.That(restoredTask.Result, Is.SameAs(original));
            Assert.That(_loader.LoadCount, Is.EqualTo(1));
            Assert.That(_loader.ReleaseCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CancelCloseDuringTransition_KeepsContextReachableAndVisible()
        {
            var config = Register<TestPageA>("Tests/CancelClose", false);
            config.UseTransition = true;
            config.TransitionType = UITransitionType.Fade;
            config.ShowDuration = 0f;
            config.HideDuration = 10f;

            var openTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(openTask);
            using var cancellation = new CancellationTokenSource();
            var closeTask = _manager.CloseAsync(
                openTask.Result,
                cancellation.Token).AsTask();
            yield return null;
            cancellation.Cancel();
            yield return AwaitCancellation(closeTask);

            Assert.That(IsCancellation(closeTask), Is.True);
            Assert.That(_manager.Get<TestPageA>(), Is.SameAs(openTask.Result));
            Assert.That(_manager.IsOpen<TestPageA>(), Is.True);
            Assert.That(
                openTask.Result.ViewObject.GetComponent<CanvasGroup>().alpha,
                Is.EqualTo(1f));

            config.HideDuration = 0f;
        }

        [UnityTest]
        public IEnumerator LifecycleStateMachine_DrivesCompleteReleaseSequence()
        {
            Register<StateTrackingPage>("Tests/StateSequence", false);

            var openTask = _manager.OpenAsync<StateTrackingPage>().AsTask();
            yield return Await(openTask);
            var page = openTask.Result;
            var lifetimeToken = page.LifetimeToken;
            yield return Await(_manager.CloseAsync(page).AsTask());

            CollectionAssert.AreEqual(
                new[]
                {
                    UIContextState.Loading,
                    UIContextState.Initializing,
                    UIContextState.Opening,
                    UIContextState.Opened,
                    UIContextState.Hiding,
                    UIContextState.Hidden,
                    UIContextState.Closing,
                    UIContextState.Releasing,
                    UIContextState.Released
                },
                page.StateHistory);
            Assert.That(page.InitState, Is.EqualTo(UIContextState.Initializing));
            Assert.That(page.ShowState, Is.EqualTo(UIContextState.Opening));
            Assert.That(page.HideState, Is.EqualTo(UIContextState.Hiding));
            Assert.That(page.CloseState, Is.EqualTo(UIContextState.Closing));
            Assert.That(page.DestroyState, Is.EqualTo(UIContextState.Releasing));
            Assert.That(page.OpenOperationId.IsValid, Is.True);
            Assert.That(page.CloseOperationId.IsValid, Is.True);
            Assert.That(page.OpenOperationId, Is.Not.EqualTo(page.CloseOperationId));
            Assert.That(page.CloseDispositionAtClose, Is.EqualTo(UICloseDisposition.Release));
            Assert.That(lifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(page.CurrentOperationId.IsValid, Is.False);
        }

        [UnityTest]
        public IEnumerator PooledLifecycle_KeepsLifetimeAndReentersOpening()
        {
            Register<StateTrackingPage>("Tests/PooledSequence", true);

            var openTask = _manager.OpenAsync<StateTrackingPage>().AsTask();
            yield return Await(openTask);
            var page = openTask.Result;
            var lifetimeToken = page.LifetimeToken;
            yield return Await(_manager.CloseAsync(page).AsTask());

            Assert.That(page.State, Is.EqualTo(UIContextState.Pooled));
            Assert.That(lifetimeToken.IsCancellationRequested, Is.False);
            Assert.That(page.CloseDispositionAtClose, Is.EqualTo(UICloseDisposition.Pool));

            var reopenTask = _manager.OpenAsync<StateTrackingPage>().AsTask();
            yield return Await(reopenTask);

            Assert.That(reopenTask.Result, Is.SameAs(page));
            Assert.That(page.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(page.InitCount, Is.EqualTo(1));
            Assert.That(page.ShowCount, Is.EqualTo(2));
            CollectionAssert.AreEqual(
                new[] { UIContextState.Pooled, UIContextState.Opening, UIContextState.Opened },
                page.StateHistory.GetRange(page.StateHistory.Count - 3, 3));
        }

        [UnityTest]
        public IEnumerator InitFailure_RecordsFailureAndReleasesContext()
        {
            FailingInitPage.LastCreated = null;
            Register<FailingInitPage>("Tests/FailInit", false);

            var openTask = _manager.OpenAsync<FailingInitPage>().AsTask();
            yield return AwaitFailure(openTask);

            var page = FailingInitPage.LastCreated;
            Assert.That(GetFailure(openTask), Is.TypeOf<UILifecycleException>());
            Assert.That(page, Is.Not.Null);
            Assert.That(page.State, Is.EqualTo(UIContextState.Released));
            Assert.That(page.LastFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(page.LifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(1));
            Assert.That(_manager.Get<FailingInitPage>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator ExistingRefreshFailure_RollsBackToOpenedState()
        {
            Register<FailingRefreshPage>("Tests/FailRefresh", false);

            var firstTask = _manager.OpenAsync<FailingRefreshPage>().AsTask();
            yield return Await(firstTask);
            var refreshTask = _manager.OpenAsync<FailingRefreshPage>().AsTask();
            yield return AwaitFailure(refreshTask);

            Assert.That(GetFailure(refreshTask), Is.TypeOf<UILifecycleException>());
            Assert.That(firstTask.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(firstTask.Result.LastFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(_manager.Get<FailingRefreshPage>(), Is.SameAs(firstTask.Result));
            Assert.That(_manager.IsOpen<FailingRefreshPage>(), Is.True);
            Assert.That(_loader.ReleaseCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HideFailure_FaultsAndReleasesContext()
        {
            FailingHidePage.LastCreated = null;
            Register<FailingHidePage>("Tests/FailHide", false);

            var openTask = _manager.OpenAsync<FailingHidePage>().AsTask();
            yield return Await(openTask);
            var closeTask = _manager.CloseAsync(openTask.Result).AsTask();
            yield return AwaitFailure(closeTask);

            Assert.That(GetFailure(closeTask), Is.TypeOf<UILifecycleException>());
            Assert.That(openTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(openTask.Result.LastFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(openTask.Result.LifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(_manager.Get<FailingHidePage>(), Is.Null);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator OperationInProgress_PublicCloseWaitsForCurrentOperation()
        {
            var config = Register<TestPageA>("Tests/OperationConflict", false);
            config.UseTransition = true;
            config.TransitionType = UITransitionType.Fade;
            config.ShowDuration = 0f;

            var firstTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            config.ShowDuration = 10f;
            using var cancellation = new CancellationTokenSource();
            var refreshTask = _manager.OpenAsync<TestPageA>(
                cancellationToken: cancellation.Token).AsTask();
            yield return null;

            var conflictingClose = _manager.CloseAsync(firstTask.Result).AsTask();
            yield return null;
            Assert.That(conflictingClose.IsCompleted, Is.False);
            Assert.That(firstTask.Result.CurrentOperationKind, Is.EqualTo(UIOperationKind.Open));

            cancellation.Cancel();
            yield return AwaitCancellation(refreshTask);
            yield return Await(conflictingClose);
            Assert.That(firstTask.Result.CurrentOperationId.IsValid, Is.False);
            Assert.That(firstTask.Result.State, Is.EqualTo(UIContextState.Released));
            config.ShowDuration = 0f;
        }

        [UnityTest]
        public IEnumerator DestroyFailure_IsRecordedAfterContextReachesReleased()
        {
            Register<FailingDestroyPage>("Tests/FailDestroy", false);

            var openTask = _manager.OpenAsync<FailingDestroyPage>().AsTask();
            yield return Await(openTask);
            var closeTask = _manager.CloseAsync(openTask.Result).AsTask();
            yield return AwaitFailure(closeTask);

            Assert.That(GetFailure(closeTask), Is.TypeOf<UILifecycleException>());
            Assert.That(openTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(openTask.Result.LastFailure, Is.TypeOf<AggregateException>());
            Assert.That(openTask.Result.LifetimeToken.IsCancellationRequested, Is.True);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClearAllPools_ContinuesAfterOneContextDestroyFails()
        {
            Register<FailingDestroyPage>("Tests/PoolFailDestroy", true);
            Register<TestPageA>("Tests/PoolHealthy", true);

            var failingTask = _manager.OpenAsync<FailingDestroyPage>().AsTask();
            yield return Await(failingTask);
            yield return Await(_manager.CloseAsync(failingTask.Result).AsTask());
            var healthyTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(healthyTask);
            yield return Await(_manager.CloseAsync(healthyTask.Result).AsTask());

            Assert.Throws<AggregateException>(() => _manager.ClearAllPools());

            Assert.That(failingTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(healthyTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(failingTask.Result.LastFailure, Is.Not.Null);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Shutdown_CancelsAndWaitsForInFlightOpen()
        {
            yield return Await(_manager.ShutdownAsync().AsTask());
            var delayedLoader = new DelayedResourceLoader();
            _manager.Initialize(delayedLoader, new UIObjectPool());
            Register<TestPageA>("Tests/DelayedOpen", false);

            var openTask = _manager.OpenAsync<TestPageA>().AsTask();
            while (!delayedLoader.Started)
            {
                yield return null;
            }

            var shutdownTask = _manager.ShutdownAsync().AsTask();
            yield return Await(shutdownTask);
            yield return AwaitCancellation(openTask);

            Assert.That(_manager.IsInitialized, Is.False);
            Assert.That(_manager.Get<TestPageA>(), Is.Null);
            Assert.That(delayedLoader.CancellationObserved, Is.True);
        }

        private UIConfig Register<T>(string key, bool cacheOnClose) where T : BaseContext
        {
            var config = new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = key,
                Layer = UILayer.Normal,
                CacheOnClose = cacheOnClose,
                MaxPoolSize = cacheOnClose ? 1 : 0,
                FullScreen = true
            };
            _manager.Register<T>(config);
            return config;
        }

        private IEnumerator CloseIfOpen<T>() where T : BaseContext
        {
            var context = _manager.Get<T>();
            if (context != null)
            {
                yield return Await(_manager.CloseAsync(context).AsTask());
            }
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
                throw task.Exception?.GetBaseException() ?? new InvalidOperationException("Task failed.");
            }
        }

        private static IEnumerator AwaitCancellation(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (IsCancellation(task))
            {
                yield break;
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ??
                      new InvalidOperationException("Task failed.");
            }

            Assert.Fail("Expected the operation to be canceled.");
        }

        private static IEnumerator AwaitFailure(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (!task.IsFaulted)
            {
                Assert.Fail("Expected the operation to fail.");
            }
        }

        private static Exception GetFailure(Task task)
        {
            return task.Exception?.GetBaseException();
        }

        private static bool IsCancellation(Task task)
        {
            return task.IsCanceled ||
                   task.Exception?.GetBaseException() is OperationCanceledException;
        }

        public class TestPageA : TrackedPageContext
        {
        }

        public class TestPageB : TrackedPageContext
        {
        }

        public class TestPageC : TrackedPageContext
        {
        }

        public class MessagingPage : TrackedPageContext
        {
            public int ReceivedValue { get; private set; }

            protected override void HandleInit()
            {
                base.HandleInit();
                SubscribeMessage<int>("tests.value", value => ReceivedValue = value);
            }
        }

        public class StateTrackingPage : TrackedPageContext
        {
            public StateTrackingPage()
            {
                StateChanged += (_, next) => StateHistory.Add(next);
            }

            public List<UIContextState> StateHistory { get; } = new List<UIContextState>();
        }

        public class FailingInitPage : TrackedPageContext
        {
            public FailingInitPage()
            {
                LastCreated = this;
            }

            public static FailingInitPage LastCreated { get; set; }

            protected override void HandleInit()
            {
                base.HandleInit();
                throw new InvalidOperationException("Expected init failure.");
            }
        }

        public class FailingRefreshPage : TrackedPageContext
        {
            protected override void HandleShow(object args)
            {
                base.HandleShow(args);
                if (ShowCount > 1)
                {
                    throw new InvalidOperationException("Expected refresh failure.");
                }
            }
        }

        public class FailingHidePage : TrackedPageContext
        {
            public FailingHidePage()
            {
                LastCreated = this;
            }

            public static FailingHidePage LastCreated { get; set; }

            protected override void HandleHide()
            {
                base.HandleHide();
                throw new InvalidOperationException("Expected hide failure.");
            }
        }

        public class FailingDestroyPage : TrackedPageContext
        {
            protected override void HandleDestroy()
            {
                base.HandleDestroy();
                throw new InvalidOperationException("Expected destroy failure.");
            }
        }

        public abstract class TrackedPageContext : BasePageContext
        {
            public int InitCount { get; private set; }
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public int CloseCount { get; private set; }
            public int DestroyCount { get; private set; }
            public object LastArgs { get; private set; }
            public UIContextState InitState { get; private set; }
            public UIContextState ShowState { get; private set; }
            public UIContextState HideState { get; private set; }
            public UIContextState CloseState { get; private set; }
            public UIContextState DestroyState { get; private set; }
            public UIOperationId OpenOperationId { get; private set; }
            public UIOperationId CloseOperationId { get; private set; }
            public UICloseDisposition CloseDispositionAtClose { get; private set; }

            protected override void HandleInit()
            {
                InitCount++;
                InitState = State;
                OpenOperationId = CurrentOperationId;
            }

            protected override void HandleShow(object args)
            {
                ShowCount++;
                LastArgs = args;
                ShowState = State;
                OpenOperationId = CurrentOperationId;
            }

            protected override void HandleHide()
            {
                HideCount++;
                HideState = State;
                CloseOperationId = CurrentOperationId;
            }

            protected override void HandleClose()
            {
                CloseCount++;
                CloseState = State;
                CloseOperationId = CurrentOperationId;
                CloseDispositionAtClose = CloseDisposition;
            }

            protected override void HandleDestroy()
            {
                DestroyCount++;
                DestroyState = State;
                if (!CloseOperationId.IsValid)
                {
                    CloseOperationId = CurrentOperationId;
                }
            }
        }

        private sealed class CharacterizationResourceLoader : IResourceLoader, IDisposable
        {
            private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

            public int LoadCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public UniTask<GameObject> LoadPrefabAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoadCount++;
                if (!_prefabs.TryGetValue(key, out var prefab) || prefab == null)
                {
                    prefab = new GameObject(
                        $"TestPrefab_{key}",
                        typeof(RectTransform),
                        typeof(UIView));
                    prefab.SetActive(false);
                    prefab.hideFlags = HideFlags.DontSave;
                    _prefabs[key] = prefab;
                }

                return UniTask.FromResult(prefab);
            }

            public void Release(string key, GameObject instance)
            {
                ReleaseCount++;
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

        private sealed class DelayedResourceLoader : IResourceLoader
        {
            public bool Started { get; private set; }
            public bool CancellationObserved { get; private set; }

            public async UniTask<GameObject> LoadPrefabAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                Started = true;
                try
                {
                    await UniTask.WaitUntil(
                        () => false,
                        cancellationToken: cancellationToken);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            public void Release(string key, GameObject instance)
            {
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }
        }
    }
}
