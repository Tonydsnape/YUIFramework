using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace YUIFramework.Tests
{
    public sealed class Stage9MessagingMvvmEditModeTests
    {
        private static int _messageSink;

        [Test]
        public void TypedTopic_PayloadMismatchFailsImmediately()
        {
            var messages = new UIMessageCenter();
            var integerTopic = new UIMessageTopic<int>("typed.value");
            messages.Subscribe(integerTopic, _ => { });

            var exception = Assert.Throws<UIMessageException>(
                () => messages.Publish(
                    new UIMessageTopic<string>("typed.value"),
                    "wrong"));

            Assert.That(exception.MessageName, Is.EqualTo("typed.value"));
            Assert.That(messages.ListenerCount, Is.EqualTo(1));
        }

        [Test]
        public void Publish_UsesPriorityStableOrderAndAggregatesFailures()
        {
            var messages = new UIMessageCenter();
            var topic = new UIMessageTopic<int>("ordered");
            var order = new List<string>();
            messages.Subscribe(topic, _ => order.Add("normal-first"));
            messages.Subscribe(
                topic,
                _ =>
                {
                    order.Add("high-failure");
                    throw new InvalidOperationException("expected");
                },
                priority: 10);
            messages.Subscribe(topic, _ => order.Add("normal-second"));

            var error = Assert.Throws<AggregateException>(
                () => messages.Publish(topic, 1));

            Assert.That(
                order,
                Is.EqualTo(
                    new[]
                    {
                        "high-failure",
                        "normal-first",
                        "normal-second"
                    }));
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(1));
            Assert.That(error.InnerExceptions[0], Is.TypeOf<UIMessageException>());
        }

        [Test]
        public void Publish_MutationAndRecursionUseDocumentedSnapshots()
        {
            var messages = new UIMessageCenter();
            var topic = new UIMessageTopic<int>("recursive");
            var order = new List<string>();
            UIMessageToken removed = null;
            var recursing = false;

            messages.Subscribe(
                topic,
                _ =>
                {
                    order.Add(recursing ? "A2" : "A1");
                    if (recursing)
                    {
                        return;
                    }

                    removed.Dispose();
                    messages.Subscribe(topic, __ => order.Add("D"));
                    recursing = true;
                    messages.Publish(topic, 2);
                    recursing = false;
                },
                priority: 10);
            removed = messages.Subscribe(topic, _ => order.Add("B"));
            messages.Subscribe(topic, _ => order.Add("C"));

            messages.Publish(topic, 1);

            Assert.That(
                order,
                Is.EqualTo(new[] { "A1", "A2", "C", "D", "C" }));
        }

        [Test]
        public void Publish_ClearDuringDispatchSkipsRemainingAndAllowsReuse()
        {
            var messages = new UIMessageCenter();
            var topic = new UIMessageTopic<int>("clear.during.publish");
            var order = new List<string>();
            messages.Subscribe(
                topic,
                _ =>
                {
                    order.Add("clear");
                    messages.Clear();
                },
                priority: 10);
            messages.Subscribe(topic, _ => order.Add("skipped"));

            messages.Publish(topic, 1);

            Assert.That(order, Is.EqualTo(new[] { "clear" }));
            Assert.That(messages.ListenerCount, Is.Zero);
            using var replacement = messages.Subscribe(
                topic,
                _ => order.Add("replacement"));
            messages.Publish(topic, 2);
            Assert.That(order, Is.EqualTo(new[] { "clear", "replacement" }));
        }

        [Test]
        public void MessageScope_DisposeAndCancellationRemoveSubscriptions()
        {
            var messages = new UIMessageCenter();
            var topic = new UIMessageTopic<int>("scoped");
            var cancellation = new CancellationTokenSource();
            var scope = messages.CreateScope("display", cancellation.Token);
            var calls = 0;
            messages.Subscribe(topic, _ => calls++, scope);

            messages.Publish(topic, 1);
            cancellation.Cancel();
            messages.Publish(topic, 2);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(scope.IsDisposed, Is.True);
            Assert.That(messages.ListenerCount, Is.Zero);
            Assert.Throws<ObjectDisposedException>(
                () => messages.Subscribe(topic, _ => { }, scope));
            cancellation.Dispose();
        }

        [Test]
        public void MessageScope_BackgroundCancellationMarshalsAndTokenCanRetry()
        {
            var previousContext = SynchronizationContext.Current;
            var owningContext = new PumpSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(owningContext);
            try
            {
                var messages = new UIMessageCenter();
                var topic = new UIMessageTopic<int>("scoped.background");
                using var cancellation = new CancellationTokenSource();
                var scope = messages.CreateScope("background", cancellation.Token);
                messages.Subscribe(topic, _ => { }, scope);

                Task.Run(cancellation.Cancel).GetAwaiter().GetResult();
                Assert.That(scope.IsDisposed, Is.False);
                Assert.That(messages.ListenerCount, Is.EqualTo(1));

                owningContext.RunPostedCallbacks();

                Assert.That(scope.IsDisposed, Is.True);
                Assert.That(messages.ListenerCount, Is.Zero);

                var token = messages.Subscribe(topic, _ => { });
                var threadError = Task.Run(
                        () =>
                        {
                            try
                            {
                                token.Dispose();
                                return null;
                            }
                            catch (Exception exception)
                            {
                                return exception;
                            }
                        })
                    .GetAwaiter()
                    .GetResult();
                Assert.That(threadError, Is.TypeOf<InvalidOperationException>());
                Assert.That(token.IsDisposed, Is.False);
                token.Dispose();
                Assert.That(token.IsDisposed, Is.True);
                Assert.That(messages.ListenerCount, Is.Zero);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        [Test]
        public void TypedPublish_SteadyStateAllocatesNoManagedBytes()
        {
            var messages = new UIMessageCenter();
            var topic = new UIMessageTopic<int>("gc.baseline");
            using var token = messages.Subscribe(topic, ReceiveMessage);
            for (var index = 0; index < 32; index++)
            {
                messages.Publish(topic, index);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 10000; index++)
            {
                messages.Publish(topic, index);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine(
                $"Typed publish steady-state allocation: {allocated} bytes / 10000 publishes.");
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
            Assert.That(_messageSink, Is.EqualTo(9999));
        }

        [Test]
        public void AsyncCommand_CoversSuccessBusyErrorsCancellationAndDispose()
        {
            var gate = new UniTaskCompletionSource();
            var command = new UIAsyncCommand(
                async cancellationToken =>
                {
                    await gate.Task.AttachExternalCancellation(cancellationToken);
                });

            var running = command.ExecuteAsync().AsTask();
            Assert.That(command.IsExecuting, Is.True);
            Assert.That(command.CanExecute, Is.False);
            Assert.Throws<InvalidOperationException>(
                () => command.ExecuteAsync().AsTask().GetAwaiter().GetResult());
            gate.TrySetResult();
            running.GetAwaiter().GetResult();
            Assert.That(command.IsExecuting, Is.False);
            Assert.That(command.LastError, Is.Null);

            var synchronousFailure = new UICommand(
                () => throw new InvalidOperationException("sync"));
            Assert.Throws<InvalidOperationException>(
                () => synchronousFailure.ExecuteAsync().AsTask().GetAwaiter().GetResult());
            Assert.That(synchronousFailure.IsExecuting, Is.False);
            Assert.That(
                synchronousFailure.LastError,
                Is.TypeOf<InvalidOperationException>());

            var asynchronousFailure = new UIAsyncCommand(
                _ => UniTask.FromException(new ArgumentException("async")));
            Assert.Throws<ArgumentException>(
                () => asynchronousFailure.ExecuteAsync().AsTask().GetAwaiter().GetResult());
            Assert.That(asynchronousFailure.IsExecuting, Is.False);

            var preCanceled = new CancellationTokenSource();
            preCanceled.Cancel();
            Assert.Throws<OperationCanceledException>(
                () => command.ExecuteAsync(preCanceled.Token).AsTask().GetAwaiter().GetResult());

            var cancellationGate = new UniTaskCompletionSource();
            var cancellable = new UIAsyncCommand(
                async token =>
                {
                    await cancellationGate.Task.AttachExternalCancellation(token);
                });
            var canceledRun = cancellable.ExecuteAsync().AsTask();
            cancellable.Dispose();
            Assert.Throws<OperationCanceledException>(
                () => canceledRun.GetAwaiter().GetResult());
            Assert.That(cancellable.IsExecuting, Is.False);
            Assert.That(cancellable.IsDisposed, Is.True);

            command.Dispose();
            synchronousFailure.Dispose();
            asynchronousFailure.Dispose();
            preCanceled.Dispose();
        }

        [Test]
        public void CommandObserverFailure_DoesNotSkipExecutionOrBusyReset()
        {
            var executed = false;
            var command = new UICommand(() => executed = true);
            command.StateChanged += () => throw new InvalidOperationException("observer");

            var error = Assert.Throws<AggregateException>(
                () => command.ExecuteAsync().AsTask().GetAwaiter().GetResult());

            Assert.That(error.InnerExceptions, Is.Not.Empty);
            Assert.That(executed, Is.True);
            Assert.That(command.IsExecuting, Is.False);
        }

        [Test]
        public void CommandObserver_ReentrantDisposeCancelsAndClearsBusy()
        {
            var executed = false;
            var command = new UICommand(() => executed = true);
            command.StateChanged += command.Dispose;

            Assert.Throws<OperationCanceledException>(
                () => command.ExecuteAsync().AsTask().GetAwaiter().GetResult());

            Assert.That(command.IsDisposed, Is.True);
            Assert.That(command.IsExecuting, Is.False);
            Assert.That(executed, Is.False);
        }

        [Test]
        public void ObservableProperty_ImmediateFailureDoesNotRetainHandler()
        {
            var property = new ObservableProperty<int>(1);
            var calls = 0;

            Assert.Throws<InvalidOperationException>(
                () =>
                    property.Subscribe(
                        _ =>
                        {
                            calls++;
                            throw new InvalidOperationException("initial");
                        }));
            property.Value = 2;

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void BindingToken_DisposesEveryEntryThenAggregates()
        {
            var token = new BindingToken();
            var disposed = 0;
            token.Add(() => disposed++);
            token.Add(() => throw new InvalidOperationException("expected"));
            token.Add(() => disposed++);

            var error = Assert.Throws<AggregateException>(token.Dispose);

            Assert.That(error.InnerExceptions.Count, Is.EqualTo(1));
            Assert.That(disposed, Is.EqualTo(2));
            Assert.That(token.IsDisposed, Is.True);
        }

        [Test]
        public void ValidatedAndReadOnlyPropertiesExposeStableContracts()
        {
            var property = new ValidatedProperty<string>(
                string.Empty,
                value => string.IsNullOrWhiteSpace(value) ? "Required" : null);
            var validationChanges = 0;
            property.ValidationChanged += () => validationChanges++;

            property.Value = "ready";
            IReadOnlyObservableProperty<string> readOnly = property;
            var observed = string.Empty;
            using var subscription = readOnly.Subscribe(value => observed = value);

            Assert.That(property.IsValid, Is.True);
            Assert.That(property.ValidationError, Is.Null);
            Assert.That(validationChanges, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo("ready"));
        }

        [Test]
        public void ValidatedProperty_ValueObserversSeeCurrentValidationState()
        {
            var throwValidation = false;
            var property = new ValidatedProperty<string>(
                "ready",
                value =>
                {
                    if (throwValidation)
                    {
                        throw new InvalidOperationException("validator");
                    }

                    return string.IsNullOrWhiteSpace(value) ? "Required" : null;
                });
            bool? validDuringValueNotification = null;
            using var subscription = property.Subscribe(
                _ => validDuringValueNotification = property.IsValid,
                false);

            property.Value = string.Empty;
            Assert.That(validDuringValueNotification, Is.False);
            Assert.That(property.ValidationError, Is.EqualTo("Required"));

            throwValidation = true;
            Assert.Throws<InvalidOperationException>(() => property.Value = "rejected");
            Assert.That(property.Value, Is.Empty);
            Assert.That(property.ValidationError, Is.EqualTo("Required"));
        }

        private static void ReceiveMessage(int value)
        {
            _messageSink = value;
        }

        private sealed class PumpSynchronizationContext : SynchronizationContext
        {
            private readonly Queue<Action> _callbacks = new Queue<Action>();

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (_callbacks)
                {
                    _callbacks.Enqueue(() => callback(state));
                }
            }

            public void RunPostedCallbacks()
            {
                while (true)
                {
                    Action callback;
                    lock (_callbacks)
                    {
                        if (_callbacks.Count == 0)
                        {
                            return;
                        }

                        callback = _callbacks.Dequeue();
                    }

                    callback();
                }
            }
        }
    }
}
