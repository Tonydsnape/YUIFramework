using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using MessagePack;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using YUIFramework.Configuration;
using YUIFramework.ConfigGenerated;

namespace YUIFramework.Tests
{
    public sealed class ConfigIntegrationEditModeTests
    {
        private sealed class Value
        {
            internal Value(int value) { Number = value; }
            internal int Number { get; }
        }
        private static readonly ConfigTable<Value> First = new ConfigTable<Value>(
            "First", true, json => new Value(ConfigValue.ReadInt32((JObject)json, "value", "First")));
        private static readonly ConfigTable<Value> Second = new ConfigTable<Value>(
            "Second", true, json => new Value(ConfigValue.ReadInt32((JObject)json, "value", "Second")));
        private sealed class Source : IConfigSource
        {
            internal int Calls, Live, Released;
            internal string Json = "{\"value\":1}";
            internal string FailTable;
            internal UniTaskCompletionSource Gate;
            internal bool ThrowOnRelease;
            public async UniTask<IConfigAsset> LoadAsync(string table, CancellationToken token)
            {
                Calls++;
                if (Gate != null) await Gate.Task; // Deliberately ignores cancellation.
                if (table == FailTable) throw new InvalidOperationException("injected load failure");
                Live++;
                return new Asset(this, Encoding.UTF8.GetBytes(Json));
            }
            private sealed class Asset : IConfigAsset
            {
                private Source _owner;
                public byte[] Bytes { get; }
                internal Asset(Source owner, byte[] bytes) { _owner = owner; Bytes = bytes; }
                public void Dispose()
                {
                    var owner = _owner;
                    if (owner == null) return;
                    _owner = null;
                    owner.Live--; owner.Released++;
                    if (owner.ThrowOnRelease) throw new InvalidOperationException("injected release failure");
                }
            }
        }
        private static ConfigService Service(Source source, params ConfigTable[] tables) =>
            new ConfigService(source, new ConfigCodec(), ConfigFormat.Json, tables.Length == 0 ? new[] { First } : tables);
        private static async Task<T> Within<T>(Task<T> task)
        {
            Assert.That(await Task.WhenAny(task, Task.Delay(5000)), Is.SameAs(task), "Bounded config gate timed out.");
            return await task;
        }
        private static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Config gate timeout.");
            task.GetAwaiter().GetResult();
        }
        private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            try { await action(); }
            catch (T) { return; }
            Assert.Fail($"Expected {typeof(T).Name}");
        }

        [UnityTest]
        public IEnumerator SharedInitializationCallerCancellationDoesNotCancelOtherCaller() => Await(SharedAsync());
        private async Task SharedAsync()
        {
            var source = new Source { Gate = new UniTaskCompletionSource() };
            var service = Service(source);
            using var caller = new CancellationTokenSource();
            var first = service.InitializeAsync(caller.Token).AsTask();
            var second = service.InitializeAsync().AsTask();
            caller.Cancel();
            await ThrowsAsync<OperationCanceledException>(async () => await Within(first));
            Assert.That(source.Calls, Is.EqualTo(1));
            source.Gate.TrySetResult();
            Assert.That((await Within(second)).Get(First).Number, Is.EqualTo(1));
            Assert.That(source.Live, Is.Zero);
            Assert.That(source.Released, Is.EqualTo(1));
            await service.ShutdownAsync();
        }

        [UnityTest]
        public IEnumerator FailureNeverPublishesPartialSnapshotAndRetryReloadAreAtomic() => Await(AtomicAsync());
        private async Task AtomicAsync()
        {
            var source = new Source { FailTable = "Second" };
            var service = Service(source, First, Second);
            await ThrowsAsync<ConfigDataException>(async () => await service.InitializeAsync());
            Assert.That(service.Snapshot, Is.Null);
            Assert.That(source.Live, Is.Zero);
            source.FailTable = null;
            var old = await service.InitializeAsync();
            source.Json = "{\"value\":2}"; source.FailTable = "Second";
            await ThrowsAsync<ConfigDataException>(async () => await service.ReloadAsync());
            Assert.That(service.Snapshot, Is.SameAs(old));
            Assert.That(service.Snapshot.Get(First).Number, Is.EqualTo(1));
            source.FailTable = null;
            var next = await service.ReloadAsync();
            Assert.That(next.Get(First).Number, Is.EqualTo(2));
            Assert.That(old.Get(First).Number, Is.EqualTo(1));
            Assert.That(next.Generation, Is.GreaterThan(old.Generation));
            Assert.That(service.LastFailure, Is.Null);
            await service.ShutdownAsync();
        }

        [UnityTest]
        public IEnumerator ShutdownFencesLateNonCooperativeLoadAndAllowsCleanReinitialization() => Await(ShutdownAsync());
        private async Task ShutdownAsync()
        {
            var source = new Source { Gate = new UniTaskCompletionSource() };
            var service = Service(source);
            var load = service.InitializeAsync().AsTask();
            var shutdown = service.ShutdownAsync().AsTask();
            Assert.Throws<InvalidOperationException>(() => service.InitializeAsync());
            Assert.That(shutdown.IsCompleted, Is.False);
            source.Gate.TrySetResult();
            await ThrowsAsync<OperationCanceledException>(async () => await Within(load));
            await Within(shutdown.ContinueWith(task => { task.GetAwaiter().GetResult(); return true; }));
            Assert.That(service.Snapshot, Is.Null);
            Assert.That(source.Live, Is.Zero);
            source.Gate = null; source.Json = "{\"value\":3}";
            Assert.That((await service.InitializeAsync()).Get(First).Number, Is.EqualTo(3));
            await service.ShutdownAsync();
        }

        [UnityTest]
        public IEnumerator CancelledCallerAndLateFaultRemainObservableWithoutUnobservedTask() => Await(LateFaultAsync());
        private async Task LateFaultAsync()
        {
            var source = new Source { Gate = new UniTaskCompletionSource(), FailTable = "First" };
            var service = Service(source);
            using var token = new CancellationTokenSource();
            var task = service.InitializeAsync(token.Token).AsTask();
            token.Cancel();
            await ThrowsAsync<OperationCanceledException>(async () => await Within(task));
            source.Gate.TrySetResult();
            Assert.That(service.LastFailure, Is.TypeOf<ConfigDataException>());
            Assert.That(service.Snapshot, Is.Null);
            await service.ShutdownAsync();
        }

        [UnityTest]
        public IEnumerator OptionalFailuresAreExplicitAndReleaseFailuresCannotPublish() => Await(OptionalAsync());
        private async Task OptionalAsync()
        {
            var optional = new ConfigTable<Value>("Optional", false, json => new Value(1));
            var source = new Source { FailTable = "Optional" };
            var service = Service(source, First, optional);
            var snapshot = await service.InitializeAsync();
            Assert.That(snapshot.OptionalFailures.Count, Is.EqualTo(1));
            Assert.That(snapshot.TryGet(optional, out _), Is.False);
            Assert.Throws<ConfigNotLoadedException>(() => snapshot.Get(optional));
            await service.ShutdownAsync();
            source.FailTable = null; source.ThrowOnRelease = true;
            await ThrowsAsync<ConfigDataException>(async () => await service.InitializeAsync());
            Assert.That(source.Live, Is.Zero);
            Assert.That(service.Snapshot, Is.Null);
            await service.ShutdownAsync();
            var optionalOnly = Service(source, optional);
            var releasedFailure = await optionalOnly.InitializeAsync();
            Assert.That(releasedFailure.OptionalFailures.Count, Is.EqualTo(1));
            Assert.That(releasedFailure.TryGet(optional, out _), Is.False);
            Assert.That(source.Live, Is.Zero);
            await optionalOnly.ShutdownAsync();
        }

        [Test]
        public void CodecRejectsDuplicateTrailingDeepAndExtensionData()
        {
            var codec = new ConfigCodec();
            Assert.Throws<Newtonsoft.Json.JsonReaderException>(() => codec.Decode(
                Encoding.UTF8.GetBytes("{\"a\":1,\"a\":2}"), ConfigFormat.Json));
            Assert.Throws<ConfigDataException>(() => codec.Decode(new byte[] { 0x80, 0x80 }, ConfigFormat.MessagePack));
            Assert.Throws<ConfigDataException>(() => codec.Decode(new byte[] { 0xd4, 0x00, 0x00 }, ConfigFormat.MessagePack));
            var deep = new List<byte>();
            for (var i = 0; i < 66; i++) deep.Add(0x91);
            deep.Add(0xc0);
            Assert.Throws<ConfigDataException>(() => codec.Decode(deep.ToArray(), ConfigFormat.MessagePack));
            Assert.Throws<ConfigDataException>(() => codec.Decode(new byte[] { 0x81, 0x01, 0x01 }, ConfigFormat.MessagePack));
            Assert.Throws<ConfigDataException>(() => codec.Decode(
                Encoding.UTF8.GetBytes("{\"value\":NaN}"), ConfigFormat.Json));
        }

        [Test]
        public void LongCompositeKeyAndJsonMessagePackProtocolAreEquivalent()
        {
            const string json = "{\"__proto__\":{\"9223372036854775807\":{\"Id\":\"9223372036854775807\",\"value\":[true,null,1.5]}}}";
            var codec = new ConfigCodec();
            var expected = codec.Decode(Encoding.UTF8.GetBytes(json), ConfigFormat.Json);
            var actual = codec.Decode(MessagePackSerializer.ConvertFromJson(json), ConfigFormat.MessagePack);
            Assert.That(JToken.DeepEquals(expected, actual), Is.True);
            Assert.That(ConfigValue.ReadInt64(actual["__proto__"]["9223372036854775807"]["Id"], "id"), Is.EqualTo(long.MaxValue));
            Assert.That(ConfigKey.Compose("a", "b:c"), Is.Not.EqualTo(ConfigKey.Compose("a:b", "c")));
        }

        [UnityTest]
        public IEnumerator GeneratedUiRowsValidatePathIdentityAndRemainReadOnly() => Await(GeneratedAsync());
        private async Task GeneratedAsync()
        {
            var service = SampleUIConfiguration.Create(new EditorJsonConfigSource(), ConfigFormat.Json);
            var snapshot = await service.InitializeAsync();
            var table = snapshot.Get(UISettings.Table);
            Assert.That(table.Count, Is.EqualTo(6));
            Assert.That(table.Get("bootstrap", "HelloPage").UseTransition, Is.False);
            Assert.That(table.Get("y2", "SampleHelloPage").PreloadCount, Is.EqualTo(2));
            Assert.That(table.Get("hello", "SecondSamplePage").SlideDistance, Is.EqualTo(900));
            Assert.That(table.Get("hello", "VirtualListSamplePage").StartScale, Is.EqualTo(.92f));
            Assert.That(table.Get("hello", "MvvmSamplePage").ShowDuration, Is.EqualTo(.18f));
            using var asset = await new EditorJsonConfigSource().LoadAsync("UISettings", default);
            var json = new ConfigCodec().Decode(asset.Bytes, ConfigFormat.Json);
            json["hello"]["HelloPage"]["Id"] = "wrong";
            var bad = new Source { Json = json.ToString() };
            var broken = new ConfigService(bad, new ConfigCodec(), ConfigFormat.Json, GeneratedConfigCatalog.Tables);
            await ThrowsAsync<ConfigDataException>(async () => await broken.InitializeAsync());
            Assert.That(bad.Live, Is.Zero);
            Assert.That(broken.Snapshot, Is.Null);
            await broken.ShutdownAsync();
            await service.ShutdownAsync();
        }

        [Test]
        public void BatchConfigValidationRejectsInvalidFieldsBeforeRegistration()
        {
            var config = new UIConfig { Id = "Id", PrefabKey = "Prefab", Layer = UILayer.Normal };
            foreach (var invalid in new Action<UIConfig>[] {
                value => value.Layer = (UILayer)42, value => value.ShowDuration = float.NaN,
                value => value.HideDuration = -1, value => value.StartScale = 0,
                value => value.PreloadCount = 2, value => value.PrefabPackage = " ",
                value => { value.UseTransition = true; value.TransitionType = UITransitionType.Custom; }
            })
            {
                var copy = config.Copy(); invalid(copy);
                Assert.That(() => UIConfigRegistration.Validate(copy), Throws.InstanceOf<ArgumentException>());
            }
        }

        [Test]
        public void ServiceRejectsDuplicateDescriptorsAndPrecancelledCaller()
        {
            var source = new Source();
            Assert.Throws<ArgumentException>(() => Service(source, First, First));
            using var token = new CancellationTokenSource(); token.Cancel();
            Assert.Throws<OperationCanceledException>(() => Service(source).InitializeAsync(token.Token));
            Assert.That(source.Calls, Is.Zero);
        }
    }
}
