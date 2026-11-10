using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    public sealed class Stage13DiagnosticsPlayModeTests
    {
        private UIManager _ui;
        private UIResourceService _resources;
        private UIDiagnostics _diagnostics;
        private Provider _provider;
        private Clock _clock;
        private UIDiagnosticsSampleSession _sample;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _clock = new Clock();
            _diagnostics = new UIDiagnostics(1024, 128, _clock);
            _provider = new Provider();
            _resources = new UIResourceService(diagnostics: _diagnostics);
            _resources.Packages.Register(_provider, true);
            _ui = new UIManager(diagnostics: _diagnostics);
            _ui.Initialize(_resources);
            Register<PageA>(UILayer.Normal, true);
            Register<PageB>(UILayer.Normal, false);
            Register<Widget>(UILayer.Fixed, false);
            Register<Dialog>(UILayer.Popup, false);
            yield return null;
        }

        private void Register<T>(UILayer layer, bool cache) where T : BaseContext =>
            _ui.Register<T>(new UIConfig { Id = typeof(T).Name, PrefabKey = typeof(T).Name,
                Layer = layer, CacheOnClose = cache, MaxPoolSize = cache ? 1 : 0,
                UseTransition = true, TransitionType = UITransitionType.Fade,
                ShowDuration = .02f, HideDuration = .02f });
        private static IEnumerator Await(Task task) => ResourceOwnershipPlayModeTests.Await(task);
        private static IEnumerator Canceled(Task task)
        {
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException, Is.True,
                task.Exception?.ToString() ?? task.Status.ToString());
        }
        private static IEnumerator Faulted(Task task)
        {
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsFaulted, Is.True);
        }
        private IEnumerator WaitForOperation(string kind)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (!_diagnostics.GetOperations().Any(e => e.Kind == kind) &&
                   Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(_diagnostics.GetOperations().Any(e => e.Kind == kind), Is.True, kind + " did not start.");
        }
        private UIDiagnosticSnapshot Snapshot(bool hierarchy = false) => _diagnostics.Capture(_ui,
            new UIPerformanceBudget(UIPerformanceTarget.Editor, operationTimeoutMs: 1000), hierarchy);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _provider.Complete();
            if (_sample != null) yield return Await(_sample.ShutdownAsync().AsTask());
            else
            {
                yield return Await(_ui.ShutdownAsync().AsTask());
                yield return Await(_resources.ShutdownAsync().AsTask());
                _diagnostics.Dispose();
            }
            _sample = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealNavigationContextLoadTransitionChainAndPoolOwnershipAreInspectable()
        {
            _provider.BlockNext = true;
            var navigation = _ui.Navigator.PushAsync<PageA>().AsTask();
            yield return WaitForOperation("NativeLoad");
            var pending = Snapshot();
            var nav = pending.Operations.Single(e => e.Kind == "Navigate");
            var open = pending.Operations.Single(e => e.Kind == "Open");
            var load = pending.Operations.Single(e => e.Kind == "Load");
            var native = pending.Operations.Single(e => e.Kind == "NativeLoad");
            Assert.That(open.ParentId, Is.EqualTo(nav.OperationId));
            Assert.That(load.ParentId, Is.EqualTo(open.OperationId));
            Assert.That(native.ParentId, Is.EqualTo(load.OperationId));
            Assert.That(pending.Contexts.Single().State, Is.EqualTo(UIContextState.Loading));
            Assert.That(pending.Resources.LoadingEntryCount, Is.EqualTo(1));
            Assert.That(pending.Resources.TotalWaiters, Is.EqualTo(1));
            var unrelated = _ui.OpenAsync<Widget>().AsTask();
            yield return Await(unrelated);
            Assert.That(_diagnostics.GetEvents().Single(e => e.Kind == "Open" && e.Page == nameof(Widget) &&
                e.Outcome == UIDiagnosticOutcome.Begin).ParentId.IsValid, Is.False);
            _clock.Seconds = .25;
            _provider.Complete();
            yield return Await(navigation);
            var live = Snapshot(true);
            var page = live.Contexts.Single(e => e.Page == nameof(PageA));
            Assert.That(page.HasInstanceLease, Is.True);
            Assert.That(page.SortingOrder.HasValue, Is.True);
            Assert.That(live.Navigation.Single(), Is.EqualTo(page.InstanceId));
            Assert.That(live.Hierarchy.ActiveGraphics, Is.GreaterThan(0));
            var transitions = live.Events.Where(e => e.Kind == "Transition" && e.Page == nameof(PageA)).ToArray();
            Assert.That(transitions.All(e => e.ParentId == open.OperationId), Is.True);
            Assert.That(transitions.Last().Outcome, Is.EqualTo(UIDiagnosticOutcome.Completed));
            var dialog = _ui.OpenAsync<Dialog>().AsTask();
            yield return Await(dialog);
            Assert.That(Snapshot().Modals.Single(), Is.EqualTo(Snapshot().Contexts.Single(e => e.Page == nameof(Dialog)).InstanceId));
            yield return Await(_ui.CloseAsync<Dialog>().AsTask());
            Assert.That(Snapshot().Events.Last(e => e.Kind == "Close").Outcome, Is.EqualTo(UIDiagnosticOutcome.Completed));
            yield return Await(_ui.CloseAsync<Widget>().AsTask());
            yield return Await(_ui.CloseAsync<PageA>().AsTask());
            var idle = Snapshot();
            Assert.That(idle.Pool.IdleLeasedCount, Is.EqualTo(1));
            Assert.That(idle.Issues, Is.Empty, "A legitimate pool lease is not an owner leak.");
            var before = _resources.GetDiagnostics().NativeLoadCount;
            yield return Await(_ui.OpenAsync<PageA>().AsTask());
            Assert.That(_resources.GetDiagnostics().NativeLoadCount, Is.EqualTo(before));
            var reused = Snapshot();
            Assert.That(reused.Contexts.Single(e => e.Page == nameof(PageA)).InstanceId, Is.EqualTo(page.InstanceId));
            var hit = reused.Events.Last(e => e.Phase == "PoolHit");
            var show = reused.Events.Last(e => e.Phase == "CachedShowStarted");
            Assert.That(show.OperationId, Is.EqualTo(hit.OperationId));
            Assert.That(show.Frame - hit.Frame, Is.LessThanOrEqualTo(1));
            yield return Await(_ui.CloseAsync<PageA>().AsTask());
            yield return Await(_ui.ClearPoolAsync<PageA>().AsTask());
            Assert.That(Snapshot().Events.Last(e => e.Kind == "Release").Outcome, Is.EqualTo(UIDiagnosticOutcome.Completed));
            var closed = Snapshot();
            Assert.That(closed.Resources.TotalLeases, Is.Zero);
            Assert.That(closed.Destroyed.Any(e => e.Context.InstanceId == page.InstanceId &&
                e.Context.State == UIContextState.Released), Is.True);
            Assert.That(closed.Operations, Is.Empty);
            foreach (var started in closed.Events.Where(e => e.Outcome == UIDiagnosticOutcome.Begin))
                Assert.That(closed.Events.Count(e => e.OperationId == started.OperationId &&
                    (e.Outcome == UIDiagnosticOutcome.Completed || e.Outcome == UIDiagnosticOutcome.Canceled ||
                     e.Outcome == UIDiagnosticOutcome.Faulted)), Is.EqualTo(1), started.ToString());
            TestContext.WriteLine("STAGE13_LIFECYCLE_TRACE_BEGIN\n" +
                string.Join("\n", closed.Events.Select(e => e.ToString())) + "\nSTAGE13_LIFECYCLE_TRACE_END");
        }

        [UnityTest]
        public IEnumerator CancellationKeepsNoncooperativeNativeLoadVisibleUntilActualCompletion_AndFaultsRetainChain()
        {
            _provider.BlockNext = true;
            using var cancel = new CancellationTokenSource();
            var open = _ui.OpenAsync<PageA>(cancellationToken: cancel.Token).AsTask();
            yield return WaitForOperation("NativeLoad");
            _clock.Seconds = 2;
            cancel.Cancel();
            yield return Canceled(open);
            var pending = Snapshot();
            Assert.That(pending.Resources.TotalLeases, Is.Zero);
            Assert.That(pending.Resources.LoadingEntryCount, Is.EqualTo(1));
            Assert.That(pending.Operations.Any(e => e.Kind == "NativeLoad"), Is.True);
            Assert.That(pending.Issues.Any(e => e.Code == "OwnerWorkPending"), Is.True);
            Assert.That(pending.Events.Any(e => e.Kind == "Open" && e.Outcome == UIDiagnosticOutcome.Canceled), Is.True);
            _provider.Complete();
            yield return null;
            Assert.That(Snapshot().Operations, Is.Empty);
            Assert.That(_resources.GetDiagnostics().IsNativeBalanced, Is.True);
            _provider.FailNext = true;
            var failure = _ui.OpenAsync<PageB>().AsTask();
            yield return Faulted(failure);
            var entry = Snapshot().Events.Last(e => e.Kind == "Open" && e.Outcome == UIDiagnosticOutcome.Faulted);
            StringAssert.Contains("injected native failure", entry.ExceptionChain);
            StringAssert.Contains("PageB", entry.Resource);
            Assert.That(entry.OperationId.IsValid, Is.True);
            Assert.That(entry.Phase, Does.Contain("open"));
            Assert.That(Snapshot().Issues, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DuplicateTopicOwnerHandlerAndDestroyedOwnerResidueUseActualSources_NotCountThresholds()
        {
            var scope = _ui.Scopes.CreateModule("Stage13 leak owner");
            yield return Await(scope.OpenAsync<Widget>().AsTask());
            var owner = _ui.Get<Widget>();
            var topic = new UIMessageTopic<int>("stage13.duplicate");
            Action<int> handler = _ => { };
            using var one = _ui.MessageCenter.Subscribe(topic, handler, owner: owner);
            using var two = _ui.MessageCenter.Subscribe(topic, handler, owner: owner);
            using var otherTopic = _ui.MessageCenter.Subscribe(new UIMessageTopic<int>("stage13.other"), handler, owner: owner);
            using var otherOwner = _ui.MessageCenter.Subscribe(topic, handler, owner: this);
            using var inputLock = _ui.InputLocks.Acquire(owner, "injected lock");
            var leaseTask = _resources.LoadOwnedAssetAsync<GameObject>(owner, UIResourceKey.Of<GameObject>("Extra")).AsTask();
            yield return Await(leaseTask);
            using var lease = leaseTask.Result;
            var duplicates = Snapshot().Subscriptions;
            Assert.That(duplicates.Count(e => e.DuplicateCount == 2), Is.EqualTo(2));
            Assert.That(duplicates.Single(e => e.Topic == "stage13.other").DuplicateCount, Is.EqualTo(1));
            yield return Await(_ui.CloseAsync<Widget>().AsTask());
            Assert.That(Snapshot().Subscriptions.Count(e => e.InstanceId != 0), Is.Zero,
                "The established Context destroy path already unsubscribes its owner.");
            using var lateOne = _ui.MessageCenter.Subscribe(topic, handler, owner: owner);
            using var lateTwo = _ui.MessageCenter.Subscribe(topic, handler, owner: owner);
            using var lateOther = _ui.MessageCenter.Subscribe(new UIMessageTopic<int>("stage13.other"), handler, owner: owner);
            var ended = Snapshot();
            Assert.That(ended.Contexts.Single(e => e.Page == nameof(Widget)).Scope, Is.EqualTo(scope.Identity.ToString()));
            Assert.That(ended.Operations.Single(e => e.Kind == "Lease").Scope, Is.EqualTo(scope.Identity.ToString()));
            Assert.That(ended.Issues.Count(e => e.Code == "OwnerSubscriptionLeak"), Is.EqualTo(3));
            Assert.That(ended.Issues.Count(e => e.Code == "OwnerLockLeak"), Is.EqualTo(1));
            Assert.That(ended.Issues.Count(e => e.Code == "OwnerLeaseLeak"), Is.EqualTo(1));
            var audit = ended.Destroyed.Single(e => e.Context.Page == nameof(Widget));
            Assert.That(audit.RemainingSubscriptions, Is.Zero);
            Assert.That(audit.RemainingLocks, Is.EqualTo(1));
            Assert.That(audit.Context.LifetimeCanceled, Is.True);
            one.Dispose(); two.Dispose(); otherTopic.Dispose(); otherOwner.Dispose();
            lateOne.Dispose(); lateTwo.Dispose(); lateOther.Dispose();
            inputLock.Dispose(); lease.Dispose();
            Assert.That(Snapshot().Issues, Is.Empty);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LegitimatePoolAndBorrowedGlobalLeaseAreNotLeaks_IncompleteRecordingNeverCertifiesZeroResidue()
        {
            yield return Await(_ui.OpenAsync<PageA>().AsTask());
            yield return Await(_ui.CloseAsync<PageA>().AsTask());
            var load = _resources.LoadAssetAsync<GameObject>("BorrowedGlobal").AsTask();
            yield return Await(load);
            using var borrowed = load.Result;
            var snapshot = Snapshot();
            Assert.That(snapshot.Contexts.Single(e => e.Page == nameof(PageA)).State, Is.EqualTo(UIContextState.Pooled));
            Assert.That(snapshot.Issues, Is.Empty);
            using var sampler = new UIProfilerSampler();
            _diagnostics.Enabled = false;
            snapshot = Snapshot();
            Assert.That(snapshot.RecordingComplete, Is.False);
            var report = sampler.Report(UIPerformanceBudget.For(UIPerformanceTarget.Editor), "Disabled diagnostics", snapshot);
            Assert.That(report.Checks.Single(e => e.Metric == "DestroyedOwnerResidue").Verdict,
                Is.EqualTo(UIPerformanceVerdict.Unavailable));
            _diagnostics.Enabled = true;
            borrowed.Dispose();
            yield return Await(_ui.ClearPoolAsync<PageA>().AsTask());
            Assert.That(Snapshot().Issues, Is.Empty);
        }

        [UnityTest]
        public IEnumerator TimeoutUsesInjectedClock_AndEndedScopeTaskIsPendingNotFalselyCleaned()
        {
            var owner = _ui.Scopes.CreateModule("pending diagnostic scope");
            yield return Await(owner.OpenAsync<Widget>().AsTask());
            var source = new UniTaskCompletionSource();
            var work = owner.RunAsync(_ => source.Task).AsTask();
            _clock.Seconds = 3;
            Assert.That(Snapshot().Issues.Any(e => e.Code == "OperationTimeout" && e.Detail.Contains("Task")), Is.True);
            var release = owner.ReleaseAsync().AsTask();
            while (_ui.IsOpen<Widget>()) yield return null;
            var pending = Snapshot();
            Assert.That(release.IsCompleted, Is.False);
            Assert.That(work.IsCompleted, Is.False);
            Assert.That(pending.Scopes.Single(e => e.Identity == owner.Identity).OperationCount, Is.EqualTo(1));
            Assert.That(pending.Operations.Single(e => e.Kind == "Task").CancellationRequested, Is.True);
            Assert.That(pending.Issues.Any(e => e.Code == "OwnerWorkPending"), Is.True);
            source.TrySetResult();
            yield return Canceled(work);
            yield return Await(release);
            Assert.That(Snapshot().Issues, Is.Empty);
            Assert.That(Snapshot().Operations, Is.Empty);
        }

        [UnityTest]
        public IEnumerator FailingAdapterCannotInterruptOpenCloseOrRelease_AndNoBusinessCallbacksRunDuringTrace()
        {
            var bad = new BadAdapter();
            using var attached = _diagnostics.Attach(bad);
            var scope = _ui.Scopes.CreateModule("adapter");
            yield return Await(scope.OpenAsync<Widget>().AsTask());
            yield return Await(scope.ReleaseAsync().AsTask());
            Assert.That(bad.Calls, Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            LogAssert.Expect(LogType.Warning, new Regex("Adapter quarantined:.*adapter failure", RegexOptions.Singleline));
            Assert.DoesNotThrow(_diagnostics.FlushAdapters);
            Assert.That(bad.Calls, Is.EqualTo(1));
            Assert.That(_diagnostics.AdapterFaultCount, Is.EqualTo(1));
            Assert.That(_diagnostics.ActiveCount, Is.Zero);
            Assert.That(_ui.Navigator.IsBusy, Is.False);
        }

        [UnityTest]
        public IEnumerator SharedOpenWithCanceledWaiterHasOneExecutionTraceAndNoDuplicateTerminals()
        {
            _provider.BlockNext = true;
            using var cancel = new CancellationTokenSource();
            var one = _ui.OpenAsync<PageA>(cancellationToken: cancel.Token).AsTask();
            var two = _ui.OpenAsync<PageA>().AsTask();
            cancel.Cancel();
            yield return Canceled(one);
            Assert.That(two.IsCompleted, Is.False);
            _provider.Complete();
            yield return Await(two);
            var events = Snapshot().Events;
            Assert.That(events.Count(e => e.Kind == "Open" && e.Outcome == UIDiagnosticOutcome.Begin), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == "Open" && e.Outcome == UIDiagnosticOutcome.Completed), Is.EqualTo(1));
            Assert.That(events.Count(e => e.Kind == "NativeLoad" && e.Outcome == UIDiagnosticOutcome.Begin), Is.EqualTo(1));
            yield return Await(_ui.CloseAsync<PageA>().AsTask());
            yield return Await(_ui.ClearPoolAsync<PageA>().AsTask());
            Assert.That(_diagnostics.ActiveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DisabledDiagnosticsPreservesUIBehaviorWithoutReadingClockOrRetainingEvents()
        {
            _diagnostics.Enabled = false;
            var reads = _clock.Reads;
            yield return Await(_ui.OpenAsync<PageA>().AsTask());
            yield return Await(_ui.CloseAsync<PageA>().AsTask());
            yield return Await(_ui.OpenAsync<PageA>().AsTask());
            yield return Await(_ui.CloseAsync<PageA>().AsTask());
            yield return Await(_ui.ClearPoolAsync<PageA>().AsTask());
            Assert.That(_diagnostics.GetEvents(), Is.Empty);
            Assert.That(_diagnostics.ActiveCount, Is.Zero);
            Assert.That(_clock.Reads, Is.EqualTo(reads));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        private IEnumerator UseSample()
        {
            yield return Await(_ui.ShutdownAsync().AsTask());
            yield return Await(_resources.ShutdownAsync().AsTask());
            _diagnostics.Dispose();
            _sample = new UIDiagnosticsSampleSession();
            _ui = _sample.UI; _resources = _sample.Resources; _diagnostics = _sample.Diagnostics;
            yield return Await(_sample.StartAsync().AsTask());
        }

        [UnityTest]
        public IEnumerator RealPanelRefreshFilterDetailsAndTwentyBackCyclesRestoreFocusWithoutSubscriptionsOrLocks()
        {
            yield return UseSample();
            var runtime = _ui.RootRuntime;
            for (var i = 0; i < 20; i++)
            {
                var panel = _ui.Get<UIDiagnosticsPanelContext>();
                Assert.That(panel, Is.Not.Null);
                _sample.Panel.Filter = "DiagnosticsSample";
                panel.Refresh();
                panel.NextPage();
                StringAssert.Contains("Selected page details:", panel.DisplayedText);
                StringAssert.Contains("Resources", panel.DisplayedText);
                Assert.That(_sample.Panel.SelectedInstance, Is.GreaterThan(0));
                Assert.That(_ui.Modals.Top, Is.SameAs(panel));
                Assert.That(runtime.EventSystem.currentSelectedGameObject, Is.SameAs(panel.DefaultFocus));
                Assert.That(_ui.Input.RequestBack(), Is.True);
                while (_ui.IsOpen<UIDiagnosticsPanelContext>()) yield return null;
                Assert.That(_ui.Modals.Count, Is.Zero);
                Assert.That(_ui.InputLocks.ActiveLockCount, Is.Zero);
                Assert.That(_ui.MessageCenter.ListenerCount, Is.Zero);
                Assert.That(runtime.EventSystem.currentSelectedGameObject, Is.SameAs(_ui.Get<DiagnosticsSamplePage>().DefaultFocus));
                Assert.That(_ui.RootRuntime, Is.SameAs(runtime));
                Assert.That(_resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
                if (i != 19) yield return Await(_sample.ShowPanelAsync().AsTask());
            }
            Assert.That(_diagnostics.Capture(_ui).Issues, Is.Empty);
            Assert.That(_diagnostics.GetEvents().Count, Is.LessThanOrEqualTo(_diagnostics.EventCapacity));
        }

        [UnityTest]
        public IEnumerator RunnableSampleInjectsActualLeaksDuplicatesAndPendingTask_ThenFullyCleansThem()
        {
            yield return UseSample();
            yield return Await(_ui.CloseAsync<UIDiagnosticsPanelContext>().AsTask());
            yield return Await(_sample.InjectFindingsAsync().AsTask());
            var snapshot = _diagnostics.Capture(_ui);
            foreach (var code in new[] { "OwnerSubscriptionLeak", "DuplicateSubscription", "OwnerLockLeak", "OwnerLeaseLeak", "OwnerWorkPending" })
                Assert.That(snapshot.Issues.Any(e => e.Code == code), Is.True, code);
            TestContext.WriteLine("STAGE13_INJECTED_FINDINGS_BEGIN\n" +
                string.Join("\n", snapshot.Issues) + "\nSTAGE13_INJECTED_FINDINGS_END");
            yield return Await(_sample.ClearFindingsAsync().AsTask());
            _ui.Get<UIDiagnosticsPanelContext>().Refresh();
            Assert.That(_ui.Get<UIDiagnosticsPanelContext>().DisplayedText.Length, Is.LessThanOrEqualTo(14000));
            Canvas.ForceUpdateCanvases();
            Assert.That(_diagnostics.Capture(_ui).Issues, Is.Empty);
            Assert.That(_ui.MessageCenter.ListenerCount, Is.Zero);
            Assert.That(_ui.InputLocks.ActiveLockCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ActualProfilerRecorderSamplesAndUnavailableCountersProduceTargetSpecificHonestReports()
        {
            yield return UseSample();
            var wasProfiling = UnityEngine.Profiling.Profiler.enabled;
            using var profilerSession = new BindingToken(() => UnityEngine.Profiling.Profiler.enabled = wasProfiling);
            UnityEngine.Profiling.Profiler.enabled = true;
            var marker = new ProfilerMarker("YUIFramework.Stage13Probe");
            marker.Begin(); marker.End();
            var sampler = _sample.Sampler;
            sampler.ObserveMarker("RealProbe", "YUIFramework.Stage13Probe");
            sampler.ObserveMarker("UnsupportedProbe", "NoSuchUnityMetric.Stage13");
            Canvas.ForceUpdateCanvases();
            yield return null;
            sampler.RefreshAvailableCounters();
            var available = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(available);
            TestContext.WriteLine("STAGE13_AVAILABLE_UI_MARKERS\n" + string.Join("\n", available
                .Select(Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription)
                .Where(e => e.Name.IndexOf("layout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Name.IndexOf("canvas", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => e.Category.Name + "/" + e.Name)));
            var rebuiltText = _ui.Get<UIDiagnosticsPanelContext>().ViewObject.GetComponentInChildren<Text>();
            for (var i = 0; i < 4; i++)
            {
                marker.Begin();
                Thread.SpinWait(25000);
                marker.End();
                rebuiltText.text = "Real canvas rebuild probe " + i;
                Canvas.ForceUpdateCanvases();
                yield return null;
            }
            var metrics = sampler.Capture();
            var real = metrics.Single(e => e.Name == "RealProbe");
            Assert.That(real.Available, Is.True, real.Reason);
            Assert.That(real.Value, Is.GreaterThan(0));
            Assert.That(real.Unit, Is.EqualTo("ms"));
            Assert.That(real.Calls, Is.GreaterThan(0));
            Assert.That(metrics.Single(e => e.Name == "LayoutRebuild").Available, Is.True,
                "Unity 2022.3's enumerated UI Layout/Layout marker should be sampled after warmup.");
            Assert.That(metrics.Single(e => e.Name == "LayoutRebuild").Source, Is.EqualTo("UI Layout/Layout"));
            Assert.That(metrics.Single(e => e.Name == "CanvasRebuild").Available, Is.True);
            Assert.That(metrics.Single(e => e.Name == "CanvasRebuild").Calls, Is.GreaterThan(0),
                "The report must include real dirty-Graphic canvas work, not only an idle zero sample.");
            var unsupported = metrics.Single(e => e.Name == "UnsupportedProbe");
            Assert.That(unsupported.Available, Is.False);
            Assert.That(unsupported.Value, Is.Null);
            Assert.That(unsupported.Reason, Is.Not.Empty);
            var snapshot = _diagnostics.Capture(_ui, includeHierarchy: true);
            var editor = sampler.Report(UIPerformanceBudget.For(UIPerformanceTarget.Editor),
                "Unity2022.3 Editor/Mono headless: real diagnostics panel + uGUI layout + native marker probe", snapshot);
            var android = sampler.Report(UIPerformanceBudget.For(UIPerformanceTarget.LowEndAndroid),
                "Same Editor observation; low-end Android hardware NOT measured", snapshot,
                UIPerformanceTarget.LowEndAndroid);
            var mobile = sampler.Report(UIPerformanceBudget.For(UIPerformanceTarget.MainstreamMobile),
                "Same Editor observation; mainstream mobile hardware NOT measured", snapshot);
            Assert.That(editor.TargetMatches, Is.True);
            Assert.That(android.TargetMatches, Is.False);
            Assert.That(android.Checks.Any(e => e.Verdict == UIPerformanceVerdict.Pass), Is.False);
            Assert.That(mobile.Checks.Any(e => e.Verdict == UIPerformanceVerdict.Pass), Is.False);
            Assert.That(editor.Checks.Single(e => e.Metric == "ProductionSynchronousLoads").Verdict,
                Is.EqualTo(UIPerformanceVerdict.Unavailable));
            TestContext.WriteLine("STAGE13_PERFORMANCE_REPORT_BEGIN\n" + editor.ToText() +
                "\n" + android.ToText() + "\n" + mobile.ToText() + "\nSTAGE13_PERFORMANCE_REPORT_END");
            sampler.Dispose();
            Assert.That(sampler.CounterCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => sampler.Capture());
        }

        [UnityTest]
        public IEnumerator TransitionCancellationAndRollbackKeepCorrelatedTerminalOutcome()
        {
            yield return Await(_ui.OpenAsync<PageA>().AsTask());
            _ui.TryGetConfig(typeof(PageA), out var config);
            config.HideDuration = 1;
            var close = _ui.CloseAsync<PageA>().AsTask();
            while (_ui.Get<PageA>().State != UIContextState.Hiding) yield return null;
            var animations = Snapshot().Transitions;
            Assert.That(animations.Count, Is.EqualTo(1));
            Assert.That(_ui.RequestTransitionInterruption<PageA>(UITransitionInterruption.Interrupt), Is.True);
            yield return Canceled(close);
            Assert.That(_ui.Get<PageA>().State, Is.EqualTo(UIContextState.Opened));
            var events = Snapshot().Events;
            var transition = events.Last(e => e.Kind == "Transition" && e.Outcome == UIDiagnosticOutcome.Canceled);
            Assert.That(events.Last(e => e.Kind == "Close").OperationId, Is.EqualTo(transition.ParentId));
            Assert.That(events.Last(e => e.Kind == "Close").Outcome, Is.EqualTo(UIDiagnosticOutcome.Canceled));
            config.HideDuration = .01f;
        }

        [UnityTest]
        public IEnumerator CallbackCancellationWithoutCanceledTokenStillRecordsCanceledRatherThanCompleted()
        {
            yield return Canceled(_ui.OpenAsync<Dialog>("cancel-show").AsTask());
            Assert.That(Snapshot().Events.Last(e => e.Kind == "Open").Outcome, Is.EqualTo(UIDiagnosticOutcome.Canceled));
            Assert.That(_ui.IsOpen<Dialog>(), Is.False);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        public sealed class PageA : BasePageContext
        {
            protected override void HandleInit()
            {
                var graphic = new GameObject("ObservedGraphic", typeof(RectTransform), typeof(Image));
                graphic.transform.SetParent(ViewObject.transform, false);
            }
        }
        public sealed class PageB : BasePageContext { }
        public sealed class Widget : BaseWidgetContext { }
        public sealed class Dialog : BaseDialogContext
        {
            protected override void HandleShow(object args)
            {
                if (Equals(args, "cancel-show")) throw new OperationCanceledException("Injected callback cancellation.");
            }
        }
        private sealed class Clock : IUIDiagnosticClock
        {
            private double _seconds;
            public int Reads;
            public double Seconds { get { Reads++; return _seconds; } set => _seconds = value; }
            public int Frame => Time.frameCount;
        }
        private sealed class BadAdapter : IUIDiagnosticsAdapter
        {
            public int Calls;
            public void Write(UIDiagnosticEvent entry) { Calls++; throw new InvalidOperationException("adapter failure"); }
        }
        private sealed class Provider : IUIResourceProvider
        {
            private readonly List<GameObject> _assets = new List<GameObject>();
            private UniTaskCompletionSource<IUINativeAssetHandle> _pending;
            public string PackageName => "Stage13Tests";
            public bool BlockNext, FailNext;
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                if (FailNext)
                {
                    FailNext = false;
                    return UniTask.FromException<IUINativeAssetHandle>(new InvalidOperationException("injected native failure"));
                }
                if (BlockNext)
                {
                    BlockNext = false;
                    _pending = new UniTaskCompletionSource<IUINativeAssetHandle>();
                    return _pending.Task;
                }
                return UniTask.FromResult(NewHandle());
            }
            private IUINativeAssetHandle NewHandle()
            {
                var asset = new GameObject("Stage13TestPrefab", typeof(RectTransform), typeof(UIView));
                asset.SetActive(false); _assets.Add(asset);
                return new Handle(asset);
            }
            public void Complete()
            {
                var pending = _pending; _pending = null;
                pending?.TrySetResult(NewHandle());
            }
            public UniTask ShutdownAsync()
            {
                foreach (var asset in _assets) UnityEngine.Object.Destroy(asset);
                _assets.Clear();
                return UniTask.CompletedTask;
            }
            private sealed class Handle : IUINativeAssetHandle
            {
                public Handle(GameObject asset) { Asset = asset; }
                public UnityEngine.Object Asset { get; private set; }
                public bool IsValid => Asset != null;
                public void Release() => Asset = null;
            }
        }
    }
}
