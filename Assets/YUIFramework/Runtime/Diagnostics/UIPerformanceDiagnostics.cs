using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace YUIFramework
{
    public enum UIPerformanceTarget { LowEndAndroid, MainstreamMobile, Editor }
    public enum UIPerformanceVerdict { Pass, Exceeded, Unavailable, NotValidated }

    public sealed class UIPerformanceBudget
    {
        public UIPerformanceBudget(UIPerformanceTarget target, double frameMs = 16.667,
            double canvasMs = 2, double layoutMs = 1, long gcBytesPerFrame = 0,
            int graphics = 1500, int raycastTargets = 200, double operationTimeoutMs = 10000,
            int cachedShowFrames = 1)
        {
            if (!Enum.IsDefined(typeof(UIPerformanceTarget), target) || !FinitePositive(frameMs) ||
                !FinitePositive(canvasMs) || !FinitePositive(layoutMs) || !FinitePositive(operationTimeoutMs) ||
                gcBytesPerFrame < 0 || graphics < 0 || raycastTargets < 0 || cachedShowFrames < 0)
                throw new ArgumentOutOfRangeException(nameof(frameMs));
            Target = target; FrameMs = frameMs; CanvasMs = canvasMs; LayoutMs = layoutMs;
            GcBytesPerFrame = gcBytesPerFrame; Graphics = graphics; RaycastTargets = raycastTargets;
            OperationTimeoutMs = operationTimeoutMs; CachedShowFrames = cachedShowFrames;
        }
        private static bool FinitePositive(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
        public UIPerformanceTarget Target { get; }
        public double FrameMs { get; }
        public double CanvasMs { get; }
        public double LayoutMs { get; }
        public long GcBytesPerFrame { get; }
        public int Graphics { get; }
        public int RaycastTargets { get; }
        public double OperationTimeoutMs { get; }
        public int CachedShowFrames { get; }
        public static UIPerformanceBudget For(UIPerformanceTarget target)
        {
            switch (target)
            {
                case UIPerformanceTarget.LowEndAndroid: return new UIPerformanceBudget(target);
                case UIPerformanceTarget.MainstreamMobile:
                    return new UIPerformanceBudget(target, canvasMs: 1.5, layoutMs: .75, graphics: 2500, raycastTargets: 400);
                case UIPerformanceTarget.Editor:
                    return new UIPerformanceBudget(target, canvasMs: 8, layoutMs: 4, graphics: 4000, raycastTargets: 800);
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }
    }

    public sealed class UIProfilerMetric
    {
        internal UIProfilerMetric(string name, string source, bool available, double? value,
            string unit, long? calls, string reason)
        { Name = name; Source = source; Available = available; Value = value; Unit = unit; Calls = calls; Reason = reason; }
        public string Name { get; }
        public string Source { get; }
        public bool Available { get; }
        public double? Value { get; }
        public string Unit { get; }
        public long? Calls { get; }
        public string Reason { get; }
    }

    public sealed class UIPerformanceCheck
    {
        internal UIPerformanceCheck(string metric, double? value, double threshold, string unit,
            bool matchesTarget, string reason)
        {
            Metric = metric; Value = value; Threshold = threshold; Unit = unit; Reason = reason;
            Verdict = !value.HasValue ? UIPerformanceVerdict.Unavailable :
                !matchesTarget ? UIPerformanceVerdict.NotValidated :
                value.Value <= threshold ? UIPerformanceVerdict.Pass : UIPerformanceVerdict.Exceeded;
        }
        public string Metric { get; }
        public double? Value { get; }
        public double Threshold { get; }
        public string Unit { get; }
        public UIPerformanceVerdict Verdict { get; }
        public string Reason { get; }
    }

    public sealed class UIPerformanceReport
    {
        internal UIPerformanceReport(UIPerformanceBudget budget, string scenario, string environment,
            bool targetMatches, IReadOnlyList<UIProfilerMetric> samples, IReadOnlyList<UIPerformanceCheck> checks)
        { Budget = budget; Scenario = scenario; Environment = environment; TargetMatches = targetMatches;
            Samples = samples; Checks = checks; }
        public UIPerformanceBudget Budget { get; }
        public string Scenario { get; }
        public string Environment { get; }
        public bool TargetMatches { get; }
        public IReadOnlyList<UIProfilerMetric> Samples { get; }
        public IReadOnlyList<UIPerformanceCheck> Checks { get; }
        public string ToText()
        {
            var text = new StringBuilder();
            text.AppendLine($"Target={Budget.Target}; scenario={Scenario}; environment={Environment}; targetMatches={TargetMatches}");
            text.AppendLine("Snapshot, NOT device/FPS certification. Last recorder samples may predate capture for sparse markers.");
            text.AppendLine("Full-frame GC includes Editor/test runner/diagnostic capture; not the stage9/10 local synchronous GC measurement.");
            foreach (var sample in Samples)
                text.AppendLine($"Sample {sample.Name}: " +
                    (sample.Available ? sample.Value?.ToString("F3", CultureInfo.InvariantCulture) + " " + sample.Unit :
                        "Unavailable") + $" source={sample.Source} calls={sample.Calls} {sample.Reason}");
            foreach (var check in Checks)
                text.AppendLine($"{check.Verdict}: {check.Metric} value=" +
                    (check.Value?.ToString("F3", CultureInfo.InvariantCulture) ?? "Unavailable") +
                    $" threshold<={check.Threshold.ToString(CultureInfo.InvariantCulture)} {check.Unit}; {check.Reason}");
            return text.ToString();
        }
    }

    public sealed class UIProfilerSampler : IDisposable
    {
        private sealed class Counter
        {
            internal string Metric, Source, Reason;
            internal string[] Names;
            internal ProfilerRecorder Recorder;
        }
        private readonly List<Counter> _counters = new List<Counter>();
        private bool _disposed;
        public static bool DevelopmentAllowed
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        public UIProfilerSampler()
        {
            if (!DevelopmentAllowed) throw new InvalidOperationException("UI profiling requires Editor or Development Build.");
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            Add(handles, "MainThread", "Main Thread");
            Add(handles, "GCAllocatedInFrame", "GC Allocated In Frame");
            Add(handles, "CanvasRebuild", "Canvas.BuildBatch", "Canvas.UpdateBatches");
            Add(handles, "LayoutRebuild", "Layout", "UI.Layout", "CanvasUpdate.Layout", "Layout.Rebuild");
            Add(handles, "DrawCalls", "Draw Calls Count");
            Add(handles, "Batches", "Batches Count");
        }
        public int CounterCount => _counters.Count;
        public void ObserveMarker(string metric, string marker)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIProfilerSampler));
            if (string.IsNullOrWhiteSpace(metric) || string.IsNullOrWhiteSpace(marker))
                throw new ArgumentException("Metric and marker names are required.");
            if (_counters.Count >= 16) throw new InvalidOperationException("The sampler supports at most 16 metrics.");
            foreach (var counter in _counters)
                if (counter.Metric == metric) throw new InvalidOperationException("The metric is already registered.");
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            Add(handles, metric, marker);
        }

        private void Add(List<ProfilerRecorderHandle> handles, string metric, params string[] names)
        {
            var counter = new Counter { Metric = metric, Names = names, Source = string.Join(" | ", names),
                Reason = "Counter/marker not enumerated by this Unity runtime." };
            TryStart(counter, handles);
            _counters.Add(counter);
        }

        public void RefreshAvailableCounters()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIProfilerSampler));
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var counter in _counters)
                if (!counter.Recorder.Valid) TryStart(counter, handles);
        }

        private static void TryStart(Counter counter, List<ProfilerRecorderHandle> handles)
        {
            foreach (var name in counter.Names)
            {
                foreach (var handle in handles)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handle);
                    if (description.Name != name || (counter.Metric == "LayoutRebuild" && name == "Layout" &&
                        description.Category.Name != "UI Layout")) continue;
                    counter.Source = description.Category.Name + "/" + description.Name;
                    try
                    {
                        counter.Recorder = ProfilerRecorder.StartNew(description.Category, name, 1,
                            ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame);
                        counter.Reason = counter.Recorder.Valid ? "" : "ProfilerRecorder is invalid.";
                    }
                    catch (Exception exception)
                    {
                        counter.Reason = "ProfilerRecorder failed: " + UIDiagnostics.ExceptionText(exception);
                    }
                    return;
                }
            }
        }

        public IReadOnlyList<UIProfilerMetric> Capture()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIProfilerSampler));
            var metrics = new List<UIProfilerMetric>();
            foreach (var counter in _counters)
            {
                var recorder = counter.Recorder;
                if (!recorder.Valid || recorder.Count == 0)
                {
                    metrics.Add(new UIProfilerMetric(counter.Metric, counter.Source, false, null, "",
                        null, string.IsNullOrEmpty(counter.Reason) ? "No completed recorder sample yet." : counter.Reason));
                    continue;
                }
                var unit = recorder.UnitType.ToString();
                var value = (double)recorder.LastValue;
                if (unit == "TimeNanoseconds") { unit = "ms"; value /= 1000000; }
                metrics.Add(new UIProfilerMetric(counter.Metric, counter.Source, true, value, unit,
                    recorder.GetSample(recorder.Count - 1).Count, counter.Reason));
            }
            return metrics.AsReadOnly();
        }

        public UIPerformanceReport Report(UIPerformanceBudget budget, string scenario,
            UIDiagnosticSnapshot snapshot, UIPerformanceTarget? declaredDeviceClass = null)
        {
            if (budget == null) throw new ArgumentNullException(nameof(budget));
            if (string.IsNullOrWhiteSpace(scenario)) throw new ArgumentException("A scenario is required.", nameof(scenario));
            var matches = Application.isEditor ? budget.Target == UIPerformanceTarget.Editor :
                declaredDeviceClass == budget.Target &&
                ((budget.Target == UIPerformanceTarget.LowEndAndroid && Application.platform == RuntimePlatform.Android) ||
                 (budget.Target == UIPerformanceTarget.MainstreamMobile &&
                  (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)));
            var samples = Capture();
            var checks = new List<UIPerformanceCheck>();
            foreach (var sample in samples)
            {
                double? threshold = sample.Name == "MainThread" ? budget.FrameMs :
                    sample.Name == "GCAllocatedInFrame" ? budget.GcBytesPerFrame :
                    sample.Name == "CanvasRebuild" ? budget.CanvasMs :
                    sample.Name == "LayoutRebuild" ? budget.LayoutMs : (double?)null;
                if (!threshold.HasValue) continue;
                var expectedUnit = sample.Name == "GCAllocatedInFrame" ? "Bytes" : "ms";
                checks.Add(new UIPerformanceCheck(sample.Name,
                    sample.Unit == expectedUnit ? sample.Value : null, threshold.Value, expectedUnit, matches,
                    sample.Source + "; " + sample.Reason));
            }
            checks.Add(new UIPerformanceCheck("ActiveGraphics", snapshot?.Hierarchy?.ActiveGraphics,
                budget.Graphics, "count", matches, "On-demand hierarchy count, not rebuild timing."));
            checks.Add(new UIPerformanceCheck("ActiveRaycastTargets", snapshot?.Hierarchy?.ActiveRaycastTargets,
                budget.RaycastTargets, "count", matches, "On-demand active Graphic.raycastTarget count."));
            double? loadMs = null, cachedFrames = null;
            if (snapshot != null)
            {
                var starts = new Dictionary<UIOperationId, int>();
                foreach (var entry in snapshot.Events)
                {
                    if (entry.Kind == "Load" && entry.Outcome == UIDiagnosticOutcome.Completed)
                        loadMs = Math.Max(loadMs ?? 0, entry.DurationMs);
                    if (entry.Phase == "PoolHit") starts[entry.OperationId] = entry.Frame;
                    if (entry.Phase == "CachedShowStarted" && starts.TryGetValue(entry.OperationId, out var frame))
                        cachedFrames = Math.Max(cachedFrames ?? 0, entry.Frame - frame);
                }
            }
            checks.Add(new UIPerformanceCheck("MaxCompletedLoad", loadMs, budget.OperationTimeoutMs, "ms", matches,
                "Retained async request durations, not frame/render time or network certification."));
            checks.Add(new UIPerformanceCheck("CachedOpenToShow", cachedFrames, budget.CachedShowFrames, "frames", matches,
                "Pool-hit execution to OnShow entry, excludes navigation/type queue wait and transition completion."));
            var residue = snapshot == null || !snapshot.RecordingComplete ? (double?)null : 0;
            if (residue.HasValue)
                foreach (var issue in snapshot.Issues)
                    if (issue.Code == "OwnerSubscriptionLeak" || issue.Code == "OwnerLockLeak" ||
                        issue.Code == "OwnerLeaseLeak" || issue.Code == "OwnerWorkPending") residue++;
            checks.Add(new UIPerformanceCheck("DestroyedOwnerResidue", residue, 0, "findings", matches,
                "Only observed owners/leases/tasks; unavailable if recording is disabled/incomplete. Unregistered external work is outside coverage."));
            checks.Add(new UIPerformanceCheck("ProductionSynchronousLoads", null, 0, "calls", matches,
                "Unavailable: async API instrumentation cannot prove a custom provider never blocks. Audit provider/Profiler separately."));
            var environment = $"{(Application.isEditor ? "Editor" : Application.platform.ToString())}; " +
                $"Unity={Application.unityVersion}; model={SystemInfo.deviceModel}; GPU={SystemInfo.graphicsDeviceType}; " +
                $"frame={Time.frameCount}; scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}";
            return new UIPerformanceReport(budget, scenario, environment, matches, samples, checks.AsReadOnly());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var counter in _counters) counter.Recorder.Dispose();
            _counters.Clear();
        }
    }
}
