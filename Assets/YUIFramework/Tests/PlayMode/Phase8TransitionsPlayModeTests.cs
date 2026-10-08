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
    public sealed class Phase8TransitionsPlayModeTests
    {
        private TransitionLoader _loader;
        private UIManager _manager;
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject(
                "Phase8Root",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _root.AddComponent<UIRoot>();
            _loader = new TransitionLoader();
            _manager = new UIManager();
            _manager.Initialize(_loader, new UIObjectPool());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_manager != null && _manager.IsInitialized)
            {
                yield return Await(_manager.ShutdownAsync().AsTask());
            }

            _loader?.Dispose();
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
        public IEnumerator ReverseClose_ContinuesToBaselineAndRollsBackOpenedState()
        {
            Register<TransitionPageA>(UITransitionType.Scale, false, 0f, 0.25f);
            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return Await(open);
            var page = open.Result;
            var baseline = page.View.RectTransform.localScale;

            var close = _manager.CloseAsync(page).AsTask();
            yield return null;
            Assert.That(
                _manager.RequestTransitionInterruption(
                    page,
                    UITransitionInterruption.Reverse),
                Is.True);
            yield return AwaitCancellation(close);

            Assert.That(page.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(page.ViewObject.activeInHierarchy, Is.True);
            Assert.That(page.View.RectTransform.localScale, Is.EqualTo(baseline));
            Assert.That(page.IsVisible, Is.True);
            Assert.That(page.CloseDisposition, Is.EqualTo(UICloseDisposition.None));
        }

        [UnityTest]
        public IEnumerator SkipClose_CompletesCurrentLifecycleOperation()
        {
            Register<TransitionPageA>(UITransitionType.Fade, false, 0f, 10f);
            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return Await(open);

            var close = _manager.CloseAsync(open.Result).AsTask();
            yield return null;
            Assert.That(
                _manager.RequestTransitionInterruption<TransitionPageA>(
                    UITransitionInterruption.SkipToEnd),
                Is.True);
            yield return Await(close);

            Assert.That(open.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_manager.Get<TransitionPageA>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator InterruptNewOpen_UsesTransitionGenerationAndReleasesInstance()
        {
            Register<TransitionPageA>(UITransitionType.SlideRight, false, 10f, 0f);
            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return null;

            Assert.That(
                _manager.RequestTransitionInterruption<TransitionPageA>(
                    UITransitionInterruption.Interrupt),
                Is.True);
            yield return AwaitCancellation(open);

            Assert.That(_manager.Get<TransitionPageA>(), Is.Null);
            Assert.That(_loader.ReleaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TimeScaleZero_UnscaledTransitionStillCompletes()
        {
            Register<TransitionPageA>(UITransitionType.Fade, false, 0.03f, 0f);
            Time.timeScale = 0f;

            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return Await(open);

            Assert.That(open.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(
                open.Result.ViewObject.GetComponent<CanvasGroup>().alpha,
                Is.EqualTo(0.65f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator Navigation_ExposesCoveredSuspendedAndRestoredVisibility()
        {
            Register<TransitionPageA>(UITransitionType.None, false, 0f, 0f);
            Register<TransitionPageB>(UITransitionType.None, false, 0f, 0f);

            var first = _manager.Navigator.PushAsync<TransitionPageA>().AsTask();
            yield return Await(first);
            var second = _manager.Navigator.PushAsync<TransitionPageB>().AsTask();
            yield return Await(second);

            Assert.That(first.Result.IsVisible, Is.False);
            Assert.That(first.Result.IsCovered, Is.True);
            Assert.That(first.Result.IsSuspended, Is.True);
            Assert.That(first.Result.IsInteractable, Is.False);
            Assert.That(second.Result.IsVisible, Is.True);
            Assert.That(second.Result.IsCovered, Is.False);
            var resumed = first.Result.WaitForResumeAsync().AsTask();
            yield return null;
            Assert.That(resumed.IsCompleted, Is.False);

            var pop = _manager.Navigator.PopAsync().AsTask();
            yield return Await(pop);
            yield return Await(resumed);

            Assert.That(first.Result.IsVisible, Is.True);
            Assert.That(first.Result.IsCovered, Is.False);
            Assert.That(first.Result.IsSuspended, Is.False);
            Assert.That(first.Result.IsInteractable, Is.True);
        }

        [UnityTest]
        public IEnumerator Navigation_CanKeepManagedRefreshRunningWhileCovered()
        {
            var firstConfig = Register<TransitionPageA>(
                UITransitionType.None,
                false,
                0f,
                0f);
            firstConfig.SuspendWhenCovered = false;
            Register<TransitionPageB>(UITransitionType.None, false, 0f, 0f);

            var first = _manager.Navigator.PushAsync<TransitionPageA>().AsTask();
            yield return Await(first);
            yield return Await(_manager.Navigator.PushAsync<TransitionPageB>().AsTask());

            Assert.That(first.Result.IsCovered, Is.True);
            Assert.That(first.Result.IsSuspended, Is.False);
            yield return Await(first.Result.WaitForResumeAsync().AsTask());
        }

        [UnityTest]
        public IEnumerator OpeningModal_BlocksUnderlyingInputBeforeTransitionCompletes()
        {
            Register<TransitionPageA>(UITransitionType.None, false, 0f, 0f);
            var modalConfig = Register<TransitionPageB>(
                UITransitionType.Fade,
                false,
                10f,
                0f);
            modalConfig.UseLayerModalPolicy = false;
            modalConfig.Modal = true;

            var first = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return Await(first);
            Assert.That(first.Result.IsInteractable, Is.True);

            var modal = _manager.OpenAsync<TransitionPageB>().AsTask();
            yield return null;
            Assert.That(_manager.Modals.Count, Is.EqualTo(1));
            Assert.That(_manager.Modals.MaskObject.activeSelf, Is.True);
            Assert.That(first.Result.IsInteractable, Is.False);
            Assert.That(first.Result.IsCovered, Is.True);

            Assert.That(
                _manager.RequestTransitionInterruption<TransitionPageB>(
                    UITransitionInterruption.Interrupt),
                Is.True);
            yield return AwaitCancellation(modal);
            Assert.That(_manager.Modals.Count, Is.Zero);
            Assert.That(first.Result.IsInteractable, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedPopShow_RestoresCoverageFromUnchangedStack()
        {
            var transition = new FailOnSecondShowTransition();
            _manager.Transitions.RegisterCustom("fail-second", transition);
            var firstConfig = Register<TransitionPageA>(
                UITransitionType.Custom,
                false,
                0f,
                0f);
            firstConfig.CustomTransitionId = "fail-second";
            Register<TransitionPageB>(UITransitionType.None, false, 0f, 0f);

            var first = _manager.Navigator.PushAsync<TransitionPageA>().AsTask();
            yield return Await(first);
            var second = _manager.Navigator.PushAsync<TransitionPageB>().AsTask();
            yield return Await(second);

            var pop = _manager.Navigator.PopAsync().AsTask();
            yield return AwaitFailure(pop);

            Assert.That(_manager.Navigator.CurrentPage, Is.SameAs(second.Result));
            Assert.That(first.Result.State, Is.EqualTo(UIContextState.Hidden));
            Assert.That(first.Result.IsCovered, Is.True);
            Assert.That(first.Result.IsSuspended, Is.True);
        }

        [UnityTest]
        public IEnumerator ReentrantCustomTransition_FailsInsteadOfDeadlockingLane()
        {
            _manager.Transitions.RegisterCustom(
                "reentrant",
                new ReentrantTransition(_manager));
            var config = Register<TransitionPageA>(
                UITransitionType.Custom,
                false,
                0f,
                0f);
            config.CustomTransitionId = "reentrant";

            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return AwaitFailure(open);

            Assert.That(
                open.Exception?.GetBaseException(),
                Is.TypeOf<UILifecycleException>());
            Assert.That(_manager.Get<TransitionPageA>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator PoolReuse_OneThousandCycles_PreservesNonDefaultVisualBaseline()
        {
            Register<TransitionPageA>(UITransitionType.Scale, true, 0f, 0f);
            TransitionPageA first = null;
            for (var index = 0; index < 1000; index++)
            {
                var open = _manager.OpenAsync<TransitionPageA>(index).AsTask();
                yield return Await(open);
                first ??= open.Result;
                Assert.That(open.Result, Is.SameAs(first));
                Assert.That(
                    open.Result.View.RectTransform.localScale,
                    Is.EqualTo(new Vector3(1.7f, 0.8f, 1f)));
                Assert.That(
                    open.Result.View.RectTransform.anchoredPosition,
                    Is.EqualTo(new Vector2(31f, -17f)));
                Assert.That(
                    open.Result.ViewObject.GetComponent<CanvasGroup>().alpha,
                    Is.EqualTo(0.65f).Within(0.0001f));
                yield return Await(_manager.CloseAsync(open.Result).AsTask());
            }

            Assert.That(_loader.LoadCount, Is.EqualTo(1));
            Assert.That(_manager.PoolDiagnostics.IdleCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator CustomTransitionFailure_RollsBackAndDoesNotBlockNextOpen()
        {
            _manager.Transitions.RegisterCustom(
                "throwing",
                new ThrowingTransition());
            var config = Register<TransitionPageA>(
                UITransitionType.Custom,
                false,
                0f,
                0f);
            config.CustomTransitionId = "throwing";

            var failed = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return AwaitFailure(failed);
            Assert.That(_manager.Get<TransitionPageA>(), Is.Null);

            config.UseTransition = false;
            var recovered = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return Await(recovered);
            Assert.That(recovered.Result.State, Is.EqualTo(UIContextState.Opened));
        }

        [UnityTest]
        public IEnumerator PublicReverse_ContinuesFromMidpointWithoutEndpointReset()
        {
            using var clock = new GatedTransitionClock(0.25f);
            yield return ReinitializeWithClock(clock);
            var config = Register<TransitionPageA>(
                UITransitionType.Fade,
                false,
                0f,
                1f);
            config.TransitionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return AwaitWithFrameLimit(open, 30);
            var page = open.Result;
            var baseline = page.ViewObject.GetComponent<CanvasGroup>().alpha;

            var close = _manager.CloseAsync(page).AsTask();
            yield return AwaitPendingClock(clock, 1, 30);
            Assert.That(clock.PendingCount, Is.EqualTo(1));
            var midpoint = page.ViewObject.GetComponent<CanvasGroup>().alpha;
            Assert.That(midpoint, Is.LessThan(baseline).And.GreaterThan(0f));
            Assert.That(
                _manager.RequestTransitionInterruption(
                    page,
                    UITransitionInterruption.Reverse),
                Is.True);

            clock.ReleaseNext();
            var reversedStep = page.ViewObject.GetComponent<CanvasGroup>().alpha;
            Assert.That(
                reversedStep,
                Is.GreaterThan(midpoint).And.LessThan(baseline),
                "Reverse must continue from the current value instead of snapping to an endpoint.");
            yield return DrainClockUntilComplete(close, clock, 30);

            Assert.That(
                close.IsCanceled ||
                close.Exception?.GetBaseException() is OperationCanceledException,
                Is.True);
            Assert.That(page.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(
                page.ViewObject.GetComponent<CanvasGroup>().alpha,
                Is.EqualTo(baseline).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator RefreshBaselineAndPoolRebind_AdoptNewStableVisualWithoutDrift()
        {
            Register<TransitionPageA>(UITransitionType.Fade, true, 0f, 0f);
            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return AwaitWithFrameLimit(open, 30);
            var page = open.Result;
            var group = page.ViewObject.GetComponent<CanvasGroup>();
            var rect = page.View.RectTransform;
            group.alpha = 0.72f;
            rect.localScale = new Vector3(1.2f, 1.6f, 1f);
            rect.anchoredPosition = new Vector2(-14f, 27f);
            _manager.RefreshTransitionBaseline(page);
            yield return AwaitWithFrameLimit(_manager.CloseAsync(page).AsTask(), 30);

            group.alpha = 0.43f;
            rect.localScale = new Vector3(2.1f, 0.7f, 1f);
            rect.anchoredPosition = new Vector2(55f, -32f);
            var rebound = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return AwaitWithFrameLimit(rebound, 30);
            Assert.That(rebound.Result, Is.SameAs(page));

            for (var cycle = 0; cycle < 20; cycle++)
            {
                Assert.That(group.alpha, Is.EqualTo(0.43f).Within(0.0001f));
                Assert.That(rect.localScale, Is.EqualTo(new Vector3(2.1f, 0.7f, 1f)));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(55f, -32f)));
                yield return AwaitWithFrameLimit(_manager.CloseAsync(page).AsTask(), 30);
                var reopened = _manager.OpenAsync<TransitionPageA>().AsTask();
                yield return AwaitWithFrameLimit(reopened, 30);
            }
        }

        [UnityTest]
        public IEnumerator AnimationCurve_MidpointUsesOperationSnapshot()
        {
            using var clock = new GatedTransitionClock(0.25f);
            yield return ReinitializeWithClock(clock);
            var curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            var config = Register<TransitionPageA>(
                UITransitionType.Fade,
                false,
                1f,
                0f);
            config.TransitionCurve = curve;

            var open = _manager.OpenAsync<TransitionPageA>().AsTask();
            yield return AwaitPendingClock(clock, 1, 30);
            Assert.That(clock.PendingCount, Is.EqualTo(1));
            var instance = GameObject.Find("Phase8_TransitionPageA(Clone)");
            Assert.That(instance, Is.Not.Null);
            var group = instance.GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.EqualTo(0.65f * 0.25f).Within(0.0001f));

            curve.MoveKey(1, new Keyframe(1f, 0f));
            clock.ReleaseNext();
            Assert.That(
                group.alpha,
                Is.EqualTo(0.65f * 0.5f).Within(0.0001f),
                "The in-flight transition must keep the curve snapshot captured at start.");
            yield return DrainClockUntilComplete(open, clock, 30);

            Assert.That(open.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(group.alpha, Is.EqualTo(0.65f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator Forget_LateBuiltInCancellationCannotWriteReboundTargetOrClearNewOwner()
        {
            var targetObject = CreateTransitionTarget("ForgetFence", 0.8f);
            var target = targetObject.GetComponent<RectTransform>();
            var group = targetObject.GetComponent<CanvasGroup>();
            var clock = new GatedTransitionClock(0.25f);
            var runner = new UITransitionRunner(clock);
            var options = new UITransitionOptions
            {
                Type = UITransitionType.Fade,
                ShowDuration = 1f
            };

            try
            {
                runner.CaptureBaseline(target);
                var oldTask = runner.PlayShowAsync(target, options).AsTask();
                Assert.That(clock.PendingCount, Is.EqualTo(1));

                runner.Forget(target);
                group.alpha = 0.6f;
                target.localScale = new Vector3(2f, 3f, 1f);
                target.anchoredPosition = new Vector2(40f, -25f);
                runner.CaptureBaseline(target, true);
                var newTask = runner.PlayShowAsync(target, options).AsTask();
                Assert.That(clock.PendingCount, Is.EqualTo(2));
                var reboundAlpha = group.alpha;

                clock.ReleaseNext();
                yield return AwaitCompletionWithFrameLimit(oldTask, 30);

                Assert.That(group.alpha, Is.EqualTo(reboundAlpha).Within(0.0001f));
                Assert.That(
                    runner.RequestInterruption(
                        target,
                        UITransitionInterruption.Interrupt),
                    Is.True,
                    "The obsolete finally block must not clear the rebound session owner.");
                clock.ReleaseNext();
                yield return AwaitCompletionWithFrameLimit(newTask, 30);
            }
            finally
            {
                runner.Dispose();
                UnityEngine.Object.Destroy(targetObject);
            }
        }

        [UnityTest]
        public IEnumerator Dispose_LateBuiltInCancellationCannotRewriteTarget()
        {
            var targetObject = CreateTransitionTarget("DisposeFence", 0.8f);
            var target = targetObject.GetComponent<RectTransform>();
            var group = targetObject.GetComponent<CanvasGroup>();
            var clock = new GatedTransitionClock(0.25f);
            var runner = new UITransitionRunner(clock);

            try
            {
                runner.CaptureBaseline(target);
                var task = runner.PlayShowAsync(
                        target,
                        new UITransitionOptions
                        {
                            Type = UITransitionType.Fade,
                            ShowDuration = 1f
                        })
                    .AsTask();
                Assert.That(clock.PendingCount, Is.EqualTo(1));

                runner.Dispose();
                group.alpha = 0.42f;
                target.localScale = new Vector3(4f, 2f, 1f);
                target.anchoredPosition = new Vector2(-18f, 33f);
                clock.ReleaseNext();
                yield return AwaitCompletionWithFrameLimit(task, 30);

                Assert.That(group.alpha, Is.EqualTo(0.42f).Within(0.0001f));
                Assert.That(target.localScale, Is.EqualTo(new Vector3(4f, 2f, 1f)));
                Assert.That(target.anchoredPosition, Is.EqualTo(new Vector2(-18f, 33f)));
            }
            finally
            {
                runner.Dispose();
                UnityEngine.Object.Destroy(targetObject);
            }
        }

        [UnityTest]
        public IEnumerator Dispose_CustomCancellationCallbackCannotMutateEnumeration()
        {
            var targetObject = CreateTransitionTarget("DisposeReentrant", 0.8f);
            var target = targetObject.GetComponent<RectTransform>();
            var runner = new UITransitionRunner();
            runner.RegisterCustom(
                "cancel-forget",
                new CancellationForgetTransition(runner, target));

            try
            {
                var task = runner.PlayShowAsync(
                        target,
                        new UITransitionOptions
                        {
                            Type = UITransitionType.Custom,
                            CustomTransitionId = "cancel-forget"
                        })
                    .AsTask();
                Assert.DoesNotThrow(runner.Dispose);
                yield return AwaitCompletionWithFrameLimit(task, 30);
            }
            finally
            {
                runner.Dispose();
                UnityEngine.Object.Destroy(targetObject);
            }
        }

        [UnityTest]
        public IEnumerator Dispose_CallbackFailureStillCancelsRemainingSessions()
        {
            var firstObject = CreateTransitionTarget("DisposeThrowingCallback", 0.8f);
            var secondObject = CreateTransitionTarget("DisposeObservedCallback", 0.8f);
            var first = new CancellationCallbackTransition(true);
            var second = new CancellationCallbackTransition(false);
            var runner = new UITransitionRunner();
            runner.RegisterCustom("throw-on-cancel", first);
            runner.RegisterCustom("observe-cancel", second);

            try
            {
                var firstTask = runner.PlayShowAsync(
                        firstObject.GetComponent<RectTransform>(),
                        new UITransitionOptions
                        {
                            Type = UITransitionType.Custom,
                            CustomTransitionId = "throw-on-cancel"
                        })
                    .AsTask();
                var secondTask = runner.PlayShowAsync(
                        secondObject.GetComponent<RectTransform>(),
                        new UITransitionOptions
                        {
                            Type = UITransitionType.Custom,
                            CustomTransitionId = "observe-cancel"
                        })
                    .AsTask();

                var error = Assert.Throws<AggregateException>(runner.Dispose);
                Assert.That(error.InnerExceptions, Is.Not.Empty);
                Assert.That(first.CancellationObserved, Is.True);
                Assert.That(second.CancellationObserved, Is.True);
                yield return AwaitCompletionWithFrameLimit(firstTask, 30);
                yield return AwaitCompletionWithFrameLimit(secondTask, 30);
            }
            finally
            {
                runner.Dispose();
                UnityEngine.Object.Destroy(firstObject);
                UnityEngine.Object.Destroy(secondObject);
            }
        }

        private UIConfig Register<T>(
            UITransitionType type,
            bool cache,
            float showDuration,
            float hideDuration)
            where T : BaseContext
        {
            var config = new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = typeof(T).Name,
                Layer = UILayer.Normal,
                CacheOnClose = cache,
                MaxPoolSize = cache ? 1 : 0,
                FullScreen = true,
                UseTransition = type != UITransitionType.None,
                TransitionType = type,
                ShowDuration = showDuration,
                HideDuration = hideDuration,
                IgnoreTransitionTimeScale = true,
                StartScale = 0.8f
            };
            _manager.Register<T>(config);
            return config;
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception;
            }
        }

        private static IEnumerator AwaitCancellation(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.That(
                task.IsCanceled ||
                task.Exception?.GetBaseException() is OperationCanceledException,
                Is.True);
        }

        private static IEnumerator AwaitFailure(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            Assert.That(task.IsFaulted, Is.True);
        }

        private static IEnumerator AwaitCompletionWithFrameLimit(
            Task task,
            int frameLimit)
        {
            for (var frame = 0; frame < frameLimit && !task.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for transition task.");
        }

        private static IEnumerator AwaitWithFrameLimit<T>(
            Task<T> task,
            int frameLimit)
        {
            yield return AwaitCompletionWithFrameLimit(task, frameLimit);
            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception;
            }

            Assert.That(task.IsCanceled, Is.False);
        }

        private static IEnumerator AwaitWithFrameLimit(
            Task task,
            int frameLimit)
        {
            yield return AwaitCompletionWithFrameLimit(task, frameLimit);
            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception;
            }

            Assert.That(task.IsCanceled, Is.False);
        }

        private static IEnumerator DrainClockUntilComplete(
            Task task,
            GatedTransitionClock clock,
            int frameLimit)
        {
            for (var frame = 0; frame < frameLimit && !task.IsCompleted; frame++)
            {
                if (clock.PendingCount > 0)
                {
                    clock.ReleaseNext();
                }

                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Timed out draining transition clock.");
        }

        private static IEnumerator AwaitPendingClock(
            GatedTransitionClock clock,
            int pendingCount,
            int frameLimit)
        {
            for (var frame = 0;
                 frame < frameLimit && clock.PendingCount < pendingCount;
                 frame++)
            {
                yield return null;
            }

            Assert.That(
                clock.PendingCount,
                Is.GreaterThanOrEqualTo(pendingCount),
                "Timed out waiting for the transition clock gate.");
        }

        private IEnumerator ReinitializeWithClock(GatedTransitionClock clock)
        {
            yield return AwaitWithFrameLimit(_manager.ShutdownAsync().AsTask(), 60);
            _manager = new UIManager(clock);
            _manager.Initialize(_loader, new UIObjectPool());
        }

        private static GameObject CreateTransitionTarget(string name, float alpha)
        {
            var target = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasGroup));
            target.GetComponent<CanvasGroup>().alpha = alpha;
            return target;
        }

        public sealed class TransitionPageA : BasePageContext
        {
            public UniTask WaitForResumeAsync()
            {
                return WaitUntilResumedAsync();
            }
        }

        public sealed class TransitionPageB : BasePageContext
        {
        }

        private sealed class ThrowingTransition : IUITransition
        {
            public UniTask PlayShowAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("Expected custom transition failure.");
            }

            public UniTask PlayHideAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class FailOnSecondShowTransition : IUITransition
        {
            private int _showCount;

            public UniTask PlayShowAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                _showCount++;
                if (_showCount > 1)
                {
                    throw new InvalidOperationException("Expected second show failure.");
                }

                return UniTask.CompletedTask;
            }

            public UniTask PlayHideAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class ReentrantTransition : IUITransition
        {
            private readonly UIManager _manager;

            public ReentrantTransition(UIManager manager)
            {
                _manager = manager;
            }

            public async UniTask PlayShowAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                await UniTask.Yield();
                await _manager.CloseAsync<TransitionPageA>(cancellationToken);
            }

            public UniTask PlayHideAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class CancellationForgetTransition : IUITransition
        {
            private readonly UITransitionRunner _runner;
            private readonly RectTransform _target;

            public CancellationForgetTransition(
                UITransitionRunner runner,
                RectTransform target)
            {
                _runner = runner;
                _target = target;
            }

            public async UniTask PlayShowAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                using (cancellationToken.Register(() => _runner.Forget(_target)))
                {
                    await UniTask.WaitUntilCanceled(cancellationToken);
                }
            }

            public UniTask PlayHideAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class CancellationCallbackTransition : IUITransition
        {
            private readonly bool _throwOnCancellation;

            public CancellationCallbackTransition(bool throwOnCancellation)
            {
                _throwOnCancellation = throwOnCancellation;
            }

            public bool CancellationObserved { get; private set; }

            public async UniTask PlayShowAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                using (cancellationToken.Register(() =>
                       {
                           CancellationObserved = true;
                           if (_throwOnCancellation)
                           {
                               throw new InvalidOperationException(
                                   "Expected cancellation callback failure.");
                           }
                       }))
                {
                    await UniTask.WaitUntilCanceled(cancellationToken);
                }
            }

            public UniTask PlayHideAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }
        }

        private sealed class TransitionLoader : IResourceLoader, IDisposable
        {
            private readonly Dictionary<string, GameObject> _prefabs =
                new Dictionary<string, GameObject>();

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
                        $"Phase8_{key}",
                        typeof(RectTransform),
                        typeof(CanvasGroup),
                        typeof(GraphicRaycaster),
                        typeof(UIView));
                    var rect = prefab.GetComponent<RectTransform>();
                    rect.localScale = new Vector3(1.7f, 0.8f, 1f);
                    rect.anchoredPosition = new Vector2(31f, -17f);
                    prefab.GetComponent<CanvasGroup>().alpha = 0.65f;
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

        private sealed class GatedTransitionClock : IUITransitionClock, IDisposable
        {
            private readonly Queue<UniTaskCompletionSource> _frames =
                new Queue<UniTaskCompletionSource>();
            private bool _completeImmediately;

            public GatedTransitionClock(float deltaTime)
            {
                DeltaTime = deltaTime;
            }

            public float DeltaTime { get; set; }
            public int PendingCount => _frames.Count;

            public float GetDeltaTime(bool ignoreTimeScale)
            {
                return DeltaTime;
            }

            public UniTask NextFrameAsync(CancellationToken cancellationToken)
            {
                if (_completeImmediately)
                {
                    return UniTask.CompletedTask;
                }

                var frame = new UniTaskCompletionSource();
                _frames.Enqueue(frame);
                return frame.Task;
            }

            public void ReleaseNext()
            {
                Assert.That(_frames.Count, Is.GreaterThan(0));
                _frames.Dequeue().TrySetResult();
            }

            public void Dispose()
            {
                _completeImmediately = true;
                while (_frames.Count > 0)
                {
                    _frames.Dequeue().TrySetResult();
                }
            }
        }
    }
}
