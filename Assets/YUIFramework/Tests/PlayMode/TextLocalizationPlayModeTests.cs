using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework.Configuration;
using YUIFramework.Localization;

namespace YUIFramework.Tests
{
    public sealed class TextLocalizationPlayModeTests
    {
        private UIManager _ui;
        private UIResourceService _resources;
        private ConfigService _configs;
        private TextLocalizationService _localization;
        private GameObject _standalone;
        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async () => {
            _resources = new UIResourceService();
            _resources.Packages.Register(new ResourcesResourceProvider(), true);
            _configs = SampleUIConfiguration.Create(new ResourceConfigSource(_resources, prefix: "YUIConfig/"), ConfigFormat.MessagePack);
            _localization = new TextLocalizationService(_configs, "en");
            _ui = new UIManager();
            await _ui.InitializeAsync(new CodeViewLoader());
        });
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () => {
            try { await _ui.ShutdownAsync(); }
            finally
            {
                try { _localization.Dispose(); }
                finally
                {
                    try { await _configs.ShutdownAsync(); }
                    finally { await _resources.ShutdownAsync(); }
                }
            }
            if (_standalone) UnityEngine.Object.Destroy(_standalone);
            await UniTask.NextFrame();
        });
        private static IEnumerator Await(Task task)
        {
            var deadline = Time.realtimeSinceStartup + 20;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Localization play gate timeout.");
            task.GetAwaiter().GetResult();
        }
        private static ConfigLocalizedText AddText(Transform parent, bool tmp, string key)
        {
            var go = new GameObject(tmp ? "TMP" : "Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (tmp) go.AddComponent<TextMeshProUGUI>();
            else
            {
                var text = go.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            var component = go.AddComponent<ConfigLocalizedText>();
            component.TextKey = new LocalizedTextKey(key);
            return component;
        }
        private static string Value(ConfigLocalizedText component)
        {
            var text = component.GetComponent<Text>();
            return text ? text.text : component.GetComponent<TMP_Text>().text;
        }
        [UnityTest]
        public IEnumerator ActualTextAndTmpRefreshOnReadyLanguageReloadShutdownAndReinit() => Await(Controls());
        private async Task Controls()
        {
            Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null, "Stage11 ships a real WenKai TMP baseline.");
            _standalone = new GameObject("LocalTextRoot", typeof(RectTransform), typeof(Canvas));
            var text = AddText(_standalone.transform, false, "sample.title");
            var tmp = AddText(_standalone.transform, true, "sample.title");
            using var textBinding = text.Bind(_localization);
            using var tmpBinding = tmp.Bind(_localization);
            Assert.That(Value(text), Is.Empty);
            Assert.That(Value(tmp), Is.Empty);
            await _configs.InitializeAsync();
            Assert.That(Value(text), Is.EqualTo("Text localization sample"));
            Assert.That(Value(tmp), Is.EqualTo(Value(text)));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            text.enabled = false; tmp.gameObject.SetActive(false);
            Assert.That(text.IsSubscribed || tmp.IsSubscribed, Is.False);
            _localization.SetLocale("fr");
            Assert.That(Value(text), Is.EqualTo("Text localization sample"));
            text.enabled = true; tmp.gameObject.SetActive(true);
            Assert.That(Value(text), Is.EqualTo("Exemple de localisation"));
            Assert.That(Value(tmp), Is.EqualTo(Value(text)));
            await _configs.ReloadAsync();
            Assert.That(Value(tmp), Is.EqualTo("Exemple de localisation"));
            await _configs.ShutdownAsync();
            Assert.That(Value(text), Is.Empty);
            Assert.That(Value(tmp), Is.Empty);
            await _configs.InitializeAsync();
            Assert.That(Value(tmp), Is.EqualTo("Exemple de localisation"));
            _localization.Dispose();
            Assert.That(Value(text), Is.Empty);
            Assert.That(text.IsSubscribed || tmp.IsSubscribed, Is.False);
            Assert.That(_configs.Snapshot, Is.Not.Null);
        }
        [UnityTest]
        public IEnumerator ComponentReportsFallbackMissingKeyAndInvalidArgumentsExplicitly() => Await(Diagnostics());
        private async Task Diagnostics()
        {
            await _configs.InitializeAsync();
            _standalone = new GameObject("Diagnostics");
            var text = AddText(_standalone.transform, false, "sample.fallback");
            using var binding = text.Bind(_localization);
            LogAssert.Expect(LogType.Warning, "Localization Fallback: 'sample.fallback', requested 'fr', resolved 'en'.");
            _localization.SetLocale("fr");
            Assert.That(text.LastResult.Status, Is.EqualTo(LocalizedTextStatus.Fallback));
            Assert.That(Value(text), Is.EqualTo("French is empty; this is the English fallback."));
            LogAssert.Expect(LogType.Error, "Localization MissingKey: 'missing', requested 'fr', resolved ''.");
            text.TextKey = new LocalizedTextKey("missing");
            Assert.That(Value(text), Is.EqualTo("missing"));
            LogAssert.Expect(LogType.Error, "Localization InvalidFormat: 'sample.greeting', requested 'fr', resolved 'fr'.");
            text.TextKey = new LocalizedTextKey("sample.greeting");
            text.SetArguments("Ada");
            Assert.That(Value(text), Is.EqualTo("Bonjour, Ada !"));
        }
        [UnityTest]
        public IEnumerator RebindFencesOldServiceAndOldDisposalAndDestroyedUnityObject() => Await(Rebind());
        private async Task Rebind()
        {
            await _configs.InitializeAsync();
            using var other = new TextLocalizationService(_configs, "fr");
            _standalone = new GameObject("Rebind");
            var text = AddText(_standalone.transform, false, "sample.title");
            var old = text.Bind(_localization);
            using var current = text.Bind(other);
            old.Dispose();
            _localization.SetLocale("zh-CN");
            Assert.That(Value(text), Is.EqualTo("Exemple de localisation"));
            Assert.That(text.IsSubscribed, Is.True);
            UnityEngine.Object.Destroy(text.gameObject);
            await UniTask.NextFrame();
            other.SetLocale("en");
            current.Dispose();
            Assert.That(text == null, Is.True);
        }
        public sealed class LocalizedPage : BasePageContext
        {
            internal ConfigLocalizedText Label;
            internal bool FailShow;
            private TextLocalizationService _service;
            protected override void HandleInit() { Label = AddText(ViewObject.transform, false, "sample.title"); }
            protected override void HandleShow(object args)
            {
                if (args is TextLocalizationService service) _service = service;
                TrackDisplayBinding(Label.Bind(_service, this));
                if (FailShow) throw new InvalidOperationException("injected localization show failure");
            }
            protected override void HandleClose() { _service = null; }
            protected override void HandleDestroy() { _service = null; }
        }
        public sealed class CoverPage : BasePageContext { }
        private UIConfig RegisterPage()
        {
            var config = new UIConfig { Id = "LocalizedPage", PrefabKey = "LocalizedPage",
                CacheOnClose = true, MaxPoolSize = 1, FullScreen = true, UseTransition = false };
            _ui.Register<LocalizedPage>(config);
            _ui.Register<CoverPage>(new UIConfig { Id = "Cover", PrefabKey = "Cover", FullScreen = true, UseTransition = false });
            return config;
        }
        [UnityTest]
        public IEnumerator DisplayPoolReuseOneThousandTimesLeavesNoSubscriptionsAndPreservesBorrowedService() => Await(Reuse());
        private async Task Reuse()
        {
            await _configs.InitializeAsync();
            RegisterPage();
            LocalizedPage previous = null;
            for (var i = 0; i < 1000; i++)
            {
                var page = await _ui.OpenAsync<LocalizedPage>(_localization);
                if (previous != null) Assert.That(page, Is.SameAs(previous));
                Assert.That(page.Label.IsSubscribed, Is.True);
                Assert.That(_localization.SubscriberCount, Is.EqualTo(1));
                Assert.That(Value(page.Label), Is.EqualTo("Text localization sample"));
                await _ui.CloseAsync(page);
                Assert.That(page.Label.IsSubscribed, Is.False);
                Assert.That(_localization.SubscriberCount, Is.Zero);
                previous = page;
            }
            await _ui.OpenAsync<LocalizedPage>(_localization);
            await _ui.ShutdownAsync();
            Assert.That(_localization.IsDisposed, Is.False);
            Assert.That(_localization.Get("sample.title"), Is.EqualTo("Text localization sample"));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }
        [UnityTest]
        public IEnumerator CoveredRefreshCloseRollbackAndFailedReopenKeepDisplayOwnershipCorrect() => Await(Navigation());
        private async Task Navigation()
        {
            await _configs.InitializeAsync();
            var config = RegisterPage();
            var page = await _ui.Navigator.PushAsync<LocalizedPage>(_localization);
            await _ui.Navigator.PushAsync<CoverPage>();
            Assert.That(page.IsSuspended, Is.True);
            _localization.SetLocale("fr");
            Assert.That(Value(page.Label), Is.EqualTo("Text localization sample"));
            await _ui.Navigator.PopAsync();
            Assert.That(Value(page.Label), Is.EqualTo("Exemple de localisation"));
            config.UseTransition = true; config.TransitionType = UITransitionType.Fade; config.HideDuration = 10f;
            using var cancel = new CancellationTokenSource();
            var closing = _ui.CloseAsync(page, cancellationToken: cancel.Token).AsTask();
            await UniTask.Yield();
            cancel.Cancel();
            try { await closing; Assert.Fail("Close was not cancelled."); } catch (OperationCanceledException) { }
            Assert.That(page.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(page.Label.IsSubscribed, Is.True);
            _localization.SetLocale("en");
            Assert.That(Value(page.Label), Is.EqualTo("Text localization sample"));
            config.UseTransition = false;
            await _ui.CloseAsync(page);
            Assert.That(page.Label.IsSubscribed, Is.False);
            page.FailShow = true;
            try { await _ui.OpenAsync<LocalizedPage>(_localization); Assert.Fail("Expected failed show."); }
            catch (UILifecycleException error) { Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>()); }
            Assert.That(page.Label == null || !page.Label.IsSubscribed, Is.True);
            Assert.That(_localization.IsDisposed, Is.False);
        }
        [UnityTest]
        public IEnumerator MigratedStartupActuallyUsesLocalizedPageAndTableBeforeBusiness() => Await(Sample());
        private async Task Sample()
        {
            await ConfigUIStartup.EnterAsync(_configs, _ui,
                snapshot => SampleUIConfiguration.Map(snapshot, "hello"),
                async token => {
                    Assert.That(_localization.IsReady, Is.True);
                    var page = await _ui.Navigator.PushAsync<SampleHelloPage>(
                        new SampleLocalizedHelloArgs(_localization, "Ada"), cancellationToken: token);
                    var text = page.ViewObject.transform.Find("Panel/Message").GetComponent<Text>();
                    Assert.That(text.text, Is.EqualTo("Hello, Ada!"));
                    await _ui.OpenAsync<SampleHelloPage>(new SampleLocalizedHelloArgs(_localization, "Ada"));
                    page.ViewObject.transform.Find("LanguageButton").GetComponent<Button>().onClick.Invoke();
                    Assert.That(text.text, Is.EqualTo("你好，Ada！"));
                    await _ui.Navigator.PushAsync<SecondSamplePage>();
                    await _ui.Navigator.PopAsync();
                    Assert.That(text.text, Is.EqualTo("你好，Ada！"), "Returning with null navigation args retains the borrowed locale service.");
                    await _ui.OpenAsync<SampleHelloPage>("plain message");
                    _localization.SetLocale("en");
                    Assert.That(text.text, Is.EqualTo("plain message"), "Plain re-show must detach the previous localization binding.");
                    await _ui.CloseAsync(page);
                    _localization.SetLocale("en");
                    Assert.That(text.GetComponent<ConfigLocalizedText>().IsSubscribed, Is.False);
                });
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }
    }
}
