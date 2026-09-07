using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// <see cref="IUIResourceService"/> 的默认实现。
    /// 负责共享加载（single-flight）、引用计数、租约发放与释放、取消语义与泄漏诊断。
    /// </summary>
    public sealed class UIResourceService : IUIResourceService
    {
        private const string LoaderName = nameof(UIResourceService);

        private readonly object _gate = new object();
        private readonly Dictionary<UIResourceKey, Entry> _entries = new Dictionary<UIResourceKey, Entry>();
        private readonly IUIResourcePackageRegistry _registry;
        private readonly List<IUINativeAssetHandle> _failedReleaseHandles =
            new List<IUINativeAssetHandle>();

        private bool _shutDown;
        private Task _shutdownTask;
        private long _nativeLoadCount;
        private long _nativeReleaseCount;

        public UIResourceService(IUIResourcePackageRegistry registry = null)
        {
            _registry = registry ?? new UIResourcePackageRegistry();
        }

        public IUIResourcePackageRegistry Packages => _registry;

        public bool IsShutDown
        {
            get
            {
                lock (_gate)
                {
                    return _shutDown;
                }
            }
        }

        public async UniTask<IUIAssetLease<T>> LoadAssetAsync<T>(
            UIResourceKey key,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            var lease = await AcquireAssetLeaseAsync(key, typeof(T), cancellationToken);
            return new TypedAssetLease<T>(lease);
        }

        public UniTask<IUIAssetLease<T>> LoadAssetAsync<T>(
            string location,
            string packageName = null,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            return LoadAssetAsync<T>(UIResourceKey.Of<T>(location, packageName), cancellationToken);
        }

        public UniTask<IUIAssetLease> LoadAssetAsync(
            UIResourceKey key,
            CancellationToken cancellationToken = default)
        {
            return AcquireAssetLeaseAsync(key, null, cancellationToken);
        }

        public async UniTask<IUIInstanceLease> InstantiateAsync(
            UIResourceKey key,
            Transform parent = null,
            CancellationToken cancellationToken = default)
        {
            var lease = await AcquireAssetLeaseAsync(key, null, cancellationToken);

            GameObject instance;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!(lease.Asset is GameObject prefab))
                {
                    throw new ResourceLoadException(
                        key.Location,
                        LoaderName,
                        $"Cannot instantiate {key}: the loaded asset is not a GameObject.");
                }

                instance = parent == null
                    ? UnityEngine.Object.Instantiate(prefab)
                    : UnityEngine.Object.Instantiate(prefab, parent);
            }
            catch
            {
                // 实例化失败/取消时，必须归还刚刚取得的资源租约，避免泄漏。
                lease.Release();
                throw;
            }

            return new InstanceLease(lease, instance);
        }

        public async UniTask<int> PreloadAsync(
            IEnumerable<UIResourceKey> keys,
            CancellationToken cancellationToken = default)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            var result = await LoadBatchAsync(keys, cancellationToken);
            var loaded = result.Leases.Count;

            // 预加载只负责把资源放进无引用缓存，不保留所有权。
            result.Dispose();
            return loaded;
        }

        public async UniTask<UIResourceBatchResult> LoadBatchAsync(
            IEnumerable<UIResourceKey> keys,
            CancellationToken cancellationToken = default)
        {
            if (keys == null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            var requested = new List<UIResourceKey>(keys);
            var pending = new List<UniTask<BatchItem>>(requested.Count);
            foreach (var key in requested)
            {
                pending.Add(LoadBatchItemAsync(key, cancellationToken));
            }

            // WhenAll 不会因为单项失败而丢弃其他项：每项都被包装成 BatchItem，永不抛出。
            var items = await UniTask.WhenAll(pending);

            var leases = new List<IUIAssetLease>();
            var failures = new List<UIResourceBatchFailure>();
            var canceled = false;
            foreach (var item in items)
            {
                if (item.Lease != null)
                {
                    leases.Add(item.Lease);
                }
                else if (item.Error is OperationCanceledException)
                {
                    canceled = true;
                }
                else if (item.Error != null)
                {
                    failures.Add(new UIResourceBatchFailure(item.Key, item.Error));
                }
            }

            if (canceled)
            {
                // 整批被取消时不能把已成功的租约泄漏出去。
                foreach (var lease in leases)
                {
                    lease.Release();
                }

                throw new OperationCanceledException(cancellationToken);
            }

            return new UIResourceBatchResult(leases, failures);
        }

        public bool ReleaseUnused(UIResourceKey key)
        {
            IUINativeAssetHandle handle = null;
            lock (_gate)
            {
                if (!TryNormalizeLocked(key, out var normalized))
                {
                    return false;
                }

                if (!_entries.TryGetValue(normalized, out var entry))
                {
                    return false;
                }

                if (!TryDetachUnreferencedLocked(normalized, entry, ref handle))
                {
                    return false;
                }
            }

            if (!TryReleaseHandle(handle, out var releaseError))
            {
                throw new InvalidOperationException(
                    $"Failed to release unused resource {key}.",
                    releaseError);
            }

            return true;
        }

        public int TrimUnused()
        {
            var released = new List<IUINativeAssetHandle>();
            lock (_gate)
            {
                var dead = new List<UIResourceKey>();
                foreach (var pair in _entries)
                {
                    IUINativeAssetHandle handle = null;
                    if (TryDetachUnreferencedLocked(pair.Key, pair.Value, ref handle, removeFromDictionary: false))
                    {
                        dead.Add(pair.Key);
                        released.Add(handle);
                    }
                }

                foreach (var key in dead)
                {
                    _entries.Remove(key);
                }
            }

            var failures = new List<Exception>();
            var releasedCount = 0;
            foreach (var handle in released)
            {
                if (TryReleaseHandle(handle, out var releaseError))
                {
                    releasedCount++;
                }
                else
                {
                    failures.Add(releaseError);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more unused resource handles failed to release.",
                    failures);
            }

            return releasedCount;
        }

        public int HandleLowMemory()
        {
            // 低内存只清理无引用缓存，绝不影响仍被持有的资源。
            return TrimUnused();
        }

        public UIResourceDiagnosticsSnapshot GetDiagnostics()
        {
            lock (_gate)
            {
                var snapshots = new List<UIResourceEntrySnapshot>(_entries.Count);
                foreach (var pair in _entries)
                {
                    var entry = pair.Value;
                    snapshots.Add(new UIResourceEntrySnapshot(
                        pair.Key,
                        entry.LeaseCount,
                        entry.WaiterCount,
                        entry.Completed && entry.Handle != null && !entry.NativeReleased,
                        !entry.Completed));
                }

                return new UIResourceDiagnosticsSnapshot(
                    snapshots,
                    _nativeLoadCount,
                    _nativeReleaseCount,
                    _shutDown);
            }
        }

        public UniTask ShutdownAsync()
        {
            List<UniTask<LoadOutcome>> inFlight;
            List<IUIResourceProvider> providers;
            Task shutdownTask;

            _registry.Freeze();
            lock (_gate)
            {
                if (_shutdownTask != null &&
                    (!_shutdownTask.IsCompleted ||
                     (!_shutdownTask.IsFaulted && !_shutdownTask.IsCanceled)))
                {
                    return _shutdownTask.AsUniTask();
                }

                _shutDown = true;
                inFlight = new List<UniTask<LoadOutcome>>();
                foreach (var pair in _entries)
                {
                    if (!pair.Value.Completed)
                    {
                        inFlight.Add(pair.Value.Shared);
                    }
                }

                providers = new List<IUIResourceProvider>(_registry.GetProviders());
                _shutdownTask = ShutdownCoreAsync(inFlight, providers).AsTask();
                shutdownTask = _shutdownTask;
            }

            return shutdownTask.AsUniTask();
        }

        private async UniTask ShutdownCoreAsync(
            IReadOnlyList<UniTask<LoadOutcome>> inFlight,
            IReadOnlyList<IUIResourceProvider> providers)
        {
            var failures = new List<Exception>();
            // 等待进行中的加载自然完成（底层不可取消），期间不持锁，避免死锁。
            foreach (var task in inFlight)
            {
                try
                {
                    await task;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            var released = new List<IUINativeAssetHandle>();
            lock (_gate)
            {
                foreach (var pair in _entries)
                {
                    var entry = pair.Value;
                    if (entry.NativeReleased || entry.Handle == null)
                    {
                        continue;
                    }

                    entry.NativeReleased = true;
                    released.Add(entry.Handle);
                }

                _entries.Clear();
                released.AddRange(_failedReleaseHandles);
                _failedReleaseHandles.Clear();
            }

            foreach (var handle in released)
            {
                if (!TryReleaseHandle(handle, out var releaseError))
                {
                    failures.Add(releaseError);
                }
            }

            foreach (var provider in providers)
            {
                try
                {
                    await provider.ShutdownAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(
                        new InvalidOperationException(
                            $"Provider \"{provider.PackageName}\" failed to shut down.",
                            exception));
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException("UI resource shutdown did not complete cleanly.", failures);
            }
        }

        private async UniTask<BatchItem> LoadBatchItemAsync(
            UIResourceKey key,
            CancellationToken cancellationToken)
        {
            try
            {
                var lease = await AcquireAssetLeaseAsync(key, null, cancellationToken);
                return new BatchItem(lease.Key, lease, null);
            }
            catch (Exception exception)
            {
                // 失败项也要报告已解析 package 的规范化键，与成功项保持一致。
                return new BatchItem(NormalizeForReport(key), null, exception);
            }
        }

        private UIResourceKey NormalizeForReport(UIResourceKey key)
        {
            if (!key.IsValid)
            {
                return key;
            }

            return _registry.TryResolve(key.PackageName, out var provider)
                ? key.WithPackage(provider.PackageName)
                : key;
        }

        private async UniTask<IUIAssetLease> AcquireAssetLeaseAsync(
            UIResourceKey key,
            Type expectedType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!key.IsValid)
            {
                throw new ArgumentException("UIResourceKey is not initialized.", nameof(key));
            }

            if (expectedType != null && key.AssetType != expectedType)
            {
                throw new ArgumentException(
                    $"Requested lease type {expectedType.Name} does not match key asset type {key.AssetType.Name} for {key}.",
                    nameof(key));
            }

            // 未注册 package 会在这里明确抛出，且不会创建任何条目。
            _registry.Freeze();
            var provider = _registry.Resolve(key.PackageName);
            var normalized = key.WithPackage(provider.PackageName);

            Entry entry;
            UniTask<LoadOutcome> shared;
            UniTaskCompletionSource<LoadOutcome> startedSource = null;

            lock (_gate)
            {
                ThrowIfShutDownLocked();

                if (!_entries.TryGetValue(normalized, out entry))
                {
                    startedSource = new UniTaskCompletionSource<LoadOutcome>();
                    entry = new Entry(normalized, startedSource);
                    _entries[normalized] = entry;
                }

                entry.WaiterCount++;
                shared = entry.Shared;
            }

            if (startedSource != null)
            {
                // 在锁外发起底层加载，避免在持锁状态下调用外部代码。
                RunLoadAsync(entry, provider, startedSource).Forget(Debug.LogException);
            }

            LoadOutcome outcome;
            try
            {
                // AttachExternalCancellation 只取消“当前调用方的等待”，
                // 共享的底层加载与其他等待者完全不受影响。
                outcome = await shared.AttachExternalCancellation(cancellationToken);
            }
            catch (Exception)
            {
                OnWaiterLeft(entry);
                throw;
            }

            IUINativeAssetHandle handle;
            bool wasShutDown;
            lock (_gate)
            {
                entry.WaiterCount--;
                wasShutDown = _shutDown;

                if (outcome.Error != null || outcome.Handle == null || !outcome.Handle.IsValid)
                {
                    MaybeReleaseAbandonedLocked(entry, out handle);
                }
                else if (wasShutDown)
                {
                    // 关闭流程会统一清扫并释放该句柄，这里不重复释放。
                    handle = null;
                }
                else
                {
                    entry.LeaseCount++;
                    entry.EverLeased = true;
                    return new AssetLease(this, entry, outcome.Handle);
                }
            }

            SafeReleaseHandle(handle);

            if (outcome.Error != null)
            {
                throw new ResourceLoadException(
                    normalized.Location,
                    LoaderName,
                    $"Failed to load {normalized}.",
                    outcome.Error);
            }

            if (wasShutDown && outcome.Handle != null)
            {
                throw new ObjectDisposedException(
                    LoaderName,
                    $"UI resource service was shut down while loading {normalized}.");
            }

            throw new ResourceLoadException(
                normalized.Location,
                LoaderName,
                $"Provider \"{provider.PackageName}\" returned no valid asset for {normalized}.");
        }

        private async UniTask RunLoadAsync(
            Entry entry,
            IUIResourceProvider provider,
            UniTaskCompletionSource<LoadOutcome> source)
        {
            IUINativeAssetHandle handle = null;
            Exception error = null;

            try
            {
                handle = await provider.LoadAssetAsync(entry.Key);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            var releaseNow = false;
            var succeeded = false;
            lock (_gate)
            {
                entry.Handle = handle;

                succeeded = error == null && handle != null && handle.IsValid;
                if (succeeded)
                {
                    _nativeLoadCount++;
                }

                if (succeeded && entry.WaiterCount == 0 && entry.LeaseCount == 0)
                {
                    // 所有等待者都已取消：底层不可取消，完成后立即释放句柄。
                    releaseNow = true;
                    entry.NativeReleased = true;
                }
            }

            if (releaseNow)
            {
                SafeReleaseHandle(handle);
            }

            IUINativeAssetHandle lateRelease = null;
            lock (_gate)
            {
                entry.Completed = true;
                if (!succeeded)
                {
                    // 失败的加载不进缓存，后续请求会重新发起。
                    RemoveEntryLocked(entry);
                }
                else if (releaseNow)
                {
                    RemoveEntryLocked(entry);
                }
                else if (entry.WaiterCount == 0 &&
                         entry.LeaseCount == 0 &&
                         !entry.EverLeased)
                {
                    // provider 完成与最后一个等待者取消可能同帧竞态；在发布共享结果前收口。
                    entry.NativeReleased = true;
                    lateRelease = handle;
                    RemoveEntryLocked(entry);
                }
            }

            if (lateRelease != null)
            {
                SafeReleaseHandle(lateRelease);
            }

            // 共享任务永远以“成功”完成，错误随结果一起传递，
            // 因此即使没有任何等待者也不会产生未观察异常。
            source.TrySetResult(new LoadOutcome(handle, error));
        }

        private void OnWaiterLeft(Entry entry)
        {
            IUINativeAssetHandle handle;
            lock (_gate)
            {
                entry.WaiterCount--;
                MaybeReleaseAbandonedLocked(entry, out handle);
            }

            SafeReleaseHandle(handle);
        }

        /// <summary>
        /// 若条目已完成、从未发放过租约且已无等待者，则释放其底层句柄。
        /// 仍在加载中的条目由 <see cref="RunLoadAsync"/> 在完成时处理。
        /// </summary>
        private void MaybeReleaseAbandonedLocked(Entry entry, out IUINativeAssetHandle handle)
        {
            handle = null;

            if (entry.NativeReleased || entry.WaiterCount > 0 || entry.LeaseCount > 0 || entry.EverLeased)
            {
                return;
            }

            if (!entry.Completed)
            {
                return;
            }

            if (entry.Handle != null && entry.Handle.IsValid)
            {
                handle = entry.Handle;
                entry.NativeReleased = true;
            }

            RemoveEntryLocked(entry);
        }

        private bool TryDetachUnreferencedLocked(
            UIResourceKey key,
            Entry entry,
            ref IUINativeAssetHandle handle,
            bool removeFromDictionary = true)
        {
            if (entry.NativeReleased
                || !entry.Completed
                || entry.LeaseCount > 0
                || entry.WaiterCount > 0
                || entry.Handle == null)
            {
                return false;
            }

            entry.NativeReleased = true;
            handle = entry.Handle;

            if (removeFromDictionary)
            {
                _entries.Remove(key);
            }

            return true;
        }

        private bool TryNormalizeLocked(UIResourceKey key, out UIResourceKey normalized)
        {
            normalized = default;
            if (!key.IsValid)
            {
                return false;
            }

            if (!_registry.TryResolve(key.PackageName, out var provider))
            {
                return false;
            }

            normalized = key.WithPackage(provider.PackageName);
            return true;
        }

        private void RemoveEntryLocked(Entry entry)
        {
            if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(entry.Key);
            }
        }

        private void ThrowIfShutDownLocked()
        {
            if (_shutDown)
            {
                throw new ObjectDisposedException(LoaderName, "UI resource service has been shut down.");
            }
        }

        private void ReleaseLease(Entry entry)
        {
            lock (_gate)
            {
                if (entry.LeaseCount > 0)
                {
                    entry.LeaseCount--;
                }

                // 最后一份租约释放后条目保留为无引用缓存，
                // 由 TrimUnused/HandleLowMemory/ShutdownAsync 统一回收。
            }
        }

        private void SafeReleaseHandle(IUINativeAssetHandle handle)
        {
            if (!TryReleaseHandle(handle, out var exception))
            {
                Debug.LogWarning($"[{LoaderName}] Native handle release failed: {exception.Message}");
            }
        }

        private bool TryReleaseHandle(
            IUINativeAssetHandle handle,
            out Exception releaseError)
        {
            releaseError = null;
            if (handle == null)
            {
                return true;
            }

            try
            {
                handle.Release();
                lock (_gate)
                {
                    _nativeReleaseCount++;
                    RemoveFailedReleaseLocked(handle);
                }

                return true;
            }
            catch (Exception exception)
            {
                releaseError = exception;
                lock (_gate)
                {
                    var tracked = false;
                    foreach (var pending in _failedReleaseHandles)
                    {
                        if (ReferenceEquals(pending, handle))
                        {
                            tracked = true;
                            break;
                        }
                    }

                    if (!tracked)
                    {
                        _failedReleaseHandles.Add(handle);
                    }
                }

                return false;
            }
        }

        private void RemoveFailedReleaseLocked(IUINativeAssetHandle handle)
        {
            for (var i = _failedReleaseHandles.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_failedReleaseHandles[i], handle))
                {
                    _failedReleaseHandles.RemoveAt(i);
                }
            }
        }

        internal static void DestroyInstance(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private readonly struct LoadOutcome
        {
            public LoadOutcome(IUINativeAssetHandle handle, Exception error)
            {
                Handle = handle;
                Error = error;
            }

            public IUINativeAssetHandle Handle { get; }

            public Exception Error { get; }
        }

        private readonly struct BatchItem
        {
            public BatchItem(UIResourceKey key, IUIAssetLease lease, Exception error)
            {
                Key = key;
                Lease = lease;
                Error = error;
            }

            public UIResourceKey Key { get; }

            public IUIAssetLease Lease { get; }

            public Exception Error { get; }
        }

        private sealed class Entry
        {
            public Entry(UIResourceKey key, UniTaskCompletionSource<LoadOutcome> source)
            {
                Key = key;
                Shared = source.Task.Preserve();
            }

            public UIResourceKey Key { get; }

            /// <summary>共享的加载任务，已 Preserve，可被多个等待者重复 await。</summary>
            public UniTask<LoadOutcome> Shared { get; }

            public IUINativeAssetHandle Handle { get; set; }

            public int LeaseCount { get; set; }

            public int WaiterCount { get; set; }

            public bool Completed { get; set; }

            public bool NativeReleased { get; set; }

            /// <summary>是否曾经发放过租约；用于区分“被完全取消”与“正常的无引用缓存”。</summary>
            public bool EverLeased { get; set; }
        }

        private sealed class AssetLease : IUIAssetLease
        {
            private readonly UIResourceService _service;
            private readonly Entry _entry;
            private IUINativeAssetHandle _handle;
            private int _released;

            public AssetLease(UIResourceService service, Entry entry, IUINativeAssetHandle handle)
            {
                _service = service;
                _entry = entry;
                _handle = handle;
                Key = entry.Key;
            }

            public UIResourceKey Key { get; }

            public string PackageName => Key.PackageName;

            public string Location => Key.Location;

            public Type AssetType => Key.AssetType;

            public UnityEngine.Object Asset => Volatile.Read(ref _released) != 0 ? null : _handle?.Asset;

            public bool IsReleased => Volatile.Read(ref _released) != 0;

            public void Release()
            {
                // Interlocked 保证幂等：重复 Release/Dispose 只会真正归还一次引用计数。
                if (Interlocked.Exchange(ref _released, 1) != 0)
                {
                    return;
                }

                _handle = null;
                _service.ReleaseLease(_entry);
            }

            public void Dispose() => Release();
        }

        private sealed class TypedAssetLease<T> : IUIAssetLease<T>
            where T : UnityEngine.Object
        {
            private readonly IUIAssetLease _inner;

            public TypedAssetLease(IUIAssetLease inner)
            {
                _inner = inner;
            }

            public T Asset => _inner.Asset as T;

            UnityEngine.Object IUIAssetLease.Asset => _inner.Asset;

            public UIResourceKey Key => _inner.Key;

            public string PackageName => _inner.PackageName;

            public string Location => _inner.Location;

            public Type AssetType => _inner.AssetType;

            public bool IsReleased => _inner.IsReleased;

            public void Release() => _inner.Release();

            public void Dispose() => _inner.Release();
        }

        private sealed class InstanceLease : IUIInstanceLease
        {
            private IUIAssetLease _assetLease;
            private GameObject _instance;
            private int _released;

            public InstanceLease(IUIAssetLease assetLease, GameObject instance)
            {
                _assetLease = assetLease;
                _instance = instance;
                Key = assetLease.Key;
            }

            public UIResourceKey Key { get; }

            public GameObject Instance => Volatile.Read(ref _released) != 0 ? null : _instance;

            public IUIAssetLease AssetLease => Volatile.Read(ref _released) != 0 ? null : _assetLease;

            public bool IsReleased => Volatile.Read(ref _released) != 0;

            public void Release()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                {
                    return;
                }

                var instance = _instance;
                var lease = _assetLease;
                _instance = null;
                _assetLease = null;

                DestroyInstance(instance);
                lease?.Release();
            }

            public void Dispose() => Release();
        }
    }
}
