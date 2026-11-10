using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.TestTools;

namespace YUIFramework.PackageTests
{
    public sealed class PackageLifecycleTests
    {
        [Preserve]
        public sealed class PackagePage : BasePageContext { }

        private sealed class Loader : IResourceLoader
        {
            public GameObject Template;
            public UniTask<GameObject> LoadPrefabAsync(string key, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(Template);
            }
            public void Release(string key, GameObject instance) => Object.Destroy(instance);
        }

        [UnityTest]
        public IEnumerator PublicApiSupportsBothReflectionPathsAndDrainsOwnership() => UniTask.ToCoroutine(async () =>
        {
            var loader = new Loader { Template = new GameObject("PackageTemplate", typeof(RectTransform), typeof(UIView)) };
            loader.Template.SetActive(false);
            var ui = new UIManager();
            try
            {
                await ui.InitializeAsync(loader);
                ui.Register<PackagePage>(new UIConfig { Id = "package", PrefabKey = "package", CacheOnClose = true,
                    MaxPoolSize = 1, PreloadCount = 1, UseTransition = false });
                await ui.PrewarmAsync<PackagePage>();
                var page = await ui.Navigator.PushAsync<PackagePage>();
                Assert.That(page.ViewObject.activeInHierarchy, Is.True);
                await ui.CloseAsync<PackagePage>();
                Assert.That(ui.PoolDiagnostics.IdleCount, Is.EqualTo(1));
                await ui.ClearPoolAsync<PackagePage>();
                var fresh = await ui.OpenAsync<PackagePage>();
                Assert.That(fresh, Is.Not.SameAs(page));
            }
            finally
            {
                if (ui.IsInitialized) await ui.ShutdownAsync();
                Object.Destroy(loader.Template);
            }
            Assert.That(ui.LastShutdownInputLockLeakCount, Is.Zero);
            await UniTask.Yield();
            Assert.That(loader.Template == null, Is.True);
        });
    }
}
