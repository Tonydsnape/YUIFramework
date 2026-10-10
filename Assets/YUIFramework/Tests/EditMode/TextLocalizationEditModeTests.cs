using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YUIFramework.Configuration;
using YUIFramework.Localization;
using YUIFramework.Localization.Editor;

namespace YUIFramework.Tests
{
    public sealed class TextLocalizationEditModeTests
    {
        public sealed class KeyHolder : ScriptableObject { public LocalizedTextKey Reference; }
        [Test]
        public void InspectorSelectionUpdatesMultipleSerializedTargetsAndRejectsUnknownKey()
        {
            var first = ScriptableObject.CreateInstance<KeyHolder>();
            var second = ScriptableObject.CreateInstance<KeyHolder>();
            try
            {
                var targets = new UnityEngine.Object[] { first, second };
                LocalizedTextEditorCatalog.ApplySelection(targets, "Reference.key", "sample.title");
                Assert.That(first.Reference.Key, Is.EqualTo("sample.title"));
                Assert.That(second.Reference.Key, Is.EqualTo("sample.title"));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(string.IsNullOrEmpty(first.Reference.Key), Is.True);
                Assert.That(string.IsNullOrEmpty(second.Reference.Key), Is.True);
                Assert.Throws<KeyNotFoundException>(() =>
                    LocalizedTextEditorCatalog.ApplySelection(targets, "Reference.key", "missing"));
            }
            finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); }
        }
        private const string Data = "{\"hello\":{\"Key\":\"hello\",\"zh-CN\":\"你好 {0}\",\"en\":\"Hello {0}\",\"fr\":null}," +
            "\"title\":{\"Key\":\"title\",\"zh-CN\":\"标题\",\"en\":\"Title\",\"fr\":\"Titre\"}," +
            "\"empty\":{\"Key\":\"empty\",\"zh-CN\":null,\"en\":null,\"fr\":null}}";
        private sealed class Source : IConfigSource
        {
            internal string Json = Data;
            internal int Live;
            internal UniTaskCompletionSource Gate;
            public async UniTask<IConfigAsset> LoadAsync(string table, CancellationToken token)
            {
                if (Gate != null) await Gate.Task;
                Live++;
                return new Asset(this, Encoding.UTF8.GetBytes(Json));
            }
            private sealed class Asset : IConfigAsset
            {
                private Source _owner;
                internal Asset(Source owner, byte[] bytes) { _owner = owner; Bytes = bytes; }
                public byte[] Bytes { get; }
                public void Dispose() { if (_owner == null) return; _owner.Live--; _owner = null; }
            }
        }
        private static ConfigService Create(Source source) => new ConfigService(source, new ConfigCodec(),
            ConfigFormat.Json, new[] { LocalizationTextCatalog.Table });
        private static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Localization test timeout.");
            task.GetAwaiter().GetResult();
        }
        [Test]
        public void RealGeneratedJsonBytesAndEditorPickerShareValidatedImmutableCatalog()
        {
            var json = LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath);
            var bytes = LocalizationTextCatalog.Parse(new ConfigCodec().Decode(
                File.ReadAllBytes("Assets/Resources/YUIConfig/LocalizationTextConfig.bytes"), ConfigFormat.MessagePack));
            Assert.That(YUIFramework.ConfigGenerated.LocalizationTextConfig.Table, Is.SameAs(LocalizationTextCatalog.Table));
            CollectionAssert.AreEquivalent(json.Keys, bytes.Keys);
            foreach (var key in json.Keys)
                CollectionAssert.AreEquivalent(json.Get(key).Translations, bytes.Get(key).Translations);
            Assert.That(json.Get("sample.greeting").Translations["zh-CN"], Is.EqualTo("你好，{0}！"));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, string>)json.Get("sample.title").Translations).Add("de", "changed"));
        }
        [TestCase("key")]
        [TestCase("locale")]
        [TestCase("unsupported")]
        [TestCase("columns")]
        [TestCase("format")]
        [TestCase("argument")]
        [TestCase("type")]
        public void RuntimeRejectsInvalidCatalogBeforePublication(string kind)
        {
            var json = JObject.Parse(Data);
            var first = (JObject)json["hello"];
            switch (kind)
            {
                case "key": first["Key"] = "wrong"; break;
                case "locale": first["EN-us"] = "bad"; break;
                case "unsupported": first["zz"] = "bad"; break;
                case "columns": first.Remove("fr"); break;
                case "format": first["en"] = "{0:N2}"; break;
                case "argument": first["en"] = "Hello {1}"; break;
                case "type": first["en"] = 12; break;
            }
            Assert.Throws<ConfigDataException>(() => LocalizationTextCatalog.Parse(json));
        }
        [UnityTest]
        public IEnumerator StrictLookupFallbackCultureAndLifecycle() => Await(Lookup());
        private async Task Lookup()
        {
            var source = new Source();
            var configs = Create(source);
            using var service = new TextLocalizationService(configs);
            Assert.That(service.Resolve("title").Status, Is.EqualTo(LocalizedTextStatus.NotReady));
            await configs.InitializeAsync();
            Assert.That(service.Get("hello", "玩家"), Is.EqualTo("你好 玩家"));
            service.SetLocale("fr");
            Assert.That(service.Get("title"), Is.EqualTo("Titre"));
            Assert.That(service.TryGet("hello", out var text, out var result, "Ada"), Is.False);
            Assert.That(text, Is.Null);
            Assert.That(result.Status, Is.EqualTo(LocalizedTextStatus.Fallback));
            Assert.That(result.ResolvedLocale, Is.EqualTo("en"));
            Assert.That(result.Text, Is.EqualTo("Hello Ada"));
            Assert.Throws<LocalizedTextException>(() => service.Get("hello", "Ada"));
            Assert.That(service.Resolve("hello").Status, Is.EqualTo(LocalizedTextStatus.InvalidFormat));
            Assert.That(service.Resolve("missing").Status, Is.EqualTo(LocalizedTextStatus.MissingKey));
            Assert.That(service.Resolve("empty").Status, Is.EqualTo(LocalizedTextStatus.MissingTranslation));
            Assert.Throws<ConfigDataException>(() => service.SetLocale("de"));
            Assert.Throws<ConfigDataException>(() => service.SetLocale("zh-cn"));
            await configs.ShutdownAsync();
            Assert.That(service.IsReady, Is.False);
            await configs.InitializeAsync();
            Assert.That(service.Get("title"), Is.EqualTo("Titre"));
            service.Dispose();
            Assert.That(service.Resolve("title").Status, Is.EqualTo(LocalizedTextStatus.Disposed));
            Assert.That(configs.Snapshot, Is.Not.Null, "Borrowed Config owner must survive.");
            await configs.ShutdownAsync();
            Assert.That(source.Live, Is.Zero);
        }
        [UnityTest]
        public IEnumerator ReloadFailureRetainsOldSnapshotAndLocaleRemovalIsExplicit() => Await(Reload());
        private async Task Reload()
        {
            var source = new Source(); var configs = Create(source);
            using var service = new TextLocalizationService(configs, "fr");
            await configs.InitializeAsync();
            var old = configs.Snapshot;
            source.Json = Data.Replace("Hello {0}", "bad {");
            try { await configs.ReloadAsync(); Assert.Fail("Invalid format published."); } catch (ConfigDataException) { }
            Assert.That(configs.Snapshot, Is.SameAs(old));
            Assert.That(service.Get("title"), Is.EqualTo("Titre"));
            var next = JObject.Parse(Data);
            foreach (var property in next.Properties()) ((JObject)property.Value).Remove("fr");
            source.Json = next.ToString();
            await configs.ReloadAsync();
            Assert.That(service.Resolve("title").Status, Is.EqualTo(LocalizedTextStatus.UnsupportedLocale));
            service.SetLocale("en");
            Assert.That(service.Get("title"), Is.EqualTo("Title"));
            Assert.That(old.Get(LocalizationTextCatalog.Table).Get("title").Translations["fr"], Is.EqualTo("Titre"));
            await configs.ShutdownAsync();
        }
        [UnityTest]
        public IEnumerator ObserverFailuresAreAggregatedAfterCommitAndCleanupContinues() => Await(Observers());
        private async Task Observers()
        {
            var configs = Create(new Source());
            var service = new TextLocalizationService(configs, "en");
            var calls = 0;
            Action bad = () => throw new InvalidOperationException("observer");
            Action good = () => calls++;
            service.Changed += bad; service.Changed += good;
            try { await configs.InitializeAsync(); Assert.Fail("Observer error was swallowed."); } catch (AggregateException) { }
            Assert.That(configs.Snapshot, Is.Not.Null);
            Assert.That(configs.LastFailure, Is.Null);
            Assert.That(configs.LastNotificationFailure, Is.TypeOf<AggregateException>());
            Assert.That(calls, Is.EqualTo(1));
            Assert.Throws<AggregateException>(() => service.SetLocale("fr"));
            Assert.That(service.Locale, Is.EqualTo("fr"));
            Assert.That(calls, Is.EqualTo(2));
            Assert.Throws<AggregateException>(() => service.Dispose());
            Assert.That(service.IsDisposed, Is.True);
            Assert.That(calls, Is.EqualTo(3));
            await configs.ReloadAsync();
            Assert.That(calls, Is.EqualTo(3), "Disposed service must unsubscribe even when its observers throw.");
            Action recursive = () => configs.ShutdownAsync();
            configs.SnapshotChanged += recursive;
            try { await configs.ReloadAsync(); Assert.Fail("Reentrant mutation was allowed."); } catch (AggregateException) { }
            configs.SnapshotChanged -= recursive;
            await configs.ShutdownAsync();
        }
        [UnityTest]
        public IEnumerator NestedConfigNotificationCannotUnlockOuterLanguageMutationGuard() => Await(Nested());
        private async Task Nested()
        {
            var configs = Create(new Source());
            using var service = new TextLocalizationService(configs, "en");
            await configs.InitializeAsync();
            var entered = false;
            var calls = 0;
            service.Changed += () => {
                if (entered) return;
                entered = true;
                configs.ReloadAsync().GetAwaiter().GetResult();
                service.SetLocale("en");
            };
            service.Changed += () => calls++;
            Assert.Throws<AggregateException>(() => service.SetLocale("fr"));
            Assert.That(service.Locale, Is.EqualTo("fr"));
            Assert.That(calls, Is.EqualTo(2));
            await configs.ShutdownAsync();
        }
        [UnityTest]
        public IEnumerator ShutdownAndCancelledCallerFenceLateReadinessThenAllowReinit() => Await(Late());
        private async Task Late()
        {
            var source = new Source { Gate = new UniTaskCompletionSource() };
            var configs = Create(source);
            using var service = new TextLocalizationService(configs);
            var readiness = new List<bool>();
            service.Changed += () => readiness.Add(service.IsReady);
            using var token = new CancellationTokenSource();
            var cancelled = configs.InitializeAsync(token.Token).AsTask();
            var other = configs.InitializeAsync().AsTask();
            token.Cancel();
            try { await cancelled; Assert.Fail(); } catch (OperationCanceledException) { }
            var shutdown = configs.ShutdownAsync().AsTask();
            source.Gate.TrySetResult();
            try { await other; Assert.Fail(); } catch (OperationCanceledException) { }
            await shutdown;
            CollectionAssert.AreEqual(new[] { false }, readiness);
            source.Gate = null;
            await configs.InitializeAsync();
            CollectionAssert.AreEqual(new[] { false, true }, readiness);
            Assert.That(source.Live, Is.Zero);
            await configs.ShutdownAsync();
        }
    }
}
