using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using global::YooAsset;

namespace YUIFramework.Bootstrap.YooAsset
{
    internal static class YooAssetRuntimeOwnership
    {
        private static readonly object Gate = new object();
        private static int _clientCount;
        private static bool _initializedByBootstrap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetTracking()
        {
            lock (Gate)
            {
                _clientCount = 0;
                _initializedByBootstrap = false;
            }
        }

        public static void Acquire()
        {
            lock (Gate)
            {
                if (_clientCount == 0)
                {
                    _initializedByBootstrap = !YooAssets.IsInitialized;
                    if (_initializedByBootstrap)
                    {
                        YooAssets.Initialize();
                    }
                }

                _clientCount++;
            }
        }

        public static void Release()
        {
            lock (Gate)
            {
                if (_clientCount <= 0)
                {
                    throw new InvalidOperationException("YooAsset bootstrap runtime lease underflow.");
                }

                _clientCount--;
                if (_clientCount != 0)
                {
                    return;
                }

                if (_initializedByBootstrap &&
                    YooAssets.IsInitialized &&
                    YooAssets.GetPackages().Count == 0)
                {
                    YooAssets.Destroy();
                }

                _initializedByBootstrap = false;
            }
        }
    }

    public static class YooAssetBootstrapUrl
    {
        public static string BuildCdnUrl(
            BootstrapProfile profile,
            string packageName,
            string platformName,
            string fileName,
            BootstrapEndpoint endpoint)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (endpoint != BootstrapEndpoint.Primary &&
                endpoint != BootstrapEndpoint.Fallback)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endpoint),
                    endpoint,
                    "A CDN URL requires the primary or fallback endpoint.");
            }

            var root = endpoint == BootstrapEndpoint.Primary
                ? profile.PrimaryCdn
                : profile.FallbackCdn;
            if (root == null)
            {
                throw new InvalidOperationException("The selected CDN endpoint is not configured.");
            }

            var relative =
                EscapeSegment(profile.ApplicationId, nameof(profile.ApplicationId)) + "/" +
                EscapeSegment(profile.Channel, nameof(profile.Channel)) + "/" +
                EscapeSegment(profile.ApplicationVersion, nameof(profile.ApplicationVersion)) + "/" +
                EscapeSegment(platformName, nameof(platformName)) + "/" +
                EscapeSegment(packageName, nameof(packageName)) + "/" +
                EscapePath(fileName);
            return new Uri(root, relative).AbsoluteUri;
        }

        internal static string EscapePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("YooAsset remote file name must not be empty.", nameof(value));
            }

            var segments = value.Replace('\\', '/').Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = EscapeSegment(segments[i], nameof(value));
            }

            return string.Join("/", segments);
        }

        public static string EscapeRelativePath(string value)
        {
            return EscapePath(value);
        }

        internal static string EscapeSegment(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
            {
                throw new ArgumentException("URL path segment is invalid.", parameterName);
            }

            return Uri.EscapeDataString(value);
        }
    }

    public sealed class YooAssetBootstrapPackageHandle : IBootstrapPackageHandle
    {
        internal YooAssetBootstrapPackageHandle(
            ResourcePackage package,
            YooAssetRemoteService remoteService,
            bool ownsPackage,
            int downloadConcurrency)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            RemoteService = remoteService;
            OwnsPackage = ownsPackage;
            DownloadConcurrency = downloadConcurrency;
        }

        public ResourcePackage Package { get; }

        public string PackageName => Package.PackageName;

        public object NativePackage => Package;

        public string ActiveVersion { get; internal set; } = string.Empty;

        public bool IsManifestVerified { get; internal set; }

        public bool IsContentVerified { get; internal set; }

        public BootstrapEndpoint ActiveEndpoint { get; internal set; }

        internal YooAssetRemoteService RemoteService { get; }

        internal bool OwnsPackage { get; }

        internal int DownloadConcurrency { get; }
    }

    public sealed class YooAssetBootstrapBackend : IBootstrapBackend
    {
        private readonly Dictionary<string, YooAssetBootstrapPackageHandle> _packages =
            new Dictionary<string, YooAssetBootstrapPackageHandle>(StringComparer.Ordinal);
        private readonly object _pendingGate = new object();
        private readonly List<Task> _pendingOperations = new List<Task>();
        private readonly HashSet<AsyncOperationBase> _activeOperations =
            new HashSet<AsyncOperationBase>();

        private bool _runtimeLeaseAcquired;
        private bool _shutDown;
        private TimeSpan _maximumDrainTimeout = TimeSpan.FromSeconds(30);

        public async UniTask<BootstrapBackendResult<IBootstrapPackageHandle>> InitializePackageAsync(
            BootstrapPackageProfile package,
            BootstrapProfile profile,
            CancellationToken cancellationToken)
        {
            ThrowIfShutDown();
            cancellationToken.ThrowIfCancellationRequested();
            await DrainPendingOperationsAsync(cancellationToken);

            if (_packages.TryGetValue(package.PackageName, out var existing))
            {
                return BootstrapBackendResult<IBootstrapPackageHandle>.Succeeded(existing);
            }

            YooAssetBootstrapPackageHandle handle = null;
            try
            {
                if (!_runtimeLeaseAcquired)
                {
                    YooAssetRuntimeOwnership.Acquire();
                    _runtimeLeaseAcquired = true;
                }

                var ownsPackage = !YooAssets.TryGetPackage(package.PackageName, out var resourcePackage);
                if (!ownsPackage)
                {
                    return Failed<IBootstrapPackageHandle>(
                        BootstrapBackendErrorKind.Permanent,
                        "initialize",
                        "A package with the requested name already exists outside this backend.");
                }

                resourcePackage = YooAssets.CreatePackage(package.PackageName);
                var remoteService = profile.Mode == BootstrapMode.Host
                    ? new YooAssetRemoteService(profile, package.PackageName)
                    : null;
                handle = new YooAssetBootstrapPackageHandle(
                    resourcePackage,
                    remoteService,
                    ownsPackage,
                    profile.DownloadConcurrency);
                if (profile.OperationTimeout > _maximumDrainTimeout)
                {
                    _maximumDrainTimeout = profile.OperationTimeout;
                }

                _packages.Add(package.PackageName, handle);
                var options = CreateInitializeOptions(profile, package.PackageName, remoteService);
                options.BundleLoadingMaxConcurrency = profile.DownloadConcurrency;
                var operation = resourcePackage.InitializePackageAsync(options);
                await AwaitOperationAsync(operation, cancellationToken);
                if (operation.Status != EOperationStatus.Succeeded)
                {
                    return Failed<IBootstrapPackageHandle>(
                        Classify(operation.Error),
                        "initialize",
                        operation.Error);
                }

                CaptureActiveManifest(handle);
                return BootstrapBackendResult<IBootstrapPackageHandle>.Succeeded(handle);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (handle != null && !_packages.ContainsKey(handle.PackageName))
                {
                    _packages.Add(handle.PackageName, handle);
                }

                return Failed<IBootstrapPackageHandle>(
                    BootstrapBackendErrorKind.Permanent,
                    "initialize",
                    exception.Message,
                    exception);
            }
        }

        public async UniTask<BootstrapBackendResult<BootstrapVersion>> RequestVersionAsync(
            IBootstrapPackageHandle package,
            BootstrapProfile profile,
            BootstrapEndpoint endpoint,
            CancellationToken cancellationToken)
        {
            var handle = GetHandle(package);
            await DrainPendingOperationsAsync(cancellationToken);
            SetEndpoint(handle, endpoint);

            try
            {
                var operation = handle.Package.RequestPackageVersionAsync(
                    new RequestPackageVersionOptions(true, TimeoutSeconds(profile)));
                await AwaitOperationAsync(operation, cancellationToken);
                if (operation.Status != EOperationStatus.Succeeded)
                {
                    return Failed<BootstrapVersion>(
                        Classify(operation.Error),
                        "request-version",
                        operation.Error);
                }

                if (string.IsNullOrWhiteSpace(operation.PackageVersion))
                {
                    return Failed<BootstrapVersion>(
                        BootstrapBackendErrorKind.Permanent,
                        "request-version",
                        "YooAsset returned an empty package version.");
                }

                return BootstrapBackendResult<BootstrapVersion>.Succeeded(
                    new BootstrapVersion(operation.PackageVersion, endpoint, false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed<BootstrapVersion>(
                    BootstrapBackendErrorKind.Permanent,
                    "request-version",
                    exception.Message,
                    exception);
            }
        }

        public async UniTask<BootstrapBackendResult<bool>> ActivateManifestAsync(
            IBootstrapPackageHandle package,
            BootstrapVersion version,
            BootstrapProfile profile,
            BootstrapEndpoint endpoint,
            CancellationToken cancellationToken)
        {
            var handle = GetHandle(package);
            await DrainPendingOperationsAsync(cancellationToken);
            SetEndpoint(handle, endpoint);

            try
            {
                var operation = handle.Package.LoadPackageManifestAsync(
                    new LoadPackageManifestOptions(version.Value, TimeoutSeconds(profile)));
                await AwaitOperationAsync(operation, cancellationToken);
                if (operation.Status != EOperationStatus.Succeeded)
                {
                    return Failed<bool>(
                        Classify(operation.Error),
                        "activate-manifest",
                        operation.Error);
                }

                handle.ActiveVersion = version.Value;
                handle.ActiveEndpoint = endpoint;
                handle.IsManifestVerified = true;
                handle.IsContentVerified =
                    endpoint == BootstrapEndpoint.VerifiedLocal && version.IsVerified;
                return BootstrapBackendResult<bool>.Succeeded(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed<bool>(
                    BootstrapBackendErrorKind.Permanent,
                    "activate-manifest",
                    exception.Message,
                    exception);
            }
        }

        public async UniTask<BootstrapBackendResult<BootstrapDownloadPlan>> CalculateDownloadAsync(
            IBootstrapPackageHandle package,
            BootstrapProfile profile,
            CancellationToken cancellationToken)
        {
            var handle = GetHandle(package);
            cancellationToken.ThrowIfCancellationRequested();
            await DrainPendingOperationsAsync(cancellationToken);
            if (!handle.IsManifestVerified)
            {
                return Failed<BootstrapDownloadPlan>(
                    BootstrapBackendErrorKind.Integrity,
                    "calculate-download",
                    "No verified active manifest is available.");
            }

            try
            {
                var isFallback =
                    handle.ActiveEndpoint == BootstrapEndpoint.VerifiedLocal ||
                    handle.ActiveEndpoint == BootstrapEndpoint.VerifiedBuiltin;
                SetEndpoint(
                    handle,
                    isFallback ? handle.ActiveEndpoint : BootstrapEndpoint.None);
                var downloader = handle.Package.CreateResourceDownloader(
                    new ResourceDownloaderOptions(profile.DownloadConcurrency, 0));
                var plan = new BootstrapDownloadPlan(
                    handle,
                    downloader.TotalDownloadCount,
                    downloader.TotalDownloadBytes,
                    handle);
                if (isFallback && plan.HasDownload)
                {
                    return Failed<BootstrapDownloadPlan>(
                        BootstrapBackendErrorKind.Integrity,
                        "calculate-download",
                        "Verified fallback manifest requires unavailable remote content.");
                }

                if (!plan.HasDownload)
                {
                    handle.IsContentVerified = true;
                }

                return BootstrapBackendResult<BootstrapDownloadPlan>.Succeeded(plan);
            }
            catch (Exception exception)
            {
                return Failed<BootstrapDownloadPlan>(
                    BootstrapBackendErrorKind.Permanent,
                    "calculate-download",
                    exception.Message,
                    exception);
            }
        }

        public async UniTask<BootstrapBackendResult<bool>> DownloadAsync(
            BootstrapDownloadPlan plan,
            Action<BootstrapDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var handle = GetHandle(plan.Package);
            cancellationToken.ThrowIfCancellationRequested();
            await DrainPendingOperationsAsync(cancellationToken);

            ResourceDownloaderOperation downloader;
            try
            {
                SetEndpoint(handle, BootstrapEndpoint.None);
                downloader = handle.Package.CreateResourceDownloader(
                    new ResourceDownloaderOptions(handle.DownloadConcurrency, 0));
            }
            catch (Exception exception)
            {
                return Failed<bool>(
                    BootstrapBackendErrorKind.Permanent,
                    "download",
                    exception.Message,
                    exception);
            }

            lock (_pendingGate)
            {
                _activeOperations.Add(downloader);
            }

            void OnProgress(DownloadProgressChangedEventArgs value)
            {
                progress?.Invoke(new BootstrapDownloadProgress(
                    value.CurrentDownloadCount,
                    value.TotalDownloadCount,
                    value.CurrentDownloadBytes,
                    value.TotalDownloadBytes));
            }

            string downloadError = null;
            void OnError(DownloadErrorEventArgs value)
            {
                downloadError = value.ErrorInfo;
            }

            downloader.DownloadProgressChanged += OnProgress;
            downloader.DownloadError += OnError;
            var started = false;
            try
            {
                downloader.StartDownload();
                started = true;
                var cancelRequested = false;
                while (!downloader.IsDone)
                {
                    if (!cancelRequested && cancellationToken.IsCancellationRequested)
                    {
                        cancelRequested = true;
                        downloader.CancelDownload();
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed<bool>(
                    BootstrapBackendErrorKind.Permanent,
                    "download",
                    exception.Message,
                    exception);
            }
            finally
            {
                downloader.DownloadProgressChanged -= OnProgress;
                downloader.DownloadError -= OnError;
                if (!started || downloader.IsDone)
                {
                    lock (_pendingGate)
                    {
                        _activeOperations.Remove(downloader);
                    }
                }
                else
                {
                    TrackPendingOperation(downloader);
                }
            }

            if (downloader.Status != EOperationStatus.Succeeded)
            {
                handle.IsContentVerified = false;
                var failure = string.IsNullOrWhiteSpace(downloadError)
                    ? downloader.Error
                    : downloadError;
                return Failed<bool>(
                    Classify(failure),
                    "download",
                    failure);
            }

            progress?.Invoke(new BootstrapDownloadProgress(
                downloader.TotalDownloadCount,
                downloader.TotalDownloadCount,
                downloader.TotalDownloadBytes,
                downloader.TotalDownloadBytes));
            return BootstrapBackendResult<bool>.Succeeded(true);
        }

        public async UniTask<BootstrapBackendResult<bool>> VerifyAsync(
            BootstrapDownloadPlan plan,
            CancellationToken cancellationToken)
        {
            var handle = GetHandle(plan.Package);
            cancellationToken.ThrowIfCancellationRequested();
            await DrainPendingOperationsAsync(cancellationToken);

            // DownloaderOperation reports success only after YooAsset validates every file.
            handle.IsContentVerified = handle.IsManifestVerified;
            return handle.IsContentVerified
                ? BootstrapBackendResult<bool>.Succeeded(true)
                : Failed<bool>(
                    BootstrapBackendErrorKind.Integrity,
                    "verify",
                    "Downloaded content has no verified active manifest.");
        }

        public async UniTask<BootstrapBackendResult<BootstrapVersion>> FindVerifiedFallbackAsync(
            IBootstrapPackageHandle package,
            BootstrapProfile profile,
            CancellationToken cancellationToken)
        {
            var handle = GetHandle(package);
            await DrainPendingOperationsAsync(cancellationToken);
            if (handle.IsManifestVerified &&
                handle.IsContentVerified &&
                handle.Package.PackageValid &&
                !string.IsNullOrWhiteSpace(handle.ActiveVersion))
            {
                return BootstrapBackendResult<BootstrapVersion>.Succeeded(
                    new BootstrapVersion(
                        handle.ActiveVersion,
                        BootstrapEndpoint.VerifiedLocal,
                        true));
            }

            if (profile.Mode != BootstrapMode.Host || handle.RemoteService == null)
            {
                return Failed<BootstrapVersion>(
                    BootstrapBackendErrorKind.Permanent,
                    "find-fallback",
                    "No verified local or built-in manifest is available.");
            }

            try
            {
                SetEndpoint(handle, BootstrapEndpoint.VerifiedBuiltin);
                var versionOperation = handle.Package.RequestPackageVersionAsync(
                    new RequestPackageVersionOptions(false, TimeoutSeconds(profile)));
                await AwaitOperationAsync(versionOperation, cancellationToken);
                if (versionOperation.Status != EOperationStatus.Succeeded ||
                    string.IsNullOrWhiteSpace(versionOperation.PackageVersion))
                {
                    return Failed<BootstrapVersion>(
                        Classify(versionOperation.Error),
                        "find-fallback-version",
                        versionOperation.Error);
                }

                var prefetch = handle.Package.PrefetchManifestAsync(
                    new PrefetchManifestOptions(
                        versionOperation.PackageVersion,
                        TimeoutSeconds(profile)));
                await AwaitOperationAsync(prefetch, cancellationToken);
                if (prefetch.Status != EOperationStatus.Succeeded)
                {
                    return Failed<BootstrapVersion>(
                        Classify(prefetch.Error),
                        "find-fallback-manifest",
                        prefetch.Error);
                }

                return BootstrapBackendResult<BootstrapVersion>.Succeeded(
                    new BootstrapVersion(
                        versionOperation.PackageVersion,
                        BootstrapEndpoint.VerifiedBuiltin,
                        true));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed<BootstrapVersion>(
                    BootstrapBackendErrorKind.Permanent,
                    "find-fallback",
                    exception.Message,
                    exception);
            }
        }

        public UniTask ResetAsync(CancellationToken cancellationToken)
        {
            return DestroyOwnedPackagesAsync(cancellationToken, false);
        }

        public UniTask ShutdownAsync(CancellationToken cancellationToken)
        {
            return DestroyOwnedPackagesAsync(cancellationToken, true);
        }

        private async UniTask DestroyOwnedPackagesAsync(
            CancellationToken cancellationToken,
            bool destroyYooAssets)
        {
            if (_shutDown)
            {
                return;
            }

            await DrainPendingOperationsAsync(cancellationToken);
            var snapshot = new List<YooAssetBootstrapPackageHandle>(_packages.Values);
            var failures = new List<Exception>();
            foreach (var handle in snapshot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!handle.OwnsPackage)
                {
                    continue;
                }

                try
                {
                    var operation = handle.Package.DestroyPackageAsync();
                    await AwaitOperationAsync(operation, cancellationToken);
                    if (operation.Status != EOperationStatus.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"YooAsset failed to destroy package \"{handle.PackageName}\": {operation.Error}");
                    }

                    YooAssets.RemovePackage(handle.PackageName);
                    _packages.Remove(handle.PackageName);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add(
                        new InvalidOperationException(
                            $"Failed to destroy YooAsset package \"{handle.PackageName}\".",
                            exception));
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more YooAsset packages failed to shut down.",
                    failures);
            }

            if (destroyYooAssets)
            {
                if (_runtimeLeaseAcquired)
                {
                    YooAssetRuntimeOwnership.Release();
                    _runtimeLeaseAcquired = false;
                }

                _shutDown = true;
            }
        }

        private static InitializePackageOptions CreateInitializeOptions(
            BootstrapProfile profile,
            string packageName,
            YooAssetRemoteService remoteService)
        {
            switch (profile.Mode)
            {
                case BootstrapMode.EditorSimulate:
#if UNITY_EDITOR
                    var buildResult = EditorSimulateBuildInvoker.Build(
                        packageName,
                        (int)EBundleType.VirtualAssetBundle);
                    return new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters =
                            FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                                buildResult.PackageRootDirectory),
                    };
#else
                    throw new PlatformNotSupportedException(
                        "EditorSimulate mode is only available in the Unity Editor.");
#endif
                case BootstrapMode.Offline:
                    return new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters = CreateVerifiedBuiltinFileSystem(),
                    };
                case BootstrapMode.Host:
                    return new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters = CreateVerifiedBuiltinFileSystem(),
                        CacheFileSystemParameters = CreateVerifiedSandboxFileSystem(
                            remoteService ??
                            throw new InvalidOperationException(
                                "Host mode requires a remote service.")),
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(profile.Mode));
            }
        }

        private static FileSystemParameters CreateVerifiedBuiltinFileSystem()
        {
            var parameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            parameters.AddParameter(
                EFileSystemParameter.FileVerifyLevel,
                EFileVerifyLevel.High);
            return parameters;
        }

        private static FileSystemParameters CreateVerifiedSandboxFileSystem(
            IRemoteService remoteService)
        {
            var parameters =
                FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            parameters.AddParameter(
                EFileSystemParameter.FileVerifyLevel,
                EFileVerifyLevel.High);
            return parameters;
        }

        private static void CaptureActiveManifest(YooAssetBootstrapPackageHandle handle)
        {
            if (!handle.Package.PackageValid)
            {
                return;
            }

            handle.ActiveVersion = handle.Package.GetPackageVersion();
            handle.ActiveEndpoint = BootstrapEndpoint.VerifiedLocal;
            handle.IsManifestVerified = true;
            handle.IsContentVerified = false;
        }

        private async UniTask AwaitOperationAsync(
            AsyncOperationBase operation,
            CancellationToken cancellationToken)
        {
            lock (_pendingGate)
            {
                _activeOperations.Add(operation);
            }

            try
            {
                while (!operation.IsDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TrackPendingOperation(operation);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                if (operation.IsDone)
                {
                    lock (_pendingGate)
                    {
                        _activeOperations.Remove(operation);
                    }
                }
            }
        }

        private void TrackPendingOperation(AsyncOperationBase operation)
        {
            var task = DrainOperationAsync(operation).AsTask();
            lock (_pendingGate)
            {
                _pendingOperations.Add(task);
            }

            task.ContinueWith(
                completed =>
                {
                    var ignored = completed.Exception;
                    lock (_pendingGate)
                    {
                        _pendingOperations.Remove(completed);
                        _activeOperations.Remove(operation);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static async UniTask DrainOperationAsync(AsyncOperationBase operation)
        {
            while (!operation.IsDone)
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        private async UniTask DrainPendingOperationsAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task[] snapshot;
                AsyncOperationBase[] active;
                lock (_pendingGate)
                {
                    snapshot = _pendingOperations.ToArray();
                    active = new AsyncOperationBase[_activeOperations.Count];
                    _activeOperations.CopyTo(active);
                }

                if (snapshot.Length == 0 && active.Length == 0)
                {
                    return;
                }

                var waits = new List<Task>(snapshot);
                foreach (var operation in active)
                {
                    if (!operation.IsDone)
                    {
                        waits.Add(DrainOperationAsync(operation).AsTask());
                    }
                }

                if (waits.Count > 0)
                {
                    var all = Task.WhenAll(waits);
                    using (var timeoutCancellation =
                           CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        var timeout = UniTask.Delay(
                                _maximumDrainTimeout,
                                true,
                                PlayerLoopTiming.Update,
                                timeoutCancellation.Token)
                            .AsTask();
                        var completed = await Task.WhenAny(all, timeout);
                        if (completed != all)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            throw new TimeoutException(
                                "Timed out while draining canceled YooAsset operations.");
                        }

                        timeoutCancellation.Cancel();
                        await all;
                    }
                }

                lock (_pendingGate)
                {
                    _activeOperations.RemoveWhere(operation => operation.IsDone);
                }
            }
        }

        private static int TimeoutSeconds(BootstrapProfile profile)
        {
            return Math.Max(1, (int)Math.Ceiling(profile.OperationTimeout.TotalSeconds));
        }

        private static void SetEndpoint(
            YooAssetBootstrapPackageHandle handle,
            BootstrapEndpoint endpoint)
        {
            handle.RemoteService?.Select(endpoint);
        }

        private YooAssetBootstrapPackageHandle GetHandle(IBootstrapPackageHandle package)
        {
            ThrowIfShutDown();
            if (!(package is YooAssetBootstrapPackageHandle handle) ||
                !_packages.TryGetValue(handle.PackageName, out var registered) ||
                !ReferenceEquals(handle, registered))
            {
                throw new ArgumentException(
                    "Package handle does not belong to this YooAsset bootstrap backend.",
                    nameof(package));
            }

            return handle;
        }

        private void ThrowIfShutDown()
        {
            if (_shutDown)
            {
                throw new ObjectDisposedException(nameof(YooAssetBootstrapBackend));
            }
        }

        private static BootstrapBackendResult<T> Failed<T>(
            BootstrapBackendErrorKind kind,
            string operation,
            string reason,
            Exception exception = null)
        {
            return BootstrapBackendResult<T>.Failed(
                new BootstrapBackendFailure(
                    kind,
                    operation,
                    string.IsNullOrWhiteSpace(reason) ? "YooAsset operation failed." : reason,
                    exception?.GetType().Name));
        }

        private static BootstrapBackendErrorKind Classify(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return BootstrapBackendErrorKind.Permanent;
            }

            var value = error.ToLowerInvariant();
            if (value.Contains("cancel"))
            {
                return BootstrapBackendErrorKind.Canceled;
            }

            if (value.Contains("timeout") || value.Contains("timed out"))
            {
                return BootstrapBackendErrorKind.Timeout;
            }

            if (value.Contains("hash") ||
                value.Contains("crc") ||
                value.Contains("corrupt") ||
                value.Contains("validation"))
            {
                return BootstrapBackendErrorKind.Integrity;
            }

            if (value.Contains("network") ||
                value.Contains("http") ||
                value.Contains("connect") ||
                value.Contains("resolve") ||
                value.Contains("unreachable"))
            {
                return BootstrapBackendErrorKind.TransientNetwork;
            }

            return BootstrapBackendErrorKind.Permanent;
        }
    }

    internal sealed class YooAssetRemoteService : IRemoteService
    {
        private readonly BootstrapProfile _profile;
        private readonly string _packageName;
        private BootstrapEndpoint _endpoint;

        public YooAssetRemoteService(BootstrapProfile profile, string packageName)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _packageName = packageName ?? throw new ArgumentNullException(nameof(packageName));
        }

        public void Select(BootstrapEndpoint endpoint)
        {
            _endpoint = endpoint;
        }

        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            if (_endpoint == BootstrapEndpoint.VerifiedBuiltin)
            {
                return new[] { BuildBuiltinUrl(fileName) };
            }

            if (_endpoint == BootstrapEndpoint.Fallback)
            {
                return new[]
                {
                    YooAssetBootstrapUrl.BuildCdnUrl(
                        _profile,
                        _packageName,
                        UnityBootstrapRuntimeEnvironment.Instance.PlatformName,
                        fileName,
                        BootstrapEndpoint.Fallback),
                };
            }

            if (_endpoint == BootstrapEndpoint.Primary)
            {
                return new[]
                {
                    YooAssetBootstrapUrl.BuildCdnUrl(
                        _profile,
                        _packageName,
                        UnityBootstrapRuntimeEnvironment.Instance.PlatformName,
                        fileName,
                        BootstrapEndpoint.Primary),
                };
            }

            if (_profile.FallbackCdn == null || _profile.FallbackCdn == _profile.PrimaryCdn)
            {
                return new[]
                {
                    YooAssetBootstrapUrl.BuildCdnUrl(
                        _profile,
                        _packageName,
                        UnityBootstrapRuntimeEnvironment.Instance.PlatformName,
                        fileName,
                        BootstrapEndpoint.Primary),
                };
            }

            return new[]
            {
                YooAssetBootstrapUrl.BuildCdnUrl(
                    _profile,
                    _packageName,
                    UnityBootstrapRuntimeEnvironment.Instance.PlatformName,
                    fileName,
                    BootstrapEndpoint.Primary),
                YooAssetBootstrapUrl.BuildCdnUrl(
                    _profile,
                    _packageName,
                    UnityBootstrapRuntimeEnvironment.Instance.PlatformName,
                    fileName,
                    BootstrapEndpoint.Fallback),
            };
        }

        private string BuildBuiltinUrl(string fileName)
        {
            var root = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/');
            var url =
                root + "/yoo/" +
                YooAssetBootstrapUrl.EscapeSegment(_packageName, nameof(_packageName)) + "/" +
                YooAssetBootstrapUrl.EscapePath(fileName);
            return url.Contains("://") ? url : "file://" + url;
        }
    }
}
