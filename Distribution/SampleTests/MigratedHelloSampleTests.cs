using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace YUIFramework.SampleTests
{
    public sealed class MigratedHelloSampleTests
    {
        [UnityTest]
        public IEnumerator MigratedY1HelloSceneUsesConfigInstanceUiAndDrainsTemplates() => UniTask.ToCoroutine(async () =>
        {
            var before = Resources.FindObjectsOfTypeAll<UIView>();
            var host = new GameObject("Y1 Hello scene after documented Y2 upgrade");
            var bootstrap = host.AddComponent<HelloUIBootstrap>();
            var field = typeof(HelloUIBootstrap).GetField("_uiService", BindingFlags.Instance | BindingFlags.NonPublic);
            UIManager ui = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await UniTask.WaitUntil(() =>
                {
                    ui = (UIManager)field.GetValue(bootstrap);
                    return ui != null && ui.IsInitialized && ui.Get<SampleHelloPage>()?.State == UIContextState.Opened;
                }, cancellationToken: timeout.Token);
                var page = ui.Get<SampleHelloPage>();
                var presentation = page.ViewObject.GetComponentInChildren<PresentationSampleView>();
                Assert.That(presentation, Is.Not.Null);
                StringAssert.Contains("YUIFramework", presentation.Greeting.text);
                var templates = Resources.FindObjectsOfTypeAll<UIView>().Where(v => !before.Contains(v) && v.name.StartsWith("CodePrefab_", StringComparison.Ordinal))
                    .Select(v => v.gameObject).ToArray();
                Assert.That(templates.Length, Is.GreaterThan(0));
                UnityEngine.Object.Destroy(host);
                await UniTask.WaitUntil(() => !ui.IsInitialized, cancellationToken: timeout.Token);
                await UniTask.WaitUntil(() => templates.All(t => t == null), cancellationToken: timeout.Token);
                Assert.That(templates.All(t => t == null), Is.True);
                Assert.That(ui.LastShutdownInputLockLeakCount, Is.Zero);
            }
            finally { UnityEngine.Object.Destroy(host); }
            await UniTask.NextFrame();
        });
    }
}
