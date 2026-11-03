using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YUIFramework.Tests
{
    public sealed class Stage13DiagnosticsEditModeTests
    {
        [Test]
        public void RingAndActiveStorageAreBounded_AndTerminalEventsAreExactlyOnce()
        {
            var clock = new Clock();
            using var diagnostics = new UIDiagnostics(4, 1, clock);
            var first = diagnostics.Begin("Open", page: "Page", resource: "package:page");
            Assert.That(diagnostics.Begin("Load"), Is.Null);
            clock.Seconds = .25;
            first.Mark("Initializing");
            first.Dispose();
            first.Dispose();
            Assert.That(diagnostics.ActiveCount, Is.Zero);
            Assert.That(diagnostics.DroppedOperations, Is.EqualTo(1));
            for (var i = 0; i < 10; i++) diagnostics.Begin("Close")?.Dispose();
            Assert.That(diagnostics.GetEvents().Count, Is.EqualTo(4));
            Assert.That(diagnostics.DroppedEvents, Is.EqualTo(19));
            var events = diagnostics.GetEvents();
            Assert.That(events.Select(e => e.Sequence), Is.Ordered);
            Assert.That(events.Count(e => e.Outcome == UIDiagnosticOutcome.Completed), Is.EqualTo(2));
            Assert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<UIDiagnosticEvent>)events).Clear());
        }

        [Test]
        public void DisabledRecordingDoesNotReadClock_AlreadyRunningWorkStillTerminates()
        {
            var clock = new Clock();
            using var diagnostics = new UIDiagnostics(clock: clock);
            var span = diagnostics.Begin("Task");
            diagnostics.Enabled = false;
            var reads = clock.Reads;
            Assert.That(diagnostics.Begin("Open"), Is.Null);
            Assert.That(clock.Reads, Is.EqualTo(reads));
            span.Dispose();
            Assert.That(diagnostics.ActiveCount, Is.Zero);
            Assert.That(diagnostics.GetEvents().Last().Outcome, Is.EqualTo(UIDiagnosticOutcome.Completed));
        }

        [Test]
        public void AdaptersRunOnlyAtExplicitFlush_FaultIsQuarantinedAndOtherAdaptersStillReceive()
        {
            using var diagnostics = new UIDiagnostics(clock: new Clock());
            var bad = new Adapter { Fail = true };
            var good = new Adapter();
            using var one = diagnostics.Attach(bad);
            using var two = diagnostics.Attach(good);
            diagnostics.Begin("Open").Dispose();
            Assert.That(bad.Calls, Is.Zero);
            LogAssert.Expect(LogType.Warning, new Regex("Adapter quarantined:.*adapter failure", RegexOptions.Singleline));
            diagnostics.FlushAdapters();
            Assert.That(bad.Calls, Is.EqualTo(1));
            Assert.That(good.Calls, Is.EqualTo(2));
            Assert.That(diagnostics.AdapterFaultCount, Is.EqualTo(1));
            diagnostics.FlushAdapters();
            Assert.That(good.Calls, Is.EqualTo(3), "The quarantined failure is itself observable on the next flush.");
            Assert.That(diagnostics.AdapterCount, Is.EqualTo(1));
        }

        [Test]
        public void NestedIdsCancellationFaultChainAndMonotonicDurationsArePreserved()
        {
            var clock = new Clock();
            using var diagnostics = new UIDiagnostics(clock: clock);
            using var cancellation = new CancellationTokenSource();
            var navigation = diagnostics.Begin("Navigate");
            var open = diagnostics.Begin("Open", parent: navigation.Id);
            var load = diagnostics.Begin("Load", parent: open.Id, resource: "pkg:page", token: cancellation.Token);
            clock.Seconds = 2;
            cancellation.Cancel();
            load.Dispose();
            open.Fail(new InvalidOperationException("outer", new ArgumentException("inner")));
            open.Dispose(); navigation.Dispose();
            var events = diagnostics.GetEvents();
            Assert.That(events.Single(e => e.OperationId == load.Id && e.Outcome == UIDiagnosticOutcome.Canceled)
                .DurationMs, Is.EqualTo(2000));
            var failure = events.Single(e => e.Outcome == UIDiagnosticOutcome.Faulted);
            Assert.That(failure.ParentId, Is.EqualTo(navigation.Id));
            StringAssert.Contains("outer", failure.ExceptionChain);
            StringAssert.Contains("inner", failure.ExceptionChain);
            Assert.That(diagnostics.ActiveCount, Is.Zero);
        }

        [Test]
        public void BrokenClockDoesNotEscapeInstrumentation_AndIncompleteEvidenceIsExplicit()
        {
            var clock = new Clock { Seconds = double.NaN };
            using var diagnostics = new UIDiagnostics(clock: clock);
            LogAssert.Expect(LogType.Warning, new Regex("Clock failure; measurements are incomplete"));
            Assert.That(diagnostics.Begin("Open"), Is.Null);
            Assert.That(diagnostics.ClockFaultCount, Is.EqualTo(1));
        }

        [Test]
        public void BrokenExceptionFormatterCannotReplaceBusinessFailureOrPreventTerminalCleanup()
        {
            using var diagnostics = new UIDiagnostics(clock: new Clock());
            var operation = diagnostics.Begin("Close");
            Assert.DoesNotThrow(() => operation.Fail(new BrokenException()));
            operation.Dispose();
            Assert.That(diagnostics.ActiveCount, Is.Zero);
            var error = diagnostics.GetEvents().Last();
            Assert.That(error.Outcome, Is.EqualTo(UIDiagnosticOutcome.Faulted));
            StringAssert.Contains("exception formatter failed", error.ExceptionChain);
        }

        [Test]
        public void DisposingDiagnosticsDoesNotDeclareRunningWorkCompleted()
        {
            var diagnostics = new UIDiagnostics(clock: new Clock());
            var task = diagnostics.Begin("Task");
            diagnostics.Dispose();
            Assert.That(diagnostics.ActiveCount, Is.EqualTo(1));
            Assert.That(diagnostics.Begin("Open"), Is.Null);
            task.Dispose();
            Assert.That(diagnostics.ActiveCount, Is.Zero);
        }

        [Test]
        public void TargetBudgetsAreExplicitAndRejectInvalidThresholds()
        {
            var low = UIPerformanceBudget.For(UIPerformanceTarget.LowEndAndroid);
            var mobile = UIPerformanceBudget.For(UIPerformanceTarget.MainstreamMobile);
            var editor = UIPerformanceBudget.For(UIPerformanceTarget.Editor);
            Assert.That(low.FrameMs, Is.EqualTo(16.667));
            Assert.That(low.GcBytesPerFrame, Is.Zero);
            Assert.That(mobile.CanvasMs, Is.LessThan(low.CanvasMs));
            Assert.That(editor.CanvasMs, Is.GreaterThan(low.CanvasMs));
            Assert.That(low.CachedShowFrames, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UIPerformanceBudget(UIPerformanceTarget.Editor, frameMs: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UIDiagnostics(0));
        }

        public sealed class Clock : IUIDiagnosticClock
        {
            private double _seconds;
            public int Reads { get; private set; }
            public double Seconds { get { Reads++; return _seconds; } set => _seconds = value; }
            public int Frame { get; set; }
        }
        private sealed class Adapter : IUIDiagnosticsAdapter
        {
            public bool Fail;
            public int Calls;
            public void Write(UIDiagnosticEvent entry)
            {
                Calls++;
                if (Fail) throw new InvalidOperationException("adapter failure");
            }
        }
        private sealed class BrokenException : Exception
        {
            public override string ToString() => throw new InvalidOperationException("formatter failure");
        }
    }
}
