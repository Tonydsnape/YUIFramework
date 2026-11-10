using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YUIFramework.Hotfix.AotSample;
using Object = UnityEngine.Object;

namespace YUIFramework.Tests
{
    public sealed class Stage15HotfixSampleLifecycleTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _host, _template, _root, _events;
        private UIManager _ui;

        [UnityTest]
        public IEnumerator Stage15_ActualStartDestroyedWhilePendingDrainsBeforeShutdown() => Run(true);

        [UnityTest]
        public IEnumerator Stage15_ActualStartCompletesNormallyAndDestroyReleasesOwnedObjects() => Run(false);

        private IEnumerator Run(bool destroyWhilePending)
        {
            var errors = new List<string>();
            var unobserved = new List<Exception>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add(message);
            };
            Action<Exception> onUnobserved = error => unobserved.Add(error);
            var previousIgnore = LogAssert.ignoreFailingMessages;
            // Capture and assert ALL errors, allowing the regression to also observe
            // ordering/cleanup after a producer failure instead of aborting mid-frame.
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += log;
            UniTaskScheduler.UnobservedTaskException += onUnobserved;
            try
            {
                _host = new GameObject("Hotfix lifecycle test");
                _host.SetActive(false);
                var sample = _host.AddComponent<HotfixBoundarySample>();
                _ui = (UIManager)typeof(HotfixBoundarySample).GetField("_ui", PrivateInstance).GetValue(sample);
                var runField = typeof(HotfixBoundarySample).GetField("_run", PrivateInstance);
                var views = typeof(HotfixBoundarySample).GetField("_views", PrivateInstance).GetValue(sample);
                var templateField = views.GetType().GetField("_template", PrivateInstance);
                bool Pending()
                {
                    var run = runField.GetValue(sample);
                    if (run is Task task) return !task.IsCompleted;
                    if (run is UniTask unitask) return unitask.Status == UniTaskStatus.Pending;
                    throw new InvalidOperationException("Unknown sample run backing type.");
                }
                var started = false;
                var observed = false;
                var pendingAtDestroyBoundary = false;
                var shutdownBeforeRunSettled = false;
                var probe = _host.AddComponent<Stage15DestroyAfterStartProbe>();
                probe.DestroyHost = destroyWhilePending;
                probe.Started = () =>
                {
                    observed = true;
                    started = _ui.IsInitialized;
                    pendingAtDestroyBoundary = Pending();
                    _root = _ui.RootRuntime.Root.gameObject;
                    _events = _ui.RootRuntime.EventSystem.gameObject;
                };
                probe.Destroyed = () => shutdownBeforeRunSettled = Pending() &&
                    (typeof(UIManager).GetField("_shutdownTask", PrivateInstance).GetValue(_ui) != null ||
                     !_ui.IsInitialized);
                _host.SetActive(true);
                for (var frame = 0; frame < 16 && !observed; frame++) yield return null;
                Assert.IsTrue(observed && started, "Real Unity Start must initialize before the later-order probe.");
                Assert.IsTrue(pendingAtDestroyBoundary, "Run must genuinely be pending; no sleep or completed-task proxy.");
                if (!destroyWhilePending)
                {
                    for (var frame = 0; frame < 16 && Pending(); frame++) yield return null;
                    Assert.IsFalse(Pending());
                    Assert.IsNull(sample.LastFailure);
                    Assert.AreEqual(UIContextState.Opened, sample.Page.State);
                    _template = (GameObject)templateField.GetValue(views);
                    Assert.IsTrue(_template, "Normal startup must create the real code-built template.");
                    Object.DestroyImmediate(_host);
                }
                for (var frame = 0; frame < 16; frame++)
                {
                    _template = (GameObject)templateField.GetValue(views);
                    if (!(Pending() || _ui.IsInitialized || _template || _root || _events)) break;
                    yield return null;
                }
                TestContext.WriteLine($"ActualStart={started}, pendingAtBoundary={pendingAtDestroyBoundary}, " +
                    $"shutdownBeforeRunSettled={shutdownBeforeRunSettled}, templateCreated={!ReferenceEquals(_template, null)}, " +
                    $"producerErrors={errors.Count}, unobserved={unobserved.Count}");
                Assert.IsEmpty(errors, string.Join("\n", errors) +
                    "\nshutdownBeforeRunSettled=" + shutdownBeforeRunSettled);
                Assert.IsFalse(shutdownBeforeRunSettled, "Cleanup must drain the run BEFORE shutdown/disposal.");
                Assert.IsFalse(Pending(), "Destroy cancellation must settle the actual run.");
                Assert.IsFalse(_ui.IsInitialized, "UI shutdown must complete.");
                if (sample.Page != null) Assert.AreEqual(UIContextState.Released, sample.Page.State);
                Assert.IsFalse(_template, "Owned view template must be destroyed.");
                Assert.IsFalse(_root, "Owned UI root must be destroyed.");
                Assert.IsFalse(_events, "Owned EventSystem must be destroyed.");
                Assert.IsEmpty(unobserved, "No UniTask continuation/Unobserved failures.");
            }
            finally
            {
                Application.logMessageReceived -= log;
                UniTaskScheduler.UnobservedTaskException -= onUnobserved;
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup() => CleanupAsync().ToCoroutine();
        private async UniTask CleanupAsync()
        {
            if (_host) Object.DestroyImmediate(_host);
            if (_ui != null) await _ui.ShutdownAsync();
            await UniTask.NextFrame();
            if (_template) Object.DestroyImmediate(_template);
            _ui = null; _host = _template = _root = _events = null;
        }
    }
}
