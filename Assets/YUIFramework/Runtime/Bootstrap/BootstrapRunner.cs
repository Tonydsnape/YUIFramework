using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;

namespace YUIFramework.Bootstrap
{
    public sealed class BootstrapRunner
    {
        private sealed class PackageRun
        {
            public BootstrapPackageProfile Profile;
            public IBootstrapPackageHandle Handle;
            public BootstrapVersion Version;
            public BootstrapDownloadPlan Plan;
        }

        private static readonly Regex SensitiveUri = new Regex(
            @"(?:https?|file)://[^\s\)\]\}]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly object _gate = new object();
        private readonly IBootstrapBackend _backend;
        private readonly IBootstrapTelemetrySink _telemetry;
        private readonly IBootstrapProgressSink _progress;
        private readonly IBootstrapCodeLoader _codeLoader;
        private readonly IBootstrapGameEntry _gameEntry;
        private readonly IBootstrapClock _clock;
        private readonly IBootstrapDelay _delay;
        private readonly IBootstrapDiskSpace _disk;
        private readonly IBootstrapConfirmation _confirmation;
        private readonly IBootstrapNetworkMonitor _network;
        private readonly IBootstrapRuntimeEnvironment _environment;
        private readonly List<BootstrapSinkDiagnostic> _sinkDiagnostics =
            new List<BootstrapSinkDiagnostic>();

        private BootstrapState _state = BootstrapState.Idle;
        private BootstrapProfile _activeProfile;
        private Guid _currentRunId;
        private CancellationTokenSource _runCancellation;
        private Task<BootstrapRunResult> _inflight;
        private Task _resetTask;
        private Task _shutdownTask;
        private bool _resetting;
        private bool _shuttingDown;
        private bool _shutDown;
        private bool _runDegraded;
        private long _downloadProgressGeneration;
        private BootstrapRunResult _lastResult;

        public BootstrapRunner(
            IBootstrapBackend backend,
            IBootstrapGameEntry gameEntry,
            IBootstrapTelemetrySink telemetry = null,
            IBootstrapProgressSink progress = null,
            IBootstrapCodeLoader codeLoader = null,
            IBootstrapClock clock = null,
            IBootstrapDelay delay = null,
            IBootstrapDiskSpace disk = null,
            IBootstrapConfirmation confirmation = null,
            IBootstrapNetworkMonitor network = null,
            IBootstrapRuntimeEnvironment environment = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _gameEntry = gameEntry ?? throw new ArgumentNullException(nameof(gameEntry));
            _telemetry = telemetry ?? NoopBootstrapTelemetrySink.Instance;
            _progress = progress ?? NoopBootstrapProgressSink.Instance;
            _codeLoader = codeLoader ?? NoopBootstrapCodeLoader.Instance;
            _clock = clock ?? SystemBootstrapClock.Instance;
            _delay = delay ?? UniTaskBootstrapDelay.Instance;
            _disk = disk ?? new UnityBootstrapDiskSpace();
            _confirmation = confirmation ?? AcceptBootstrapConfirmation.Instance;
            _network = network ?? UnityBootstrapNetworkMonitor.Instance;
            _environment = environment ?? UnityBootstrapRuntimeEnvironment.Instance;
        }

        public BootstrapState State
        {
            get
            {
                lock (_gate)
                {
                    return _state;
                }
            }
        }

        public Guid CurrentRunId
        {
            get
            {
                lock (_gate)
                {
                    return _currentRunId;
                }
            }
        }

        public BootstrapRunResult LastResult
        {
            get
            {
                lock (_gate)
                {
                    return _lastResult;
                }
            }
        }

        public IReadOnlyList<BootstrapSinkDiagnostic> SinkDiagnostics
        {
            get
            {
                lock (_gate)
                {
                    return new ReadOnlyCollection<BootstrapSinkDiagnostic>(
                        _sinkDiagnostics.ToArray());
                }
            }
        }

        public UniTask<BootstrapRunResult> RunAsync(
            BootstrapProfile profile,
            CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            cancellationToken.ThrowIfCancellationRequested();

            Task<BootstrapRunResult> task;
            lock (_gate)
            {
                if (_shuttingDown || _shutDown)
                {
                    return UniTask.FromResult(CreateRejectedResult(
                        profile,
                        BootstrapErrorCode.RunnerShutDown,
                        "runner-shutdown"));
                }

                if (_resetting)
                {
                    return UniTask.FromResult(CreateRejectedResult(
                        profile,
                        BootstrapErrorCode.ResetInProgress,
                        "reset-in-progress"));
                }

                if (_inflight != null && !_inflight.IsCompleted)
                {
                    if (!_activeProfile.Equals(profile))
                    {
                        return UniTask.FromResult(CreateRejectedResult(
                            profile,
                            BootstrapErrorCode.AlreadyRunningDifferentProfile,
                            "different-profile-active"));
                    }

                    task = _inflight;
                }
                else
                {
                    var resetBeforeRun = _state != BootstrapState.Idle;
                    _activeProfile = profile;
                    _currentRunId = Guid.NewGuid();
                    _lastResult = null;
                    _runDegraded = false;
                    _downloadProgressGeneration++;
                    _runCancellation?.Dispose();
                    _runCancellation = new CancellationTokenSource();
                    _inflight = RunCoreAsync(
                            profile,
                            _currentRunId,
                            resetBeforeRun,
                            _runCancellation.Token)
                        .AsTask();
                    task = _inflight;
                }
            }

            return AwaitCallerAsync(task, cancellationToken);
        }

        public UniTask ResetAsync(CancellationToken cancellationToken = default)
        {
            Task resetTask;
            lock (_gate)
            {
                if (_shuttingDown)
                {
                    return AwaitLifecycleCallerAsync(_shutdownTask, cancellationToken);
                }

                if (_shutDown)
                {
                    return UniTask.CompletedTask;
                }

                if (_resetTask == null || _resetTask.IsCompleted)
                {
                    _resetting = true;
                    _resetTask = ResetCoreAsync().AsTask();
                }

                resetTask = _resetTask;
            }

            return AwaitLifecycleCallerAsync(resetTask, cancellationToken);
        }

        public UniTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            Task shutdownTask;
            lock (_gate)
            {
                if (_shutdownTask == null ||
                    (!_shutDown && _shutdownTask.IsCompleted && _shutdownTask.IsFaulted))
                {
                    _shuttingDown = true;
                    _shutdownTask = ShutdownCoreAsync().AsTask();
                }

                shutdownTask = _shutdownTask;
            }

            return AwaitLifecycleCallerAsync(shutdownTask, cancellationToken);
        }

        private async UniTask<BootstrapRunResult> RunCoreAsync(
            BootstrapProfile profile,
            Guid runId,
            bool resetBeforeRun,
            CancellationToken cancellationToken)
        {
            var startedAt = _clock.UtcNow;
            var degraded = false;
            var usedFallback = false;
            var packageRuns = new List<PackageRun>(profile.Packages.Count);
            var packagesUsingFallback = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                if (resetBeforeRun)
                {
                    Transition(runId, BootstrapState.Resetting);
                    await _backend.ResetAsync(cancellationToken);
                    Transition(runId, BootstrapState.Idle);
                }

                EmitTelemetry(new BootstrapTelemetryEvent(
                    runId,
                    BootstrapTelemetryKind.RunStarted,
                    BootstrapState.Idle,
                    _clock.UtcNow));

                Transition(runId, BootstrapState.InitializingPackage);
                if (profile.Mode == BootstrapMode.EditorSimulate && !_environment.IsEditor)
                {
                    return Fail(
                        runId,
                        BootstrapErrorCode.EditorSimulateUnavailable,
                        "initialize",
                        null,
                        startedAt,
                        degraded,
                        usedFallback);
                }

                foreach (var packageProfile in profile.Packages)
                {
                    var initialized = await InvokeWithTimeoutAsync(
                        token => _backend.InitializePackageAsync(packageProfile, profile, token),
                        profile,
                        "initialize",
                        cancellationToken);
                    if (!initialized.IsSuccess || initialized.Value == null)
                    {
                        return FailFromBackend(
                            runId,
                            BootstrapErrorCode.InitializationFailed,
                            packageProfile.PackageName,
                            initialized.Failure,
                            startedAt,
                            degraded,
                            usedFallback);
                    }

                    packageRuns.Add(new PackageRun
                    {
                        Profile = packageProfile,
                        Handle = initialized.Value,
                    });
                }

                Transition(runId, BootstrapState.RequestingVersion);
                foreach (var package in packageRuns)
                {
                    BootstrapBackendResult<BootstrapVersion> version;
                    if (profile.Mode == BootstrapMode.Host && _network.IsNetworkReachable)
                    {
                        version = await RequestRemoteVersionAsync(package, profile, runId, cancellationToken);
                    }
                    else if (profile.Mode == BootstrapMode.Host)
                    {
                        version = BootstrapBackendResult<BootstrapVersion>.Failed(
                            new BootstrapBackendFailure(
                                BootstrapBackendErrorKind.TransientNetwork,
                                "request-version",
                                "network-unavailable"));
                    }
                    else
                    {
                        version = await InvokeWithTimeoutAsync(
                            token => _backend.RequestVersionAsync(
                                package.Handle,
                                profile,
                                BootstrapEndpoint.None,
                                token),
                            profile,
                            "request-version",
                            cancellationToken);
                    }

                    if (!version.IsSuccess || version.Value == null)
                    {
                        var fallback = await FindVerifiedFallbackAsync(
                            package,
                            profile,
                            runId,
                            cancellationToken);
                        if (!fallback.IsSuccess)
                        {
                            var code = profile.Mode == BootstrapMode.Host && !_network.IsNetworkReachable
                                ? BootstrapErrorCode.NetworkUnavailable
                                : MapError(BootstrapErrorCode.VersionRequestFailed, version.Failure);
                            return FailFromBackend(
                                runId,
                                code,
                                package.Profile.PackageName,
                                version.Failure ?? fallback.Failure,
                                startedAt,
                                degraded,
                                usedFallback);
                        }

                        package.Version = fallback.Value;
                        packagesUsingFallback.Add(package.Profile.PackageName);
                        degraded = true;
                        usedFallback = true;
                        MarkRunDegraded();
                    }
                    else
                    {
                        package.Version = version.Value;
                    }
                }

                Transition(runId, BootstrapState.ActivatingManifest);
                foreach (var package in packageRuns)
                {
                    var activated = await ActivateVersionAsync(
                        package,
                        profile,
                        runId,
                        cancellationToken);
                    if (!activated.IsSuccess || !activated.Value)
                    {
                        if (!packagesUsingFallback.Contains(package.Profile.PackageName))
                        {
                            var fallback = await FindVerifiedFallbackAsync(
                                package,
                                profile,
                                runId,
                                cancellationToken);
                            if (fallback.IsSuccess)
                            {
                                package.Version = fallback.Value;
                                packagesUsingFallback.Add(package.Profile.PackageName);
                                degraded = true;
                                usedFallback = true;
                                MarkRunDegraded();
                                activated = await ActivateVersionAsync(
                                    package,
                                    profile,
                                    runId,
                                    cancellationToken);
                            }
                        }

                        if (!activated.IsSuccess || !activated.Value)
                        {
                            return FailFromBackend(
                                runId,
                                BootstrapErrorCode.ManifestActivationFailed,
                                package.Profile.PackageName,
                                activated.Failure,
                                startedAt,
                                degraded,
                                usedFallback);
                        }
                    }
                }

                while (true)
                {
                    Transition(runId, BootstrapState.CalculatingDownload);
                    long allBytes = 0;
                    var allFiles = 0;
                    var hasDownload = false;
                    foreach (var package in packageRuns)
                    {
                        var plan = await InvokeWithTimeoutAsync(
                            token => _backend.CalculateDownloadAsync(package.Handle, profile, token),
                            profile,
                            "calculate-download",
                            cancellationToken);
                        if (!plan.IsSuccess || plan.Value == null)
                        {
                            return FailFromBackend(
                                runId,
                                BootstrapErrorCode.BackendFailure,
                                package.Profile.PackageName,
                                plan.Failure,
                                startedAt,
                                degraded,
                                usedFallback);
                        }

                        package.Plan = plan.Value;
                        hasDownload |= plan.Value.HasDownload;
                        allBytes = CheckedAdd(allBytes, plan.Value.TotalBytes);
                        allFiles = CheckedAdd(allFiles, plan.Value.FileCount);
                    }

                    if (!hasDownload)
                    {
                        break;
                    }

                    if (profile.RequireDownloadConfirmation)
                    {
                        Transition(runId, BootstrapState.AwaitingConfirmation);
                        EmitProgress(new BootstrapProgress(
                            runId,
                            BootstrapState.AwaitingConfirmation,
                            StateProgress(BootstrapState.AwaitingConfirmation),
                            totalFiles: allFiles,
                            totalBytes: allBytes,
                            degraded: degraded));
                        foreach (var package in packageRuns)
                        {
                            if (package.Plan == null || !package.Plan.HasDownload)
                            {
                                continue;
                            }

                            bool accepted;
                            try
                            {
                                accepted = await _confirmation.ConfirmAsync(
                                    package.Plan,
                                    cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                return Fail(
                                    runId,
                                    BootstrapErrorCode.ConfirmationDeclined,
                                    "confirm-download",
                                    package.Profile.PackageName,
                                    startedAt,
                                    degraded,
                                    usedFallback,
                                    exception);
                            }

                            if (!accepted)
                            {
                                return Fail(
                                    runId,
                                    BootstrapErrorCode.ConfirmationDeclined,
                                    "confirm-download",
                                    package.Profile.PackageName,
                                    startedAt,
                                    degraded,
                                    usedFallback);
                            }
                        }
                    }

                    Transition(runId, BootstrapState.CheckingDisk);
                    EmitProgress(new BootstrapProgress(
                        runId,
                        BootstrapState.CheckingDisk,
                        StateProgress(BootstrapState.CheckingDisk),
                        totalFiles: allFiles,
                        totalBytes: allBytes,
                        degraded: degraded));
                    var reservedByScope = new Dictionary<string, long>(StringComparer.Ordinal);
                    foreach (var package in packageRuns)
                    {
                        if (package.Plan == null || !package.Plan.HasDownload)
                        {
                            continue;
                        }

                        long available;
                        string storageScope;
                        try
                        {
                            storageScope = _disk.GetStorageScope(package.Profile.PackageName);
                            if (string.IsNullOrWhiteSpace(storageScope))
                            {
                                throw new InvalidOperationException(
                                    "Disk service returned an empty storage scope.");
                            }

                            available = _disk.GetAvailableBytes(package.Profile.PackageName);
                        }
                        catch (Exception exception)
                        {
                            return Fail(
                                runId,
                                BootstrapErrorCode.InsufficientDiskSpace,
                                "check-disk",
                                package.Profile.PackageName,
                                startedAt,
                                degraded,
                                usedFallback,
                                exception);
                        }

                        reservedByScope.TryGetValue(storageScope, out var reserved);
                        var required = CheckedAdd(
                            CheckedAdd(reserved, package.Plan.TotalBytes),
                            profile.DiskSafetyMarginBytes);
                        if (available < required)
                        {
                            return Fail(
                                runId,
                                BootstrapErrorCode.InsufficientDiskSpace,
                                "check-disk",
                                package.Profile.PackageName,
                                startedAt,
                                degraded,
                                usedFallback);
                        }

                        reservedByScope[storageScope] = CheckedAdd(
                            reserved,
                            package.Plan.TotalBytes);
                    }

                    Transition(runId, BootstrapState.Downloading);
                    PackageRun failedPackage = null;
                    BootstrapBackendFailure downloadFailure = null;
                    long completedPackageBytes = 0;
                    var completedPackageFiles = 0;
                    foreach (var package in packageRuns)
                    {
                        if (package.Plan == null || !package.Plan.HasDownload)
                        {
                            continue;
                        }

                        var capturedPackage = package;
                        var capturedBaseBytes = completedPackageBytes;
                        var capturedBaseFiles = completedPackageFiles;
                        var downloaded = await ExecuteWithRetryAsync(
                            token => DownloadAttemptAsync(
                                runId,
                                capturedPackage,
                                capturedBaseBytes,
                                capturedBaseFiles,
                                allBytes,
                                allFiles,
                                degraded,
                                token),
                            profile,
                            runId,
                            package.Profile.PackageName,
                            "download",
                            BootstrapEndpoint.None,
                            cancellationToken);
                        if (!downloaded.IsSuccess || !downloaded.Value)
                        {
                            failedPackage = package;
                            downloadFailure = downloaded.Failure;
                            break;
                        }

                        completedPackageBytes = CheckedAdd(completedPackageBytes, package.Plan.TotalBytes);
                        completedPackageFiles = CheckedAdd(completedPackageFiles, package.Plan.FileCount);
                    }

                    if (failedPackage != null)
                    {
                        if (await RecoverWithFallbackAsync(
                                failedPackage,
                                packageRuns,
                                packagesUsingFallback,
                                profile,
                                runId,
                                cancellationToken))
                        {
                            degraded = true;
                            usedFallback = true;
                            MarkRunDegraded();
                            continue;
                        }

                        return FailFromBackend(
                            runId,
                            BootstrapErrorCode.DownloadFailed,
                            failedPackage.Profile.PackageName,
                            downloadFailure,
                            startedAt,
                            degraded,
                            usedFallback);
                    }

                    Transition(runId, BootstrapState.Verifying);
                    PackageRun verifyFailedPackage = null;
                    BootstrapBackendFailure verifyFailure = null;
                    foreach (var package in packageRuns)
                    {
                        if (package.Plan == null || !package.Plan.HasDownload)
                        {
                            continue;
                        }

                        var verified = await InvokeWithTimeoutAsync(
                            token => _backend.VerifyAsync(package.Plan, token),
                            profile,
                            "verify",
                            cancellationToken);
                        if (!verified.IsSuccess || !verified.Value)
                        {
                            verifyFailedPackage = package;
                            verifyFailure = verified.Failure;
                            break;
                        }
                    }

                    if (verifyFailedPackage == null)
                    {
                        break;
                    }

                    if (await RecoverWithFallbackAsync(
                            verifyFailedPackage,
                            packageRuns,
                            packagesUsingFallback,
                            profile,
                            runId,
                            cancellationToken))
                    {
                        degraded = true;
                        usedFallback = true;
                        MarkRunDegraded();
                        continue;
                    }

                    return FailFromBackend(
                        runId,
                        BootstrapErrorCode.VerificationFailed,
                        verifyFailedPackage.Profile.PackageName,
                        verifyFailure,
                        startedAt,
                        degraded,
                        usedFallback);
                }

                foreach (var package in packageRuns)
                {
                    if (!package.Handle.IsManifestVerified)
                    {
                        return Fail(
                            runId,
                            BootstrapErrorCode.VerificationFailed,
                            "ready-gate",
                            package.Profile.PackageName,
                            startedAt,
                            degraded,
                            usedFallback);
                    }
                }

                Transition(runId, BootstrapState.ResourcesReady);
                var handles = new List<IBootstrapPackageHandle>(packageRuns.Count);
                foreach (var package in packageRuns)
                {
                    handles.Add(package.Handle);
                }

                var readyContext = new BootstrapReadyContext(runId, handles, degraded, usedFallback);

                Transition(runId, BootstrapState.LoadingCodeExtension);
                try
                {
                    await _codeLoader.LoadAsync(readyContext, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Fail(
                        runId,
                        BootstrapErrorCode.CodeLoadFailed,
                        "load-code-extension",
                        null,
                        startedAt,
                        degraded,
                        usedFallback,
                        exception);
                }

                Transition(runId, BootstrapState.EnteringGame);
                try
                {
                    await _gameEntry.EnterAsync(readyContext, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return Fail(
                        runId,
                        BootstrapErrorCode.EnterGameFailed,
                        "enter-game",
                        null,
                        startedAt,
                        degraded,
                        usedFallback,
                        exception);
                }

                Transition(runId, BootstrapState.Completed);
                var succeeded = BootstrapRunResult.Succeeded(
                    runId,
                    readyContext,
                    ElapsedSince(startedAt));
                CompleteRun(succeeded);
                return succeeded;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TransitionIfAllowed(runId, BootstrapState.Canceled);
                var canceled = BootstrapRunResult.Failed(
                    runId,
                    BootstrapState.Canceled,
                    BootstrapErrorCode.Canceled,
                    new BootstrapFailureContext(
                        State,
                        BootstrapErrorCode.Canceled,
                        string.Empty,
                        "run",
                        "canceled"),
                    ElapsedSince(startedAt),
                    degraded,
                    usedFallback);
                CompleteRun(canceled);
                return canceled;
            }
            catch (Exception exception)
            {
                return Fail(
                    runId,
                    BootstrapErrorCode.Unexpected,
                    "run",
                    null,
                    startedAt,
                    degraded,
                    usedFallback,
                    exception);
            }
        }

        private async UniTask<BootstrapBackendResult<BootstrapVersion>> RequestRemoteVersionAsync(
            PackageRun package,
            BootstrapProfile profile,
            Guid runId,
            CancellationToken cancellationToken)
        {
            var primary = await ExecuteWithRetryAsync(
                token => _backend.RequestVersionAsync(
                    package.Handle,
                    profile,
                    BootstrapEndpoint.Primary,
                    token),
                profile,
                runId,
                package.Profile.PackageName,
                "request-version",
                BootstrapEndpoint.Primary,
                cancellationToken);
            if (primary.IsSuccess || profile.FallbackCdn == null ||
                profile.FallbackCdn == profile.PrimaryCdn)
            {
                return primary;
            }

            return await ExecuteWithRetryAsync(
                token => _backend.RequestVersionAsync(
                    package.Handle,
                    profile,
                    BootstrapEndpoint.Fallback,
                    token),
                profile,
                runId,
                package.Profile.PackageName,
                "request-version",
                BootstrapEndpoint.Fallback,
                cancellationToken);
        }

        private UniTask<BootstrapBackendResult<bool>> ActivateVersionAsync(
            PackageRun package,
            BootstrapProfile profile,
            Guid runId,
            CancellationToken cancellationToken)
        {
            return ExecuteWithRetryAsync(
                token => _backend.ActivateManifestAsync(
                    package.Handle,
                    package.Version,
                    profile,
                    package.Version.Source,
                    token),
                profile,
                runId,
                package.Profile.PackageName,
                "activate-manifest",
                package.Version.Source,
                cancellationToken);
        }

        private async UniTask<BootstrapBackendResult<BootstrapVersion>> FindVerifiedFallbackAsync(
            PackageRun package,
            BootstrapProfile profile,
            Guid runId,
            CancellationToken cancellationToken)
        {
            if (profile.FallbackPolicy != BootstrapFallbackPolicy.VerifiedLocalOrBuiltin)
            {
                return BootstrapBackendResult<BootstrapVersion>.Failed(
                    new BootstrapBackendFailure(
                        BootstrapBackendErrorKind.Permanent,
                        "find-fallback",
                        "fallback-disabled"));
            }

            var fallback = await InvokeWithTimeoutAsync(
                token => _backend.FindVerifiedFallbackAsync(
                    package.Handle,
                    profile,
                    token),
                profile,
                "find-fallback",
                cancellationToken);
            if (!fallback.IsSuccess || fallback.Value == null)
            {
                return fallback;
            }

            if (!fallback.Value.IsVerified ||
                (fallback.Value.Source != BootstrapEndpoint.VerifiedLocal &&
                 fallback.Value.Source != BootstrapEndpoint.VerifiedBuiltin))
            {
                return BootstrapBackendResult<BootstrapVersion>.Failed(
                    new BootstrapBackendFailure(
                        BootstrapBackendErrorKind.Integrity,
                        "find-fallback",
                        "fallback-not-verified"));
            }

            EmitTelemetry(new BootstrapTelemetryEvent(
                runId,
                BootstrapTelemetryKind.FallbackActivated,
                State,
                _clock.UtcNow,
                package.Profile.PackageName,
                "find-fallback",
                endpoint: fallback.Value.Source,
                degraded: true));
            return fallback;
        }

        private async UniTask<bool> RecoverWithFallbackAsync(
            PackageRun failedPackage,
            IReadOnlyList<PackageRun> packages,
            ISet<string> packagesUsingFallback,
            BootstrapProfile profile,
            Guid runId,
            CancellationToken cancellationToken)
        {
            if (packagesUsingFallback.Contains(failedPackage.Profile.PackageName))
            {
                return false;
            }

            var fallback = await FindVerifiedFallbackAsync(
                failedPackage,
                profile,
                runId,
                cancellationToken);
            if (!fallback.IsSuccess)
            {
                return false;
            }

            Transition(runId, BootstrapState.ActivatingManifest);
            MarkRunDegraded();
            failedPackage.Version = fallback.Value;
            var activated = await ActivateVersionAsync(
                failedPackage,
                profile,
                runId,
                cancellationToken);
            if (!activated.IsSuccess || !activated.Value)
            {
                return false;
            }

            packagesUsingFallback.Add(failedPackage.Profile.PackageName);
            foreach (var package in packages)
            {
                package.Plan = null;
            }

            return true;
        }

        private async UniTask<BootstrapBackendResult<T>> ExecuteWithRetryAsync<T>(
            Func<CancellationToken, UniTask<BootstrapBackendResult<T>>> operation,
            BootstrapProfile profile,
            Guid runId,
            string packageName,
            string operationName,
            BootstrapEndpoint endpoint,
            CancellationToken cancellationToken)
        {
            BootstrapBackendResult<T> last = null;
            for (var attempt = 1; attempt <= profile.MaximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EmitTelemetry(new BootstrapTelemetryEvent(
                    runId,
                    BootstrapTelemetryKind.AttemptStarted,
                    State,
                    _clock.UtcNow,
                    packageName,
                    operationName,
                    attempt,
                    endpoint));

                last = await InvokeWithTimeoutAsync(
                    operation,
                    profile,
                    operationName,
                    cancellationToken);
                if (last.IsSuccess)
                {
                    return last;
                }

                EmitTelemetry(new BootstrapTelemetryEvent(
                    runId,
                    BootstrapTelemetryKind.AttemptFailed,
                    State,
                    _clock.UtcNow,
                    packageName,
                    operationName,
                    attempt,
                    endpoint,
                    MapError(BootstrapErrorCode.BackendFailure, last.Failure)));

                if (last.Failure == null ||
                    !last.Failure.IsRetryable ||
                    attempt == profile.MaximumAttempts)
                {
                    break;
                }

                await _delay.DelayAsync(CalculateBackoff(profile, attempt), cancellationToken);
            }

            return last ?? BootstrapBackendResult<T>.Failed(
                new BootstrapBackendFailure(
                    BootstrapBackendErrorKind.Permanent,
                    operationName,
                    "operation-produced-no-result"));
        }

        private async UniTask<BootstrapBackendResult<T>> InvokeWithTimeoutAsync<T>(
            Func<CancellationToken, UniTask<BootstrapBackendResult<T>>> operation,
            BootstrapProfile profile,
            string operationName,
            CancellationToken cancellationToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                Task<BootstrapBackendResult<T>> operationTask;
                try
                {
                    operationTask = operation(linked.Token).AsTask();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return BackendException<T>(operationName, exception);
                }

                Task timeoutTask;
                try
                {
                    timeoutTask = _delay.DelayAsync(
                            profile.OperationTimeout,
                            linked.Token)
                        .AsTask();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    linked.Cancel();
                    throw;
                }
                catch (Exception exception)
                {
                    linked.Cancel();
                    Observe(operationTask);
                    return BackendException<T>("timeout-delay", exception);
                }

                var completed = await Task.WhenAny(operationTask, timeoutTask);
                if (completed == operationTask)
                {
                    linked.Cancel();
                    try
                    {
                        return await operationTask;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        return BootstrapBackendResult<T>.Failed(
                            new BootstrapBackendFailure(
                                BootstrapBackendErrorKind.Canceled,
                                operationName,
                                "backend-canceled"));
                    }
                    catch (Exception exception)
                    {
                        return BackendException<T>(operationName, exception);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                linked.Cancel();
                Observe(operationTask);
                return BootstrapBackendResult<T>.Failed(
                    new BootstrapBackendFailure(
                        BootstrapBackendErrorKind.Timeout,
                        operationName,
                        "operation-timeout"));
            }
        }

        private async UniTask ResetCoreAsync()
        {
            Task<BootstrapRunResult> inflight;
            CancellationTokenSource runCancellation;
            lock (_gate)
            {
                inflight = _inflight;
                runCancellation = _runCancellation;
            }

            runCancellation?.Cancel();
            if (inflight != null)
            {
                await inflight;
            }

            try
            {
                TransitionIfAllowed(_currentRunId, BootstrapState.Resetting);
                await _backend.ResetAsync(CancellationToken.None);

                lock (_gate)
                {
                    _activeProfile = null;
                    _inflight = null;
                    _runCancellation?.Dispose();
                    _runCancellation = null;
                    _lastResult = null;
                    _currentRunId = Guid.Empty;
                    _runDegraded = false;
                }

                if (_shuttingDown)
                {
                    TransitionIfAllowed(_currentRunId, BootstrapState.ShuttingDown);
                }
                else
                {
                    Transition(_currentRunId, BootstrapState.Idle);
                }
            }
            finally
            {
                lock (_gate)
                {
                    _resetting = false;
                }
            }
        }

        private async UniTask ShutdownCoreAsync()
        {
            var failures = new List<Exception>();
            Task resetTask;
            Task<BootstrapRunResult> inflight;
            CancellationTokenSource runCancellation;
            lock (_gate)
            {
                resetTask = _resetTask;
                inflight = _inflight;
                runCancellation = _runCancellation;
            }

            runCancellation?.Cancel();
            if (resetTask != null && !resetTask.IsCompleted)
            {
                try
                {
                    await resetTask;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            else if (inflight != null)
            {
                try
                {
                    await inflight;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            TransitionIfAllowed(_currentRunId, BootstrapState.ShuttingDown);
            var backendShutDown = false;
            try
            {
                await _backend.ShutdownAsync(CancellationToken.None);
                backendShutDown = true;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            lock (_gate)
            {
                _activeProfile = null;
                _inflight = null;
                _runCancellation?.Dispose();
                _runCancellation = null;
                _shutDown = backendShutDown;
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Bootstrap shutdown did not complete cleanly.",
                    failures);
            }
        }

        private void ReportDownloadProgress(
            Guid runId,
            PackageRun package,
            BootstrapDownloadProgress value,
            long completedPackageBytes,
            int completedPackageFiles,
            long totalBytes,
            int totalFiles,
            bool degraded,
            long generation)
        {
            lock (_gate)
            {
                if (_currentRunId != runId ||
                    _state != BootstrapState.Downloading ||
                    _resetting ||
                    _shuttingDown ||
                    _downloadProgressGeneration != generation)
                {
                    return;
                }
            }

            var completeBytes = CheckedAdd(completedPackageBytes, value.CompletedBytes);
            var completeFiles = CheckedAdd(completedPackageFiles, value.CompletedFiles);
            var normalized = totalBytes <= 0
                ? totalFiles == 0 ? 1f : (float)completeFiles / totalFiles
                : (float)((double)completeBytes / totalBytes);
            EmitProgress(new BootstrapProgress(
                runId,
                BootstrapState.Downloading,
                Math.Max(0f, Math.Min(1f, normalized)),
                package.Profile.PackageName,
                completeFiles,
                totalFiles,
                completeBytes,
                totalBytes,
                degraded));
        }

        private async UniTask<BootstrapBackendResult<bool>> DownloadAttemptAsync(
            Guid runId,
            PackageRun package,
            long completedPackageBytes,
            int completedPackageFiles,
            long totalBytes,
            int totalFiles,
            bool degraded,
            CancellationToken cancellationToken)
        {
            var generation = BeginDownloadProgress();
            using (cancellationToken.Register(() => InvalidateDownloadProgress(generation)))
            {
                try
                {
                    return await _backend.DownloadAsync(
                        package.Plan,
                        value => ReportDownloadProgress(
                            runId,
                            package,
                            value,
                            completedPackageBytes,
                            completedPackageFiles,
                            totalBytes,
                            totalFiles,
                            degraded,
                            generation),
                        cancellationToken);
                }
                finally
                {
                    InvalidateDownloadProgress(generation);
                }
            }
        }

        private long BeginDownloadProgress()
        {
            lock (_gate)
            {
                return ++_downloadProgressGeneration;
            }
        }

        private void InvalidateDownloadProgress(long generation)
        {
            lock (_gate)
            {
                if (_downloadProgressGeneration == generation)
                {
                    _downloadProgressGeneration++;
                }
            }
        }

        private void Transition(Guid runId, BootstrapState target)
        {
            BootstrapState previous;
            lock (_gate)
            {
                previous = _state;
                BootstrapStateGraph.EnsureTransition(previous, target);
                _state = target;
            }

            PublishTransition(runId, previous, target);
        }

        private void PublishTransition(
            Guid runId,
            BootstrapState previous,
            BootstrapState target)
        {
            EmitTelemetry(new BootstrapTelemetryEvent(
                runId,
                BootstrapTelemetryKind.StateChanged,
                target,
                _clock.UtcNow,
                operation: previous + "->" + target));
            EmitProgress(new BootstrapProgress(
                runId,
                target,
                StateProgress(target),
                degraded: IsRunDegraded()));
        }

        private void TransitionIfAllowed(Guid runId, BootstrapState target)
        {
            BootstrapState previous;
            var changed = false;
            lock (_gate)
            {
                previous = _state;
                if (BootstrapStateGraph.CanTransition(previous, target))
                {
                    _state = target;
                    changed = true;
                }
            }

            if (changed)
            {
                PublishTransition(runId, previous, target);
            }
        }

        private BootstrapRunResult FailFromBackend(
            Guid runId,
            BootstrapErrorCode defaultCode,
            string packageName,
            BootstrapBackendFailure failure,
            DateTimeOffset startedAt,
            bool degraded,
            bool usedFallback)
        {
            return Fail(
                runId,
                MapError(defaultCode, failure),
                failure?.Operation ?? "backend",
                packageName,
                startedAt,
                degraded,
                usedFallback,
                null,
                failure);
        }

        private BootstrapRunResult Fail(
            Guid runId,
            BootstrapErrorCode errorCode,
            string operation,
            string packageName,
            DateTimeOffset startedAt,
            bool degraded,
            bool usedFallback,
            Exception exception = null,
            BootstrapBackendFailure backendFailure = null)
        {
            TransitionIfAllowed(runId, BootstrapState.Failed);
            var failure = new BootstrapFailureContext(
                State,
                errorCode,
                packageName,
                operation,
                Redact(backendFailure?.Reason ?? exception?.Message ?? errorCode.ToString()),
                endpoint: BootstrapEndpoint.None,
                exceptionType: backendFailure?.ExceptionType ?? exception?.GetType().Name);
            var result = BootstrapRunResult.Failed(
                runId,
                BootstrapState.Failed,
                errorCode,
                failure,
                ElapsedSince(startedAt),
                degraded,
                usedFallback);
            CompleteRun(result);
            return result;
        }

        private void CompleteRun(BootstrapRunResult result)
        {
            lock (_gate)
            {
                _lastResult = result;
            }

            EmitTelemetry(new BootstrapTelemetryEvent(
                result.RunId,
                BootstrapTelemetryKind.RunCompleted,
                result.FinalState,
                _clock.UtcNow,
                errorCode: result.ErrorCode,
                degraded: result.Degraded));
        }

        private BootstrapRunResult CreateRejectedResult(
            BootstrapProfile profile,
            BootstrapErrorCode code,
            string operation)
        {
            var runId = Guid.NewGuid();
            return BootstrapRunResult.Failed(
                runId,
                BootstrapState.Failed,
                code,
                new BootstrapFailureContext(
                    State,
                    code,
                    profile.PackageName,
                    operation,
                    code.ToString()),
                TimeSpan.Zero);
        }

        private void EmitTelemetry(BootstrapTelemetryEvent telemetryEvent)
        {
            try
            {
                _telemetry.Record(telemetryEvent);
            }
            catch (Exception exception)
            {
                AddSinkDiagnostic(
                    telemetryEvent.RunId,
                    nameof(IBootstrapTelemetrySink),
                    telemetryEvent.State,
                    exception);
            }
        }

        private void EmitProgress(BootstrapProgress progress)
        {
            try
            {
                _progress.Report(progress);
            }
            catch (Exception exception)
            {
                AddSinkDiagnostic(
                    progress.RunId,
                    nameof(IBootstrapProgressSink),
                    progress.State,
                    exception);
            }

            EmitTelemetry(new BootstrapTelemetryEvent(
                progress.RunId,
                BootstrapTelemetryKind.Progress,
                progress.State,
                _clock.UtcNow,
                progress.PackageName,
                degraded: progress.Degraded));
        }

        private void AddSinkDiagnostic(
            Guid runId,
            string sink,
            BootstrapState state,
            Exception exception)
        {
            lock (_gate)
            {
                _sinkDiagnostics.Add(new BootstrapSinkDiagnostic(
                    runId,
                    sink,
                    state,
                    exception.GetType().Name,
                    _clock.UtcNow));
            }
        }

        private TimeSpan ElapsedSince(DateTimeOffset startedAt)
        {
            var elapsed = _clock.UtcNow - startedAt;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }

        private static TimeSpan CalculateBackoff(BootstrapProfile profile, int failedAttempt)
        {
            var multiplier = Math.Pow(2d, Math.Max(0, failedAttempt - 1));
            var ticks = profile.InitialRetryBackoff.Ticks * multiplier;
            return TimeSpan.FromTicks((long)Math.Min(ticks, profile.MaximumRetryBackoff.Ticks));
        }

        private static BootstrapErrorCode MapError(
            BootstrapErrorCode defaultCode,
            BootstrapBackendFailure failure)
        {
            if (failure == null)
            {
                return defaultCode;
            }

            switch (failure.Kind)
            {
                case BootstrapBackendErrorKind.Timeout:
                    return BootstrapErrorCode.Timeout;
                case BootstrapBackendErrorKind.Canceled:
                    return BootstrapErrorCode.Canceled;
                case BootstrapBackendErrorKind.Integrity:
                    return BootstrapErrorCode.VerificationFailed;
                default:
                    return defaultCode;
            }
        }

        private static BootstrapBackendResult<T> BackendException<T>(
            string operation,
            Exception exception)
        {
            return BootstrapBackendResult<T>.Failed(
                new BootstrapBackendFailure(
                    BootstrapBackendErrorKind.Permanent,
                    operation,
                    Redact(exception.Message),
                    exception.GetType().Name));
        }

        private static string Redact(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : SensitiveUri.Replace(value, "<redacted-url>");
        }

        private static long CheckedAdd(long left, long right)
        {
            if (right > 0 && left > long.MaxValue - right)
            {
                throw new OverflowException("Bootstrap byte count exceeds Int64 capacity.");
            }

            return left + right;
        }

        private static int CheckedAdd(int left, int right)
        {
            if (right > 0 && left > int.MaxValue - right)
            {
                throw new OverflowException("Bootstrap file count exceeds Int32 capacity.");
            }

            return left + right;
        }

        private void MarkRunDegraded()
        {
            lock (_gate)
            {
                _runDegraded = true;
            }
        }

        private bool IsRunDegraded()
        {
            lock (_gate)
            {
                return _runDegraded;
            }
        }

        private static float StateProgress(BootstrapState state)
        {
            switch (state)
            {
                case BootstrapState.Idle:
                    return 0f;
                case BootstrapState.InitializingPackage:
                    return 0.05f;
                case BootstrapState.RequestingVersion:
                    return 0.12f;
                case BootstrapState.ActivatingManifest:
                    return 0.2f;
                case BootstrapState.CalculatingDownload:
                    return 0.28f;
                case BootstrapState.AwaitingConfirmation:
                    return 0.32f;
                case BootstrapState.CheckingDisk:
                    return 0.36f;
                case BootstrapState.Downloading:
                    return 0.4f;
                case BootstrapState.Verifying:
                    return 0.82f;
                case BootstrapState.ResourcesReady:
                    return 0.9f;
                case BootstrapState.LoadingCodeExtension:
                    return 0.94f;
                case BootstrapState.EnteringGame:
                    return 0.98f;
                case BootstrapState.Completed:
                    return 1f;
                default:
                    return 0f;
            }
        }

        private static async UniTask<BootstrapRunResult> AwaitCallerAsync(
            Task<BootstrapRunResult> task,
            CancellationToken cancellationToken)
        {
            return await task.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        private static async UniTask AwaitLifecycleCallerAsync(
            Task task,
            CancellationToken cancellationToken)
        {
            await task.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        private static void Observe(Task task)
        {
            task.ContinueWith(
                completed =>
                {
                    var ignored = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
