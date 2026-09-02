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
    /// Phase 3: per-key FIFO coordination, single-flight Open merging, and shutdown
    /// quiescing. Uses a gate-controlled resource loader instead of fragile
    /// frame/timing-based waits so every scenario is deterministic.
    /// </summary>
    public sealed class UIOperationCoordinatorCharacterizationTests
    {
        private GatedResourceLoader _loader;
        private UIManager _manager;
        private GameObject _rootObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rootObject = new GameObject(
                "CoordinatorTestUIRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _rootObject.AddComponent<UIRoot>();

            _loader = new GatedResourceLoader();
            _manager = new UIManager();
            _manager.Initialize(_loader, new UIObjectPool());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null && _manager.IsInitialized)
            {
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
        public IEnumerator ConcurrentOpen_SameKeySameArgs_SharesSingleCreation()
        {
            Register<TestPageA>("Tests/MergeSame", false);
            _loader.ArmGate("Tests/MergeSame");

            var firstTask = _manager.OpenAsync<TestPageA>("shared").AsTask();
            var secondTask = _manager.OpenAsync<TestPageA>("shared").AsTask();
            yield return null;

            Assert.That(_loader.LoadCallCount, Is.EqualTo(1));
            Assert.That(firstTask.IsCompleted, Is.False);
            Assert.That(secondTask.IsCompleted, Is.False);

            _loader.OpenGate("Tests/MergeSame");
            yield return Await(firstTask);
            yield return Await(secondTask);

            Assert.That(firstTask.Result, Is.SameAs(secondTask.Result));
            Assert.That(_loader.LoadCallCount, Is.EqualTo(1));
            Assert.That(firstTask.Result.InitCount, Is.EqualTo(1));
            Assert.That(firstTask.Result.ShowCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ConcurrentOpen_DifferentArgs_DoesNotMerge()
        {
            Register<TestPageA>("Tests/MergeDifferentArgs", false);
            _loader.ArmGate("Tests/MergeDifferentArgs");

            var firstTask = _manager.OpenAsync<TestPageA>("one").AsTask();
            var secondTask = _manager.OpenAsync<TestPageA>("two").AsTask();
            yield return null;

            // Different args: the second request must not attach to the first and must
            // wait its own turn behind it in the same key's FIFO queue.
            Assert.That(_loader.LoadCallCount, Is.EqualTo(1));
            Assert.That(secondTask.IsCompleted, Is.False);

            _loader.OpenGate("Tests/MergeDifferentArgs");
            yield return Await(firstTask);
            yield return Await(secondTask);

            Assert.That(firstTask.Result, Is.SameAs(secondTask.Result));
            Assert.That(secondTask.Result.LastArgs, Is.EqualTo("two"));
            Assert.That(secondTask.Result.ShowCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator ConcurrentOpen_DifferentKeys_ExecuteWithoutBlockingEachOther()
        {
            Register<TestPageA>("Tests/KeyA", false);
            Register<TestPageB>("Tests/KeyB", false);
            _loader.ArmGate("Tests/KeyA");

            var taskA = _manager.OpenAsync<TestPageA>().AsTask();
            var taskB = _manager.OpenAsync<TestPageB>().AsTask();
            yield return Await(taskB);

            Assert.That(taskB.IsCompleted, Is.True);
            Assert.That(taskA.IsCompleted, Is.False);

            _loader.OpenGate("Tests/KeyA");
            yield return Await(taskA);
            Assert.That(taskA.Result, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator OpenThenClose_FIFO_ClosesTheOpenedInstance()
        {
            Register<TestPageA>("Tests/OpenCloseFifo", false);

            var openTask = _manager.OpenAsync<TestPageA>().AsTask();
            var closeTask = _manager.CloseAsync<TestPageA>().AsTask();
            yield return Await(openTask);
            yield return Await(closeTask);

            Assert.That(openTask.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_manager.Get<TestPageA>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator CloseThenOpen_FIFO_ExecutesInOrder()
        {
            Register<TestPageA>("Tests/CloseOpenFifo", false);

            var firstOpen = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(firstOpen);

            var closeTask = _manager.CloseAsync(firstOpen.Result).AsTask();
            var secondOpen = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(closeTask);
            yield return Await(secondOpen);

            Assert.That(secondOpen.Result, Is.Not.SameAs(firstOpen.Result));
            Assert.That(firstOpen.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(secondOpen.Result.State, Is.EqualTo(UIContextState.Opened));
        }

        [UnityTest]
        public IEnumerator CloseStaleReference_AfterNewInstanceOpened_IsNoOp()
        {
            Register<TestPageA>("Tests/StaleClose", false);

            var firstTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(firstTask);
            yield return Await(_manager.CloseAsync(firstTask.Result).AsTask());

            var secondTask = _manager.OpenAsync<TestPageA>().AsTask();
            yield return Await(secondTask);

            // A generic Close<T>() resolves the active context at execution time, so a
            // stale explicit reference to the first (already released) instance must not
            // touch the newer active instance.
            yield return Await(_manager.CloseAsync(firstTask.Result).AsTask());

            Assert.That(_manager.Get<TestPageA>(), Is.SameAs(secondTask.Result));
            Assert.That(secondTask.Result.State, Is.EqualTo(UIContextState.Opened));
        }

        [UnityTest]
        public IEnumerator MergedOpen_CallerCancellation_DoesNotAffectSharedResultOrNextRequest()
        {
            Register<TestPageA>("Tests/MergeCancel", false);
            _loader.ArmGate("Tests/MergeCancel");

            using var firstCancellation = new CancellationTokenSource();
            var firstTask = _manager.OpenAsync<TestPageA>("args", firstCancellation.Token).AsTask();
            var secondTask = _manager.OpenAsync<TestPageA>("args").AsTask();
            yield return null;

            firstCancellation.Cancel();
            yield return AwaitCancellation(firstTask);

            // Even the first caller owns only its wait once another equivalent caller
            // shares the first-creation operation.
            Assert.That(secondTask.IsCompleted, Is.False);

            _loader.OpenGate("Tests/MergeCancel");
            yield return Await(secondTask);
            Assert.That(secondTask.Result, Is.Not.Null);
            Assert.That(secondTask.Result.State, Is.EqualTo(UIContextState.Opened));

            var thirdTask = _manager.OpenAsync<TestPageA>("args2").AsTask();
            yield return Await(thirdTask);
            Assert.That(thirdTask.Result, Is.SameAs(secondTask.Result));
            Assert.That(thirdTask.Result.LastArgs, Is.EqualTo("args2"));
        }

        [UnityTest]
        public IEnumerator FailedOpen_DoesNotStallSubsequentQueuedCommand()
        {
            Register<FailingInitPage>("Tests/FailStall", false);

            var failedTask = _manager.OpenAsync<FailingInitPage>().AsTask();
            var closeTask = _manager.CloseAsync<FailingInitPage>().AsTask();
            yield return AwaitFailure(failedTask);
            yield return Await(closeTask);

            var recoveredTask = _manager.OpenAsync<FailingInitPage>().AsTask();
            yield return AwaitFailure(recoveredTask);
            Assert.That(_manager.Get<FailingInitPage>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator Shutdown_WithQueuedAndInFlightCommands_CompletesWithoutDeadlock()
        {
            Register<TestPageA>("Tests/ShutdownGate", false);
            _loader.ArmGate("Tests/ShutdownGate");

            var openTask = _manager.OpenAsync<TestPageA>().AsTask();
            var closeTask = _manager.CloseAsync<TestPageA>().AsTask();
            yield return null;

            var shutdownTask = _manager.ShutdownAsync().AsTask();
            _loader.OpenAllGates();
            yield return Await(shutdownTask);

            Assert.That(_manager.IsInitialized, Is.False);
            yield return AwaitEitherOutcome(openTask);
            yield return AwaitEitherOutcome(closeTask);
        }

        [UnityTest]
        public IEnumerator Reentrant_OpenFromWithinItsOwnShowCallback_FailsExplicitlyInsteadOfDeadlocking()
        {
            ReentrantOpenPage.Manager = _manager;
            Register<ReentrantOpenPage>("Tests/Reentrant", false);

            var openTask = _manager.OpenAsync<ReentrantOpenPage>().AsTask();
            yield return AwaitFailure(openTask);

            var failure = GetFailure(openTask);
            Assert.That(failure, Is.TypeOf<UILifecycleException>());
            Assert.That(failure.InnerException, Is.TypeOf<UIOperationReentrancyException>());
        }

        private void Register<T>(string prefabKey, bool cacheOnClose) where T : BaseContext
        {
            _manager.Register<T>(new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = prefabKey,
                Layer = UILayer.Normal,
                CacheOnClose = cacheOnClose,
                MaxPoolSize = cacheOnClose ? 1 : 0,
                FullScreen = true
            });
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

        private static IEnumerator AwaitEitherOutcome(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private static Exception GetFailure(Task task)
        {
            return task.Exception?.GetBaseException();
        }

        public class TestPageA : UIManagerCharacterizationTests.TrackedPageContext
        {
        }

        public class TestPageB : UIManagerCharacterizationTests.TrackedPageContext
        {
        }

        public class FailingInitPage : UIManagerCharacterizationTests.TrackedPageContext
        {
            protected override void HandleInit()
            {
                base.HandleInit();
                throw new InvalidOperationException("Expected init failure.");
            }
        }

        /// <summary>
        /// Synchronously calls back into <see cref="UIManager.OpenAsync{T}"/> for its own
        /// type from inside its own OnShow callback, which runs on the same key's lane
        /// that is currently executing this very Open command. This must fail explicitly
        /// via <see cref="UIOperationReentrancyException"/> instead of deadlocking by
        /// awaiting itself.
        /// </summary>
        public class ReentrantOpenPage : UIManagerCharacterizationTests.TrackedPageContext
        {
            public static UIManager Manager;

            protected override void HandleShow(object args)
            {
                base.HandleShow(args);
                // This call is expected to throw synchronously (reentrancy detected)
                // before ever producing an awaitable, so no await/discard is needed.
                Manager.OpenAsync<ReentrantOpenPage>();
            }
        }

        private sealed class GatedResourceLoader : IResourceLoader, IDisposable
        {
            private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();
            private readonly Dictionary<string, TaskCompletionSource<bool>> _gates =
                new Dictionary<string, TaskCompletionSource<bool>>();

            public int LoadCallCount { get; private set; }
            public int ReleaseCount { get; private set; }

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
                LoadCallCount++;
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
                    prefab = new GameObject($"GatedPrefab_{key}", typeof(RectTransform), typeof(UIView));
                    prefab.SetActive(false);
                    prefab.hideFlags = HideFlags.DontSave;
                    _prefabs[key] = prefab;
                }

                return prefab;
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
                _gates.Clear();
            }
        }
    }
}
