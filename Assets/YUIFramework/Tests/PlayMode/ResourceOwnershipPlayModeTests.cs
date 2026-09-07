using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    /// <summary>
    /// 阶段 5：资源所有权体系的核心语义。
    /// 使用不依赖网络与 YooAsset 的伪 provider，验证共享加载、引用计数、取消与释放。
    /// </summary>
    public sealed class ResourceOwnershipPlayModeTests
    {
        private const string UiPackage = "UIPackage";

        private FakeResourceProvider _provider;
        private UIResourceService _service;

        [SetUp]
        public void SetUp()
        {
            _provider = new FakeResourceProvider(UiPackage);
            _service = new UIResourceService();
            _service.Packages.Register(_provider, isDefault: true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_service != null)
            {
                // 先让挂起的加载完成，避免关闭流程等待一个永不完成的底层加载。
                _provider?.CompleteAllPending();
                yield return Await(_service.ShutdownAsync().AsTask());
            }

            _provider?.DestroyRemainingAssets();
        }

        [UnityTest]
        public IEnumerator SameKeyConcurrentLoads_LoadNativeOnce_AndGiveIndependentLeases()
        {
            _provider.ManualCompletion = true;
            var key = UIResourceKey.Of<GameObject>("UI/Pages/Shared");

            var firstTask = _service.LoadAssetAsync<GameObject>(key).AsTask();
            var secondTask = _service.LoadAssetAsync<GameObject>(key).AsTask();

            Assert.That(_provider.LoadCallCount, Is.EqualTo(1), "同一 key 的并发加载只能触发一次底层加载");

            _provider.CompleteAllPending();
            yield return Await(firstTask);
            yield return Await(secondTask);

            var first = firstTask.Result;
            var second = secondTask.Result;

            Assert.That(first, Is.Not.SameAs(second), "每个调用方必须获得独立租约");
            Assert.That(first.Asset, Is.SameAs(second.Asset), "独立租约应指向同一份底层资源");

            first.Release();
            Assert.That(first.IsReleased, Is.True);
            Assert.That(second.IsReleased, Is.False, "释放一个租约不能影响另一个持有者");
            Assert.That(second.Asset, Is.Not.Null);
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero, "仍有引用时不得释放底层句柄");

            second.Release();
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero, "最后一个租约释放后先进入无引用缓存");

            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1), "底层句柄不得被重复释放");
        }

        [UnityTest]
        public IEnumerator SingleWaiterCancellation_LeavesOtherWaiterAndNativeLoadIntact()
        {
            _provider.ManualCompletion = true;
            var key = UIResourceKey.Of<GameObject>("UI/Pages/CancelOne");

            using var cts = new CancellationTokenSource();
            var canceledTask = _service.LoadAssetAsync<GameObject>(key, cts.Token).AsTask();
            var survivingTask = _service.LoadAssetAsync<GameObject>(key).AsTask();

            cts.Cancel();
            yield return AwaitCancellation(canceledTask);

            _provider.CompleteAllPending();
            yield return Await(survivingTask);

            Assert.That(_provider.LoadCallCount, Is.EqualTo(1), "取消一个等待者不得重复发起底层加载");
            Assert.That(survivingTask.Result.Asset, Is.Not.Null, "其他等待者必须正常拿到资源");
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero, "仍有持有者时不得释放句柄");

            survivingTask.Result.Release();
        }

        [UnityTest]
        public IEnumerator AllWaitersCancel_ReleasesNativeHandleOnceLoadCompletes()
        {
            _provider.ManualCompletion = true;
            var key = UIResourceKey.Of<GameObject>("UI/Pages/CancelAll");

            using var cts = new CancellationTokenSource();
            var firstTask = _service.LoadAssetAsync<GameObject>(key, cts.Token).AsTask();
            var secondTask = _service.LoadAssetAsync<GameObject>(key, cts.Token).AsTask();

            cts.Cancel();
            yield return AwaitCancellation(firstTask);
            yield return AwaitCancellation(secondTask);

            Assert.That(_provider.NativeReleaseCallCount, Is.Zero, "底层尚未完成，还没有句柄可以释放");

            // 底层加载不可取消，完成后必须立即释放，避免泄漏。
            _provider.CompleteAllPending();
            yield return null;
            yield return null;

            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1), "全部等待者取消后必须在完成时立即释放句柄");
            Assert.That(_service.GetDiagnostics().EntryCount, Is.Zero, "被完全取消的条目不应留在缓存中");
        }

        [UnityTest]
        public IEnumerator DifferentPackageLocationAndType_AreNeverMerged()
        {
            var extraProvider = new FakeResourceProvider("ExtraPackage");
            _service.Packages.Register(extraProvider);

            var prefabInUi = UIResourceKey.Of<GameObject>("Shared/Asset", UiPackage);
            var prefabInExtra = UIResourceKey.Of<GameObject>("Shared/Asset", "ExtraPackage");
            var textureInUi = UIResourceKey.Of<Texture2D>("Shared/Asset", UiPackage);
            var otherLocation = UIResourceKey.Of<GameObject>("Shared/Other", UiPackage);

            var a = _service.LoadAssetAsync(prefabInUi).AsTask();
            var b = _service.LoadAssetAsync(prefabInExtra).AsTask();
            var c = _service.LoadAssetAsync(textureInUi).AsTask();
            var d = _service.LoadAssetAsync(otherLocation).AsTask();

            yield return Await(a);
            yield return Await(b);
            yield return Await(c);
            yield return Await(d);

            Assert.That(_provider.LoadCallCount, Is.EqualTo(3), "同 package 内不同 location/type 必须各自加载");
            Assert.That(extraProvider.LoadCallCount, Is.EqualTo(1), "不同 package 必须路由到各自的 provider");
            Assert.That(_service.GetDiagnostics().EntryCount, Is.EqualTo(4));

            a.Result.Release();
            b.Result.Release();
            c.Result.Release();
            d.Result.Release();
            extraProvider.DestroyRemainingAssets();
        }

        [UnityTest]
        public IEnumerator UnknownPackage_FailsExplicitlyWithoutCreatingEntry()
        {
            var key = UIResourceKey.Of<GameObject>("UI/Pages/Missing", "NotRegistered");
            var task = _service.LoadAssetAsync(key).AsTask();

            yield return AwaitFailure(task);

            Assert.That(GetFailure(task), Is.TypeOf<UIResourcePackageNotFoundException>());
            Assert.That(_provider.LoadCallCount, Is.Zero);
            Assert.That(_service.GetDiagnostics().EntryCount, Is.Zero, "未知 package 不得留下任何条目");
        }

        [UnityTest]
        public IEnumerator AssetAndInstanceLeases_AreIndependent_AndReleaseIsIdempotent()
        {
            var key = UIResourceKey.Of<GameObject>("UI/Pages/Instanced");

            var instanceTask = _service.InstantiateAsync(key).AsTask();
            yield return Await(instanceTask);
            var instanceLease = instanceTask.Result;

            var assetTask = _service.LoadAssetAsync<GameObject>(key).AsTask();
            yield return Await(assetTask);
            var assetLease = assetTask.Result;

            Assert.That(_provider.LoadCallCount, Is.EqualTo(1), "实例化与资源加载共享同一次底层加载");
            Assert.That(instanceLease.Instance, Is.Not.Null);
            Assert.That(instanceLease.Instance, Is.Not.SameAs(assetLease.Asset), "实例必须与源资源分离");

            // 幂等：重复释放与 Dispose 只生效一次。
            instanceLease.Release();
            instanceLease.Release();
            instanceLease.Dispose();

            Assert.That(instanceLease.IsReleased, Is.True);
            Assert.That(instanceLease.Instance, Is.Null);
            Assert.That(assetLease.IsReleased, Is.False, "释放实例不得连带释放其他持有者的资源租约");
            Assert.That(assetLease.Asset, Is.Not.Null);
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero);

            assetLease.Release();
            assetLease.Dispose();
            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1), "重复 Dispose 不得造成二次释放");
        }

        [UnityTest]
        public IEnumerator Preload_PopulatesCache_AndLaterLoadHits()
        {
            var key = UIResourceKey.Of<GameObject>("UI/Pages/Preloaded");

            var preloadTask = _service.PreloadAsync(new[] { key }).AsTask();
            yield return Await(preloadTask);

            Assert.That(preloadTask.Result, Is.EqualTo(1));
            Assert.That(_provider.LoadCallCount, Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.Zero, "预加载后资源应保留为无引用缓存");

            var snapshot = _service.GetDiagnostics();
            Assert.That(snapshot.UnreferencedEntries, Has.Count.EqualTo(1));

            var loadTask = _service.LoadAssetAsync<GameObject>(key).AsTask();
            yield return Await(loadTask);

            Assert.That(_provider.LoadCallCount, Is.EqualTo(1), "预加载命中后不得再次发起底层加载");
            Assert.That(loadTask.Result.Asset, Is.Not.Null);
            loadTask.Result.Release();
        }

        [UnityTest]
        public IEnumerator Batch_PartialFailure_KeepsSuccessfulOwnershipAndAggregatesErrors()
        {
            var good = UIResourceKey.Of<GameObject>("UI/Pages/Good");
            var bad = UIResourceKey.Of<GameObject>("UI/Pages/Bad");
            _provider.FailingLocations.Add(bad.Location);

            var batchTask = _service.LoadBatchAsync(new[] { good, bad }).AsTask();
            yield return Await(batchTask);

            var result = batchTask.Result;
            Assert.That(result.HasFailures, Is.True);
            Assert.That(result.Leases, Has.Count.EqualTo(1), "部分失败不得丢弃已成功的项");
            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(result.Failures[0].Key, Is.EqualTo(bad.WithPackage(UiPackage)), "失败项必须被准确定位");

            var succeeded = result.Leases[0];
            Assert.That(succeeded.Asset, Is.Not.Null, "成功项的所有权必须明确交给调用方");
            Assert.That(succeeded.Key, Is.EqualTo(good.WithPackage(UiPackage)));
            Assert.That(result.ToAggregateException(), Is.Not.Null);

            result.Dispose();
            Assert.That(succeeded.IsReleased, Is.True);

            Assert.That(_service.TrimUnused(), Is.EqualTo(1));
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator LowMemory_ClearsOnlyUnreferencedEntries()
        {
            var held = UIResourceKey.Of<GameObject>("UI/Pages/Held");
            var cached = UIResourceKey.Of<GameObject>("UI/Pages/Cached");

            var heldTask = _service.LoadAssetAsync<GameObject>(held).AsTask();
            yield return Await(heldTask);
            var heldLease = heldTask.Result;

            var preloadTask = _service.PreloadAsync(new[] { cached }).AsTask();
            yield return Await(preloadTask);

            var freed = _service.HandleLowMemory();

            Assert.That(freed, Is.EqualTo(1), "低内存只清理无引用缓存");
            Assert.That(heldLease.IsReleased, Is.False);
            Assert.That(heldLease.Asset, Is.Not.Null, "仍被持有的资源不得被低内存回收");
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));

            heldLease.Release();
        }

        [UnityTest]
        public IEnumerator Diagnostics_ReportOutstandingLeasesForLeakDetection()
        {
            var key = UIResourceKey.Of<GameObject>("UI/Pages/Leaky");

            var loadTask = _service.LoadAssetAsync<GameObject>(key).AsTask();
            yield return Await(loadTask);
            var lease = loadTask.Result;

            var leaked = _service.GetDiagnostics();
            Assert.That(leaked.TotalLeases, Is.EqualTo(1));
            Assert.That(leaked.HasOutstandingLeases, Is.True);
            Assert.That(leaked.LeasedEntries, Has.Count.EqualTo(1));
            Assert.That(leaked.LeasedEntries[0].Key, Is.EqualTo(key.WithPackage(UiPackage)));
            Assert.That(leaked.NativeLoadCount, Is.EqualTo(1));

            lease.Release();

            var settled = _service.GetDiagnostics();
            Assert.That(settled.HasOutstandingLeases, Is.False);
            Assert.That(settled.UnreferencedEntries, Has.Count.EqualTo(1));

            _service.TrimUnused();
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True, "全部释放后底层加载/释放必须配平");
        }

        [UnityTest]
        public IEnumerator Shutdown_DrainsInFlightLoadWithoutDeadlockAndReleasesHandle()
        {
            _provider.ManualCompletion = true;
            var key = UIResourceKey.Of<GameObject>("UI/Pages/ShutdownRace");

            var pendingLoad = _service.LoadAssetAsync<GameObject>(key).AsTask();
            var shutdown = _service.ShutdownAsync().AsTask();

            Assert.That(shutdown.IsCompleted, Is.False, "关闭必须等待进行中的加载");

            _provider.CompleteAllPending();
            yield return Await(shutdown);

            Assert.That(_service.IsShutDown, Is.True);
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1), "关闭必须释放进行中加载产生的句柄");
            Assert.That(_provider.MaxReleaseCallsOnAnyHandle, Is.EqualTo(1));
            Assert.That(_provider.ShutdownCallCount, Is.EqualTo(1), "关闭必须传播到 provider");

            // 该调用方不会拿到租约，但必须以确定性的方式结束，且异常已被观察。
            while (!pendingLoad.IsCompleted)
            {
                yield return null;
            }

            Assert.That(pendingLoad.IsFaulted || pendingLoad.IsCanceled, Is.True);
            if (pendingLoad.IsFaulted)
            {
                Assert.That(GetFailure(pendingLoad), Is.TypeOf<ObjectDisposedException>());
            }
        }

        [UnityTest]
        public IEnumerator Shutdown_RejectsNewLoads()
        {
            var shutdown = _service.ShutdownAsync().AsTask();
            yield return Await(shutdown);

            var task = _service.LoadAssetAsync<GameObject>(UIResourceKey.Of<GameObject>("UI/Pages/After")).AsTask();
            yield return AwaitFailure(task);

            Assert.That(GetFailure(task), Is.TypeOf<ObjectDisposedException>());
            Assert.That(_provider.LoadCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ConcurrentShutdownCallers_AwaitOneSharedCleanup()
        {
            _provider.ManualCompletion = true;
            var pendingLoad = _service.LoadAssetAsync<GameObject>(
                    UIResourceKey.Of<GameObject>("UI/Pages/SharedShutdown"))
                .AsTask();

            var first = _service.ShutdownAsync().AsTask();
            var second = _service.ShutdownAsync().AsTask();

            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            _provider.CompleteAllPending();

            yield return Await(first);
            yield return Await(second);
            Assert.That(_provider.ShutdownCallCount, Is.EqualTo(1));

            while (!pendingLoad.IsCompleted)
            {
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ProviderShutdownFailure_CanRetryCleanup()
        {
            var provider = new FakeResourceProvider("FailingPackage")
            {
                ShutdownFailure = new InvalidOperationException("forced-shutdown-failure"),
            };
            var service = new UIResourceService();
            service.Packages.Register(provider, isDefault: true);

            var first = service.ShutdownAsync().AsTask();
            yield return AwaitFailure(first);
            provider.ShutdownFailure = null;
            var second = service.ShutdownAsync().AsTask();
            yield return Await(second);

            Assert.That(provider.ShutdownCallCount, Is.EqualTo(2));
            Assert.That(GetFailure(first).Message, Does.Contain("Provider"));
        }

        [UnityTest]
        public IEnumerator ReleaseUnusedFailure_IsNotReportedAsSuccess_AndShutdownRetries()
        {
            var key = UIResourceKey.Of<GameObject>("UI/Pages/ReleaseRetry");
            var load = _service.LoadAssetAsync<GameObject>(key).AsTask();
            yield return Await(load);
            load.Result.Release();
            _provider.ReleaseFailuresRemaining = 1;

            Assert.Throws<InvalidOperationException>(() => _service.ReleaseUnused(key));
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.False);

            yield return Await(_service.ShutdownAsync().AsTask());
            Assert.That(_service.GetDiagnostics().IsNativeBalanced, Is.True);
            Assert.That(_provider.NativeReleaseCallCount, Is.EqualTo(1));
        }

        internal static IEnumerator Await(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsCanceled)
            {
                throw new TaskCanceledException(task);
            }

            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? new InvalidOperationException("Task failed.");
            }
        }

        internal static IEnumerator AwaitCancellation(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException)
            {
                yield break;
            }

            Assert.Fail("Expected the operation to be canceled.");
        }

        internal static IEnumerator AwaitFailure(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (!task.IsFaulted)
            {
                Assert.Fail("Expected the operation to fail.");
            }
        }

        internal static Exception GetFailure(Task task)
        {
            return task.Exception?.GetBaseException();
        }
    }

    /// <summary>
    /// 不依赖 YooAsset 与网络的资源 provider 伪实现，可精确控制完成时机与失败位置。
    /// </summary>
    internal sealed class FakeResourceProvider : IUIResourceProvider
    {
        private readonly List<PendingLoad> _pending = new List<PendingLoad>();
        private readonly List<FakeHandle> _handles = new List<FakeHandle>();

        public FakeResourceProvider(string packageName)
        {
            PackageName = packageName;
        }

        public string PackageName { get; }

        public int LoadCallCount { get; private set; }

        public int ShutdownCallCount { get; private set; }

        public bool ManualCompletion { get; set; }

        public Exception ShutdownFailure { get; set; }

        public int ReleaseFailuresRemaining { get; set; }

        public HashSet<string> FailingLocations { get; } = new HashSet<string>(StringComparer.Ordinal);

        public int NativeReleaseCallCount { get; private set; }

        public int MaxReleaseCallsOnAnyHandle
        {
            get
            {
                var max = 0;
                foreach (var handle in _handles)
                {
                    if (handle.ReleaseCallCount > max)
                    {
                        max = handle.ReleaseCallCount;
                    }
                }

                return max;
            }
        }

        public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
        {
            LoadCallCount++;
            var shouldFail = FailingLocations.Contains(key.Location);

            if (!ManualCompletion)
            {
                if (shouldFail)
                {
                    throw new ResourceLoadException(key.Location, PackageName, "Forced failure from the fake provider.");
                }

                return UniTask.FromResult<IUINativeAssetHandle>(CreateHandle(key));
            }

            var pending = new PendingLoad(key, shouldFail);
            _pending.Add(pending);
            return pending.Source.Task;
        }

        public UniTask ShutdownAsync()
        {
            ShutdownCallCount++;
            if (ShutdownFailure != null)
            {
                throw ShutdownFailure;
            }

            return UniTask.CompletedTask;
        }

        public void CompleteAllPending()
        {
            var snapshot = _pending.ToArray();
            _pending.Clear();

            foreach (var pending in snapshot)
            {
                if (pending.ShouldFail)
                {
                    pending.Source.TrySetException(
                        new ResourceLoadException(pending.Key.Location, PackageName, "Forced failure from the fake provider."));
                }
                else
                {
                    pending.Source.TrySetResult(CreateHandle(pending.Key));
                }
            }
        }

        public void DestroyRemainingAssets()
        {
            foreach (var handle in _handles)
            {
                handle.DestroyAsset();
            }

            _handles.Clear();
        }

        private FakeHandle CreateHandle(UIResourceKey key)
        {
            var asset = new GameObject($"FakeAsset_{key.Location}", typeof(RectTransform), typeof(UIView));
            asset.SetActive(false);
            asset.hideFlags = HideFlags.DontSave;

            var handle = new FakeHandle(this, asset);
            _handles.Add(handle);
            return handle;
        }

        private void OnHandleReleased()
        {
            NativeReleaseCallCount++;
        }

        private sealed class PendingLoad
        {
            public PendingLoad(UIResourceKey key, bool shouldFail)
            {
                Key = key;
                ShouldFail = shouldFail;
                Source = new UniTaskCompletionSource<IUINativeAssetHandle>();
            }

            public UIResourceKey Key { get; }

            public bool ShouldFail { get; }

            public UniTaskCompletionSource<IUINativeAssetHandle> Source { get; }
        }

        private sealed class FakeHandle : IUINativeAssetHandle
        {
            private readonly FakeResourceProvider _owner;
            private GameObject _asset;

            public FakeHandle(FakeResourceProvider owner, GameObject asset)
            {
                _owner = owner;
                _asset = asset;
            }

            public int ReleaseCallCount { get; private set; }

            public UnityEngine.Object Asset => _asset;

            public bool IsValid => _asset != null;

            public void Release()
            {
                ReleaseCallCount++;
                if (_owner.ReleaseFailuresRemaining > 0)
                {
                    _owner.ReleaseFailuresRemaining--;
                    throw new InvalidOperationException("Forced native release failure.");
                }

                if (_asset == null)
                {
                    return;
                }

                _owner.OnHandleReleased();
                DestroyAsset();
            }

            public void DestroyAsset()
            {
                if (_asset == null)
                {
                    return;
                }

                var asset = _asset;
                _asset = null;
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
