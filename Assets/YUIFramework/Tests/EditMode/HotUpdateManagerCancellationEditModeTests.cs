using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using YUIFramework.HotUpdate;

namespace YUIFramework.Tests
{
    public sealed class HotUpdateManagerCancellationEditModeTests
    {
        [UnityTest]
        public IEnumerator Cancellation_CompletesCallerBeforePendingHandleAndReleasesAfterSuccess()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var handle = new ControllableHandle();
                using var cancellation = new CancellationTokenSource();
                var caller = WaitAsync(handle, cancellation.Token).AsTask();

                await UniTask.Yield();
                cancellation.Cancel();
                await WaitForCompletionAsync(caller);

                Assert.That(caller.IsCompleted, Is.True);
                var canceled = false;
                try
                {
                    await caller;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.That(canceled, Is.True);
                Assert.That(handle.IsDone, Is.False);
                Assert.That(handle.ReleaseCount, Is.Zero);

                handle.Complete(succeeded: true);
                await UniTask.Yield();

                Assert.That(handle.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Cancellation_ReleasesFailedHandleAfterNativeCompletion()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var handle = new ControllableHandle();
                using var cancellation = new CancellationTokenSource();
                var caller = WaitAsync(handle, cancellation.Token).AsTask();

                await UniTask.Yield();
                cancellation.Cancel();
                await WaitForCompletionAsync(caller);
                Assert.That(caller.IsCompleted, Is.True);
                var canceled = false;
                try
                {
                    await caller;
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                }

                Assert.That(canceled, Is.True);
                Assert.That(handle.ReleaseCount, Is.Zero);

                handle.Complete(succeeded: false);
                await UniTask.Yield();

                Assert.That(handle.ReleaseCount, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator NormalCompletion_DoesNotStartBackgroundRelease()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var handle = new ControllableHandle();
                var caller = WaitAsync(handle, CancellationToken.None).AsTask();

                await UniTask.Yield();
                handle.Complete(succeeded: true);
                Assert.That(await caller, Is.True);
                await UniTask.Yield();

                Assert.That(handle.ReleaseCount, Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator NormalFailure_ReleasesHandleExactlyOnce()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var handle = new ControllableHandle();
                var caller = WaitAsync(handle, CancellationToken.None).AsTask();

                await UniTask.Yield();
                handle.Complete(succeeded: false);
                Assert.That(await caller, Is.False);
                await UniTask.Yield();

                Assert.That(handle.ReleaseCount, Is.EqualTo(1));
            });
        }

        private static UniTask<bool> WaitAsync(
            ControllableHandle handle,
            CancellationToken cancellationToken)
        {
            return HotUpdateManager.WaitForHandleAsync(
                () => handle.IsDone,
                () => handle.IsValid,
                () => handle.Succeeded,
                handle.Release,
                cancellationToken);
        }

        private static async UniTask WaitForCompletionAsync(System.Threading.Tasks.Task task)
        {
            for (var i = 0; i < 8 && !task.IsCompleted; i++)
            {
                await UniTask.Yield();
            }
        }

        private sealed class ControllableHandle
        {
            public bool IsDone { get; private set; }

            public bool IsValid { get; private set; } = true;

            public bool Succeeded { get; private set; }

            public int ReleaseCount { get; private set; }

            public void Complete(bool succeeded)
            {
                Succeeded = succeeded;
                IsDone = true;
            }

            public void Release()
            {
                ReleaseCount++;
                IsValid = false;
            }
        }
    }
}
