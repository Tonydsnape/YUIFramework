using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YUIFramework.Tests
{
    public sealed class Stage12SystemUIEditModeTests
    {
        private UIManager _ui;
        [SetUp]
        public void SetUp()
        {
            _ui = new UIManager();
            _ui.Initialize(new NoViews());
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var task = _ui.ShutdownAsync().AsTask();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception;
            Assert.That(task.IsCanceled, Is.False);
        }

        [Test]
        public void Loading_IndependentHandlesWeightedProgressAndOutOfOrderDispose()
        {
            using var loading = new UILoadingService();
            var one = loading.Begin(_ui.Scopes.Global, "one", weight: 1);
            var two = loading.Begin(_ui.Scopes.Global, "two", canCancel: false, weight: 3);
            Assert.That(loading.State.Progress, Is.Null);
            one.Report(1);
            two.Report(.5f);
            Assert.That(loading.State.Progress, Is.EqualTo(.625f));
            Assert.That(loading.State.Count, Is.EqualTo(2));
            one.Dispose();
            one.Dispose();
            Assert.That(loading.State.Count, Is.EqualTo(1));
            Assert.That(loading.State.CanCancel, Is.False);
            Assert.That(two.Token.IsCancellationRequested, Is.False);
            Assert.Throws<InvalidOperationException>(two.Cancel);
            two.Dispose();
            Assert.That(loading.State.IsVisible, Is.False);
        }

        [Test]
        public void Loading_CallerCancelAndServiceDisposeReleaseExactlyOnce()
        {
            using var loading = new UILoadingService();
            using var cancellation = new CancellationTokenSource();
            var one = loading.Begin(_ui.Scopes.Global, "one", cancellationToken: cancellation.Token);
            var two = loading.Begin(_ui.Scopes.Global, "two");
            cancellation.Cancel();
            Assert.That(one.IsEnded, Is.True);
            Assert.That(loading.State.Count, Is.EqualTo(1));
            loading.Dispose();
            Assert.That(two.Token.IsCancellationRequested, Is.True);
            Assert.That(loading.State.Count, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => loading.Begin(_ui.Scopes.Global, "late"));
        }

        [Test]
        public void Loading_InvalidProgressAndWeightsFailExplicitly()
        {
            using var loading = new UILoadingService();
            Assert.Throws<ArgumentOutOfRangeException>(() => loading.Begin(_ui.Scopes.Global, "bad", weight: float.NaN));
            using var operation = loading.Begin(_ui.Scopes.Global, "valid");
            Assert.Throws<ArgumentOutOfRangeException>(() => operation.Report(1.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => operation.Report(float.NaN));
            Assert.That(loading.State.Count, Is.EqualTo(1));
        }

        [Test]
        public void Loading_ObserverFailureRollsBackAdmissionAndNotifiesOtherObservers()
        {
            using var loading = new UILoadingService();
            var calls = 0;
            loading.Changed += _ => throw new InvalidOperationException("observer");
            loading.Changed += _ => calls++;
            Assert.Throws<AggregateException>(() => loading.Begin(_ui.Scopes.Global, "bad"));
            Assert.That(loading.State.Count, Is.Zero);
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void Toast_TenThousandRepeatedRequestsMergeWithoutRetainingWaitersOrExtendingDeadline()
        {
            var clock = new Clock();
            using var toast = new UIToastService(clock);
            var first = toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("same", "repeat", 2));
            toast.Tick();
            for (var i = 0; i < 10000; i++)
                Assert.That(Result(toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("same", "repeat"))),
                    Is.EqualTo(UIToastOutcome.Merged));
            Assert.That(toast.Count, Is.EqualTo(1));
            Assert.That(toast.Current.Count, Is.EqualTo(10001));
            clock.Seconds = 2;
            toast.Tick();
            Assert.That(Result(first), Is.EqualTo(UIToastOutcome.Displayed));
            Assert.That(toast.Count, Is.Zero);
        }

        [Test]
        public void Toast_CapacityPriorityEvictionAndStablePriorityOrderAreBounded()
        {
            var clock = new Clock();
            using var toast = new UIToastService(clock, capacity: 3, maxPerWindow: 100);
            var low = toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("low", "low", priority: -1));
            var a = toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("a", "a", priority: 2));
            var b = toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("b", "b", priority: 2));
            var high = toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("high", "high", priority: 5));
            Assert.That(Result(low), Is.EqualTo(UIToastOutcome.Superseded));
            Assert.That(Result(toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("overflow", "overflow"))),
                Is.EqualTo(UIToastOutcome.Overflow));
            toast.Tick();
            Assert.That(toast.Current.Request.Key, Is.EqualTo("high"));
            clock.Seconds = 2; toast.Tick();
            clock.Seconds = 2.1; toast.Tick();
            Assert.That(toast.Current.Request.Key, Is.EqualTo("a"));
            a.Dispose(); b.Dispose();
            Assert.That(Result(a), Is.EqualTo(UIToastOutcome.Canceled));
            Assert.That(Result(b), Is.EqualTo(UIToastOutcome.Canceled));
            Assert.That(Result(high), Is.EqualTo(UIToastOutcome.Displayed));
        }

        [Test]
        public void Toast_RateWindowLimitsUniqueTrafficAndMinimumGapUsesUnscaledClock()
        {
            var clock = new Clock();
            using var toast = new UIToastService(clock, maxPerWindow: 2, minimumInterval: .5);
            toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("a", "a", .1));
            toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("b", "b", .1));
            Assert.That(Result(toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("c", "c"))),
                Is.EqualTo(UIToastOutcome.RateLimited));
            toast.Tick();
            clock.Seconds = .1; toast.Tick();
            Assert.That(toast.Current, Is.Null);
            clock.Seconds = .59; toast.Tick();
            Assert.That(toast.Current, Is.Null);
            clock.Seconds = .6; toast.Tick();
            Assert.That(toast.Current.Request.Key, Is.EqualTo("b"));
            clock.Seconds = 1;
            Assert.That(toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("c", "c")).IsCompleted, Is.False);
        }

        [Test]
        public void Toast_MergeIdentityIncludesScope_AndDisposeCompletesEveryTicket()
        {
            var module = _ui.Scopes.CreateModule("different");
            using var toast = new UIToastService(new Clock());
            var one = toast.Enqueue(_ui.Scopes.Global, new UIToastRequest("same", "global"));
            var two = toast.Enqueue(module, new UIToastRequest("same", "module"));
            Assert.That(toast.Count, Is.EqualTo(2));
            toast.Dispose();
            Assert.That(Result(one), Is.EqualTo(UIToastOutcome.ServiceStopped));
            Assert.That(Result(two), Is.EqualTo(UIToastOutcome.ServiceStopped));
        }

        [Test]
        public void ModelConstructorsRejectNonfiniteOrBlankRequests()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UIToastRequest("key", "text", double.NaN));
            Assert.Throws<ArgumentException>(() => new UIToastRequest("", "text"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UIToastService(windowSeconds: double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new UIConfirmationRequest("title", " "));
            Assert.Throws<InvalidOperationException>(() => _ui.Scopes.Global.ReleaseAsync());
        }

        private static UIToastOutcome Result(UIToastTicket ticket) => ticket.Completion.GetAwaiter().GetResult();
        private sealed class Clock : IUISystemClock { public double Seconds { get; set; } }
        private sealed class NoViews : IResourceLoader
        {
            public UniTask<GameObject> LoadPrefabAsync(string key, CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();
            public void Release(string key, GameObject instance) => throw new NotSupportedException();
        }
    }
}
