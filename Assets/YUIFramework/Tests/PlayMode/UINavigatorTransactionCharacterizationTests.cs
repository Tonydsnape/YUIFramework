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
    /// <summary>
    /// Phase 3: navigator FIFO/IsBusy, duplicate-push prevention, public BringToTopAsync,
    /// async guard extension point, and non-destructive-first transaction
    /// rollback/convergence for Push/Pop/Replace/BringToTop.
    /// </summary>
    public sealed class UINavigatorTransactionCharacterizationTests
    {
        private CharacterizationLoader _loader;
        private UIManager _manager;
        private GameObject _rootObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rootObject = new GameObject(
                "NavigatorTestUIRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _rootObject.AddComponent<UIRoot>();

            _loader = new CharacterizationLoader();
            _manager = new UIManager();
            _manager.Initialize(_loader, new UIObjectPool());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null && _manager.IsInitialized)
            {
                _manager.Navigator.Guard = null;
                _loader.OpenAllGates();
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
        public IEnumerator PushExistingPage_WithDefaultOptions_BringsItToTopWithoutDuplicating()
        {
            Register<PageA>("Tests/DefaultDup/A");
            Register<PageB>("Tests/DefaultDup/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            yield return Await(nav.PushAsync<PageB>().AsTask());

            // No BringExistingPageToTop set: duplicate prevention must still apply.
            var bringTask = nav.PushAsync<PageA>("restored").AsTask();
            yield return Await(bringTask);

            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            Assert.That(bringTask.Result.LastArgs, Is.EqualTo("restored"));
            Assert.That(_manager.Get<PageB>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator SameTypeTopPush_IsRefreshOnly_DoesNotCloseInstance()
        {
            Register<PageA>("Tests/RefreshPush/A");
            var nav = _manager.Navigator;

            var firstTask = nav.PushAsync<PageA>("first").AsTask();
            yield return Await(firstTask);
            var refreshTask = nav.PushAsync<PageA>("second").AsTask();
            yield return Await(refreshTask);

            Assert.That(refreshTask.Result, Is.SameAs(firstTask.Result));
            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(refreshTask.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(refreshTask.Result.ShowCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator SameTypeTopReplace_IsRefreshOnly_DoesNotCloseInstance()
        {
            Register<PageA>("Tests/RefreshReplace/A");
            var nav = _manager.Navigator;

            var firstTask = nav.PushAsync<PageA>("first").AsTask();
            yield return Await(firstTask);
            var refreshTask = nav.ReplaceAsync<PageA>("second").AsTask();
            yield return Await(refreshTask);

            Assert.That(refreshTask.Result, Is.SameAs(firstTask.Result));
            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(refreshTask.Result.ShowCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator BringToTopAsync_Public_MovesExistingPageToTopAndClosesPagesAbove()
        {
            Register<PageA>("Tests/BringPublic/A");
            Register<PageB>("Tests/BringPublic/B");
            Register<PageC>("Tests/BringPublic/C");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            yield return Await(nav.PushAsync<PageB>().AsTask());
            yield return Await(nav.PushAsync<PageC>().AsTask());

            var bringTask = nav.BringToTopAsync<PageA>("top-again").AsTask();
            yield return Await(bringTask);

            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            Assert.That(bringTask.Result.LastArgs, Is.EqualTo("top-again"));
            Assert.That(_manager.Get<PageB>(), Is.Null);
            Assert.That(_manager.Get<PageC>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator NavigateBackAsync_IsAliasForBackAsync()
        {
            Register<PageA>("Tests/BackAlias/A");
            Register<PageB>("Tests/BackAlias/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            yield return Await(nav.PushAsync<PageB>().AsTask());

            var backTask = nav.NavigateBackAsync().AsTask();
            yield return Await(backTask);

            Assert.That(backTask.Result, Is.True);
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
        }

        [UnityTest]
        public IEnumerator Navigator_IsBusy_ReflectsQueueActivity()
        {
            Register<PageA>("Tests/IsBusy/A");
            _loader.ArmGate("Tests/IsBusy/A");
            var nav = _manager.Navigator;

            Assert.That(nav.IsBusy, Is.False);
            var pushTask = nav.PushAsync<PageA>().AsTask();
            yield return null;

            Assert.That(nav.IsBusy, Is.True);

            _loader.OpenGate("Tests/IsBusy/A");
            yield return Await(pushTask);

            Assert.That(nav.IsBusy, Is.False);
        }

        [UnityTest]
        public IEnumerator RapidPushBack_FifoOrderingKeepsStackConsistent()
        {
            Register<PageA>("Tests/Stress/A");
            Register<PageB>("Tests/Stress/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());

            var pending = new List<Task>();
            for (var i = 0; i < 4; i++)
            {
                pending.Add(nav.PushAsync<PageB>().AsTask());
                pending.Add(nav.BackAsync().AsTask());
            }

            foreach (var task in pending)
            {
                yield return Await(task);
            }

            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            Assert.That(nav.IsBusy, Is.False);
            Assert.That(_manager.Get<PageB>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator Guard_RefusingPush_HasNoSideEffectsAndThrowsRejection()
        {
            Register<PageA>("Tests/GuardPush/A");
            Register<PageB>("Tests/GuardPush/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            nav.Guard = (request, token) => UniTask.FromResult(request.Kind != UINavigationCommandKind.Push);

            var rejectedTask = nav.PushAsync<PageB>().AsTask();
            yield return AwaitFailure(rejectedTask);

            Assert.That(GetFailure(rejectedTask), Is.TypeOf<UINavigationRejectedException>());
            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            Assert.That(_manager.Get<PageB>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator Guard_RefusingBack_ReturnsFalseWithNoSideEffects()
        {
            Register<PageA>("Tests/GuardBack/A");
            Register<PageB>("Tests/GuardBack/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            yield return Await(nav.PushAsync<PageB>().AsTask());
            nav.Guard = (request, token) => UniTask.FromResult(request.Kind != UINavigationCommandKind.Pop);

            var backTask = nav.BackAsync().AsTask();
            yield return Await(backTask);

            Assert.That(backTask.Result, Is.False);
            Assert.That(nav.Count, Is.EqualTo(2));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageB)));
        }

        [UnityTest]
        public IEnumerator Guard_ThrowingException_PropagatesAndLeavesStackIntact()
        {
            Register<PageA>("Tests/GuardThrow/A");
            Register<PageB>("Tests/GuardThrow/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            nav.Guard = (request, token) => throw new InvalidOperationException("guard exploded");

            var pushTask = nav.PushAsync<PageB>().AsTask();
            yield return AwaitFailure(pushTask);

            Assert.That(GetFailure(pushTask), Is.TypeOf<InvalidOperationException>());
            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
        }

        [UnityTest]
        public IEnumerator Push_OpenFailure_RollsBackShownCurrentPage()
        {
            Register<PageA>("Tests/PushRollback/A");
            Register<FailingInitPage>("Tests/PushRollback/Fail");
            var nav = _manager.Navigator;

            var firstTask = nav.PushAsync<PageA>().AsTask();
            yield return Await(firstTask);

            var failedPush = nav.PushAsync<FailingInitPage>().AsTask();
            yield return AwaitFailure(failedPush);

            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            Assert.That(firstTask.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(firstTask.Result.ViewObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator Pop_ShowPreviousFailure_KeepsCurrentPageAndDoesNotDestructivelyClose()
        {
            Register<FailingRefreshPage>("Tests/PopRollback/A");
            Register<PageB>("Tests/PopRollback/B");
            var nav = _manager.Navigator;

            // FailingRefreshPage succeeds its first OnShow (initial push) but throws on
            // the second OnShow invocation, which Pop triggers when re-showing it.
            var firstTask = nav.PushAsync<FailingRefreshPage>().AsTask();
            yield return Await(firstTask);
            yield return Await(nav.PushAsync<PageB>().AsTask());

            var popTask = nav.PopAsync().AsTask();
            yield return AwaitFailure(popTask);

            Assert.That(nav.Count, Is.EqualTo(2));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageB)));
            Assert.That(_manager.Get<PageB>(), Is.Not.Null);
            Assert.That(_manager.Get<PageB>().State, Is.EqualTo(UIContextState.Opened));
        }

        [UnityTest]
        public IEnumerator Replace_CloseCurrentFailsAfterNewOpened_ConvergesOnNewTopWithoutFabricatingOld()
        {
            Register<FailingHidePage>("Tests/ReplaceConverge/Old");
            Register<PageB>("Tests/ReplaceConverge/New");
            var nav = _manager.Navigator;

            var oldTask = nav.PushAsync<FailingHidePage>().AsTask();
            yield return Await(oldTask);

            var replaceTask = nav.ReplaceAsync<PageB>().AsTask();
            yield return AwaitFailure(replaceTask);

            // The old page's Close destructively released it before failing (hide
            // throws); the navigator must never fabricate/reopen that identity. It
            // converges on the newly opened page as the only top entry instead.
            Assert.That(oldTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageB)));
            Assert.That(_manager.Get<PageB>(), Is.Not.Null);
            Assert.That(_manager.Get<PageB>().State, Is.EqualTo(UIContextState.Opened));
        }

        [UnityTest]
        public IEnumerator BringToTop_CloseAboveFailure_DegradesButKeepsConsistentStack()
        {
            Register<PageA>("Tests/BringDegrade/A");
            Register<FailingHidePage>("Tests/BringDegrade/Mid");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            var midTask = nav.PushAsync<FailingHidePage>().AsTask();
            yield return Await(midTask);

            var bringTask = nav.BringToTopAsync<PageA>().AsTask();
            yield return AwaitFailure(bringTask);

            Assert.That(GetFailure(bringTask), Is.Not.Null);
            // Degraded-but-consistent: the target is on top and Opened, the failed
            // "above" page was force-released by UIManager's own failure handling and
            // is never left referenced by the tracked stack.
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(midTask.Result.State, Is.EqualTo(UIContextState.Released));
        }

        [UnityTest]
        public IEnumerator Push_RollbackAlsoFails_ThrowsAggregateException()
        {
            Register<FailingRefreshPage>("Tests/PushAggregate/A");
            Register<FailingInitPage>("Tests/PushAggregate/Fail");
            var nav = _manager.Navigator;

            var firstTask = nav.PushAsync<FailingRefreshPage>().AsTask();
            yield return Await(firstTask);

            // Push hides the current page then tries to open the new one; the open
            // fails, and the rollback re-show of the current page also fails (its
            // second OnShow call throws), so both failures must be aggregated.
            var failedPush = nav.PushAsync<FailingInitPage>().AsTask();
            yield return AwaitFailure(failedPush);

            Assert.That(GetDirectFailure(failedPush), Is.TypeOf<AggregateException>());
            var aggregate = (AggregateException)GetDirectFailure(failedPush);
            Assert.That(aggregate.InnerExceptions.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Push_CancellationBeforeOpenCompletes_LeavesStackIntact()
        {
            Register<PageA>("Tests/PushCancel/A");
            Register<PageB>("Tests/PushCancel/B");
            _loader.ArmGate("Tests/PushCancel/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            using var cancellation = new CancellationTokenSource();
            var pushTask = nav.PushAsync<PageB>(cancellationToken: cancellation.Token).AsTask();
            yield return null;

            cancellation.Cancel();
            yield return AwaitCancellation(pushTask);

            Assert.That(nav.Count, Is.EqualTo(1));
            Assert.That(nav.CurrentPageType, Is.EqualTo(typeof(PageA)));
            _loader.OpenGate("Tests/PushCancel/B");
        }

        [UnityTest]
        public IEnumerator Shutdown_DuringPush_AllowsRollbackBeforeQueuesDrain()
        {
            Register<PageA>("Tests/ShutdownPush/A");
            Register<PageB>("Tests/ShutdownPush/B");
            _loader.ArmGate("Tests/ShutdownPush/B");
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            var pushTask = nav.PushAsync<PageB>().AsTask();
            yield return null;

            var shutdownTask = _manager.ShutdownAsync().AsTask();
            yield return Await(shutdownTask);
            yield return AwaitCancellation(pushTask);

            Assert.That(_manager.IsInitialized, Is.False);
            Assert.That(
                pushTask.Exception?.GetBaseException(),
                Is.Not.TypeOf<AggregateException>());
        }

        [UnityTest]
        public IEnumerator Shutdown_DuringReplace_AllowsTransitionedRollbackClose()
        {
            Register<PageA>("Tests/ShutdownReplace/A", useTransition: true, hideDuration: 10f);
            Register<PageB>("Tests/ShutdownReplace/B", useTransition: true);
            var nav = _manager.Navigator;

            yield return Await(nav.PushAsync<PageA>().AsTask());
            var replaceTask = nav.ReplaceAsync<PageB>().AsTask();
            yield return AwaitState<PageA>(UIContextState.Hiding);

            var shutdownTask = _manager.ShutdownAsync().AsTask();
            yield return Await(shutdownTask);
            yield return AwaitCancellation(replaceTask);

            Assert.That(_manager.IsInitialized, Is.False);
            Assert.That(
                replaceTask.Exception?.GetBaseException(),
                Is.Not.TypeOf<AggregateException>());
        }

        [UnityTest]
        public IEnumerator Reentrant_NavigatorCallFromWithinItsOwnOperation_FailsExplicitly()
        {
            Register<ReentrantNavPage>("Tests/ReentrantNav/A");
            ReentrantNavPage.Navigator = _manager.Navigator;
            var nav = _manager.Navigator;

            var pushTask = nav.PushAsync<ReentrantNavPage>().AsTask();
            yield return AwaitFailure(pushTask);

            Assert.That(GetFailure(pushTask), Is.TypeOf<UILifecycleException>());
            Assert.That(GetFailure(pushTask).InnerException, Is.TypeOf<UIOperationReentrancyException>());
        }

        private void Register<T>(
            string prefabKey,
            bool useTransition = false,
            float hideDuration = 0f)
            where T : BaseContext
        {
            _manager.Register<T>(new UIConfig
            {
                Id = typeof(T).Name + "_" + prefabKey,
                PrefabKey = prefabKey,
                Layer = UILayer.Normal,
                CacheOnClose = false,
                MaxPoolSize = 0,
                FullScreen = true,
                UseTransition = useTransition,
                TransitionType = useTransition ? UITransitionType.Fade : UITransitionType.None,
                ShowDuration = 0f,
                HideDuration = hideDuration
            });
        }

        private IEnumerator AwaitState<T>(UIContextState state) where T : BaseContext
        {
            var timeoutAt = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                var context = _manager.Get<T>();
                if (context != null && context.State == state)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail($"Timed out waiting for {typeof(T).Name} to reach {state}.");
        }

        private static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? new InvalidOperationException("Task failed.");
            }
        }

        private static IEnumerator AwaitFailure(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (!task.IsFaulted && !task.IsCanceled)
            {
                Assert.Fail("Expected the operation to fail.");
            }
        }

        private static IEnumerator AwaitCancellation(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException)
            {
                yield break;
            }

            Assert.Fail("Expected the operation to be canceled.");
        }

        private static Exception GetFailure(Task task)
        {
            return task.Exception?.GetBaseException();
        }

        /// <summary>
        /// Returns exactly what the framework threw for this task, unwrapped only from
        /// the Task's own single-exception AggregateException wrapper. Unlike
        /// <see cref="GetFailure"/> (which uses <see cref="Exception.GetBaseException"/>
        /// and keeps drilling through any further AggregateException nesting), this is
        /// needed to assert on a deliberately-thrown AggregateException itself.
        /// </summary>
        private static Exception GetDirectFailure(Task task)
        {
            return task.Exception?.InnerException;
        }

        public abstract class TrackedPage : BasePageContext
        {
            public int ShowCount { get; private set; }
            public object LastArgs { get; private set; }

            protected override void HandleShow(object args)
            {
                ShowCount++;
                LastArgs = args;
            }
        }

        public class PageA : TrackedPage
        {
        }

        public class PageB : TrackedPage
        {
        }

        public class PageC : TrackedPage
        {
        }

        public class FailingInitPage : TrackedPage
        {
            protected override void HandleInit()
            {
                base.HandleInit();
                throw new InvalidOperationException("Expected init failure.");
            }
        }

        public class FailingRefreshPage : TrackedPage
        {
            protected override void HandleShow(object args)
            {
                base.HandleShow(args);
                if (ShowCount > 1)
                {
                    throw new InvalidOperationException("Expected refresh/re-show failure.");
                }
            }
        }

        public class FailingHidePage : TrackedPage
        {
            protected override void HandleHide()
            {
                base.HandleHide();
                throw new InvalidOperationException("Expected hide failure.");
            }
        }

        /// <summary>
        /// Calls back into the navigator for the same lane from inside its own OnShow,
        /// which runs while the navigator's single FIFO lane is still executing the very
        /// Push command that is showing this page. Must fail explicitly rather than
        /// deadlock waiting for a queue slot behind itself.
        /// </summary>
        public class ReentrantNavPage : TrackedPage
        {
            public static UINavigator Navigator;

            protected override void HandleShow(object args)
            {
                base.HandleShow(args);
                Navigator.PushAsync<ReentrantNavPage>();
            }
        }

        private sealed class CharacterizationLoader : IResourceLoader, IDisposable
        {
            private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
            private readonly Dictionary<string, TaskCompletionSource<bool>> _gates =
                new Dictionary<string, TaskCompletionSource<bool>>();

            public void ArmGate(string key)
            {
                _gates[key] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void OpenGate(string key)
            {
                if (_gates.TryGetValue(key, out var gate))
                {
                    gate.TrySetResult(true);
                }
            }

            public void OpenAllGates()
            {
                foreach (var gate in _gates.Values)
                {
                    gate.TrySetResult(true);
                }
            }

            public async UniTask<GameObject> LoadPrefabAsync(string key, CancellationToken cancellationToken = default)
            {
                if (_gates.TryGetValue(key, out var gate))
                {
                    using (cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken)))
                    {
                        await gate.Task;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!_prefabs.TryGetValue(key, out var prefab) || prefab == null)
                {
                    prefab = new GameObject($"NavPrefab_{key}", typeof(RectTransform), typeof(UIView));
                    prefab.SetActive(false);
                    prefab.hideFlags = HideFlags.DontSave;
                    _prefabs[key] = prefab;
                }

                return prefab;
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
                _gates.Clear();
            }
        }
    }
}
