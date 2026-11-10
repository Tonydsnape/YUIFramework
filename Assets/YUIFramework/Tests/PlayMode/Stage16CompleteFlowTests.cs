using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YUIFramework.Localization;

namespace YUIFramework.Tests
{
    public sealed class Stage16CompleteFlowTests
    {
        [UnityTest]
        public IEnumerator Stage16_CompleteFlowUsesExistingServicesAndCleansUp() => UniTask.ToCoroutine(async () =>
        {
            var ui = new UIManager();
            var owner = new SampleConfigOwner();
            var views = new CodeViewLoader();
            TextLocalizationService text = null;
            ReleaseCandidateFlow flow = null;
            using var timeout = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(15));
            try
            {
                var snapshot = await owner.Service.InitializeAsync();
                text = new TextLocalizationService(owner.Service, "en");
                await ui.InitializeAsync(views);
                flow = new ReleaseCandidateFlow(ui, text);
                await flow.EnterAsync(snapshot, timeout.Token);
                var checks = await flow.SmokeAsync(timeout.Token);
                Assert.That(checks.Length, Is.GreaterThanOrEqualTo(9));
                Assert.That(ui.Navigator.Count, Is.EqualTo(1));
            }
            finally
            {
                try { if (ui.IsInitialized) await ui.ShutdownAsync(); }
                finally
                {
                    if (flow != null) { await flow.RestoreScenesAsync(); flow.Dispose(); }
                    views.Dispose(); text?.Dispose(); await owner.ShutdownAsync();
                }
            }
            Assert.That(text.SubscriberCount, Is.Zero);
            Assert.That(ui.LastShutdownInputLockLeakCount, Is.Zero);
            await UniTask.Yield();
        });
    }
}
