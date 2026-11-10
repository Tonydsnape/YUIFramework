using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YUIFramework.SampleTests
{
    [DefaultExecutionOrder(32000)]
    public sealed class DestroyRcAtStart : MonoBehaviour
    {
        public ReleaseCandidateSample Sample;
        public bool SawPending;
        private void Start()
        {
            SawPending = !Sample.Completion.IsCompleted;
            Destroy(Sample.gameObject);
        }
    }

    public sealed class ReleaseCandidateSampleTests
    {
        [UnityTest]
        public IEnumerator RealBootstrapAndImportedCompleteSampleRun() => UniTask.ToCoroutine(async () =>
        {
            var host = new GameObject("Complete sample lifecycle test");
            var sample = host.AddComponent<ReleaseCandidateSample>();
            sample.ConfigureSmoke(false);
            try
            {
                await UniTask.NextFrame();
                await sample.Completion.AsUniTask().Timeout(TimeSpan.FromSeconds(45));
                Assert.That(sample.Report.Status, Is.EqualTo("Passed"), sample.Report.Error);
                Assert.That(sample.Report.BootstrapReady && sample.Report.UiShutdown && sample.Report.SceneRestored &&
                    sample.Report.TemplatesDestroyed, Is.True);
            }
            finally { UnityEngine.Object.Destroy(host); }
            await UniTask.NextFrame();
        });

        [UnityTest]
        public IEnumerator RealPendingStartDestructionDrainsOneSharedTask() => UniTask.ToCoroutine(async () =>
        {
            var host = new GameObject("Destroy actual pending RC sample");
            var sample = host.AddComponent<ReleaseCandidateSample>();
            var destroyerObject = new GameObject("Late Start destroyer");
            var destroyer = destroyerObject.AddComponent<DestroyRcAtStart>();
            destroyer.Sample = sample;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await UniTask.NextFrame(timeout.Token);
                Assert.That(destroyer.SawPending, Is.True, "The real Start must have registered an incomplete operation.");
                await sample.CleanupAsync().AttachExternalCancellation(timeout.Token);
                Assert.That(sample.Completion.IsCompleted, Is.True);
                Assert.That(sample.Report.UiShutdown, Is.True);
            }
            finally { UnityEngine.Object.Destroy(host); UnityEngine.Object.Destroy(destroyerObject); }
            await UniTask.NextFrame();
        });
    }
}
