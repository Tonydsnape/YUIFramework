#pragma warning disable CS0618

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;

namespace YUIFramework.HotUpdate
{
    internal sealed class LegacyBootstrapRuntime
    {
        private static LegacyBootstrapRuntime _current;

        private readonly object _lifecycleGate = new object();
        private BootstrapProfile _profile;
        private BootstrapRunner _runner;
        private YooAssetBootstrapComposition _composition;
        private Func<BootstrapReadyContext, CancellationToken, UniTask> _gameEntry;
        private Func<long, UniTask<bool>> _confirmationOverride;
        private bool _lifecycleRequested;
        private bool _entryInProgress;
        private bool _shutdownSucceeded;
        private System.Threading.Tasks.Task _resetTask;
        private System.Threading.Tasks.Task _shutdownTask;

        private LegacyBootstrapRuntime()
        {
            _profile = CreateDefaultProfile();
        }

        internal static LegacyBootstrapRuntime Current =>
            _current ?? (_current = new LegacyBootstrapRuntime());

        internal static bool TryGetCurrent(out LegacyBootstrapRuntime runtime)
        {
            runtime = _current;
            return runtime != null;
        }

        internal BootstrapProfile Profile => _profile;

        internal BootstrapRunner Runner => _runner;

        internal BootstrapRunResult LastResult => _runner?.LastResult;

        internal YooAssetBootstrapComposition Composition
        {
            get
            {
                lock (_lifecycleGate)
                {
                    return _composition;
                }
            }
        }

        internal IUIResourceService ResourceService => Composition?.ResourceService;

        internal YooAssetBootstrapPackageHandle PrimaryPackage =>
            LastResult?.ReadyContext?.Packages.Count > 0
                ? LastResult.ReadyContext.Packages[0] as YooAssetBootstrapPackageHandle
                : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _current = null;
        }

        internal void SetProfile(BootstrapProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (_runner != null &&
                (_runner.State != BootstrapState.Idle || _runner.LastResult != null))
            {
                throw new InvalidOperationException(
                    "Reset the legacy bootstrap runtime before changing its profile.");
            }

            _profile = profile;
        }

        internal void SetMode(BootstrapMode mode)
        {
            SetProfile(CopyProfile(mode: mode));
        }

        internal void SetCdn(Uri primary, Uri fallback)
        {
            SetProfile(CopyProfile(primary: primary, fallback: fallback, replaceCdn: true));
        }

        internal void SetDownloadConcurrency(int value)
        {
            SetProfile(CopyProfile(downloadConcurrency: value));
        }

        internal void SetMaximumAttempts(int value)
        {
            SetProfile(CopyProfile(maximumAttempts: value));
        }

        internal void SetTimeout(TimeSpan value)
        {
            SetProfile(CopyProfile(timeout: value));
        }

        internal void SetGameEntry(
            Func<BootstrapReadyContext, CancellationToken, UniTask> gameEntry)
        {
            _gameEntry = gameEntry;
        }

        internal void SetConfirmationOverride(Func<long, UniTask<bool>> confirmation)
        {
            _confirmationOverride = confirmation;
        }

        internal UniTask<BootstrapRunResult> RunOnceAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            System.Threading.Tasks.Task<BootstrapRunResult> task;
            lock (_lifecycleGate)
            {
                if (_lifecycleRequested)
                {
                    throw new InvalidOperationException(
                        "Legacy bootstrap reset or shutdown is in progress.");
                }

                EnsureRunner();
                if (_runner.LastResult != null)
                {
                    return UniTask.FromResult(_runner.LastResult);
                }

                // Starting while holding the lifecycle gate makes acceptance atomic with
                // Reset/Shutdown; the runner itself gives each caller independent cancellation.
                task = _runner.RunAsync(_profile).AsTask();
            }

            return AwaitAcceptedRunAsync(task, cancellationToken);
        }

        private static async UniTask<BootstrapRunResult> AwaitAcceptedRunAsync(
            System.Threading.Tasks.Task<BootstrapRunResult> task,
            CancellationToken cancellationToken)
        {
            return await task.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        internal async UniTask ResetAsync(CancellationToken cancellationToken = default)
        {
            System.Threading.Tasks.Task task;
            lock (_lifecycleGate)
            {
                if (_shutdownTask != null)
                {
                    task = _shutdownTask;
                }
                else
                {
                    if (_resetTask == null || _resetTask.IsCompleted)
                    {
                        _lifecycleRequested = true;
                        _resetTask = ResetCoreAsync().AsTask();
                    }

                    task = _resetTask;
                }
            }

            await task.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        internal async UniTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            System.Threading.Tasks.Task task;
            lock (_lifecycleGate)
            {
                if (_shutdownTask == null ||
                    (!_shutdownSucceeded &&
                     _shutdownTask.IsCompleted &&
                     _shutdownTask.IsFaulted))
                {
                    _lifecycleRequested = true;
                    var resetToWait = _resetTask != null && !_resetTask.IsCompleted
                        ? _resetTask
                        : null;
                    _shutdownTask = ShutdownCoreAsync(resetToWait).AsTask();
                }

                task = _shutdownTask;
            }

            await task.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        private async UniTask ResetCoreAsync()
        {
            var failures = new System.Collections.Generic.List<Exception>();
            YooAssetBootstrapComposition failedComposition = null;
            var composition = DetachCompositionIfEntryIdle();
            if (composition != null)
            {
                try
                {
                    await composition.ShutdownResourceServiceAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    failedComposition = composition;
                }
            }

            if (_runner != null)
            {
                try
                {
                    await _runner.ResetAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            composition = DetachCompositionIfEntryIdle();
            if (composition != null)
            {
                try
                {
                    await composition.ShutdownResourceServiceAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    failedComposition = failedComposition ?? composition;
                }
            }

            if (failedComposition != null)
            {
                RestoreComposition(failedComposition);
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Legacy bootstrap reset did not complete cleanly.",
                    failures);
            }

            lock (_lifecycleGate)
            {
                if (_shutdownTask == null)
                {
                    _lifecycleRequested = false;
                }
            }
        }

        private async UniTask ShutdownCoreAsync(System.Threading.Tasks.Task resetToWait)
        {
            var failures = new System.Collections.Generic.List<Exception>();
            YooAssetBootstrapComposition failedComposition = null;
            if (resetToWait != null)
            {
                try
                {
                    await resetToWait;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            var composition = DetachCompositionIfEntryIdle();
            if (composition != null)
            {
                try
                {
                    await composition.ShutdownResourceServiceAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    failedComposition = composition;
                }
            }

            if (_runner != null)
            {
                try
                {
                    await _runner.ShutdownAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            composition = DetachCompositionIfEntryIdle();
            if (composition != null)
            {
                try
                {
                    await composition.ShutdownResourceServiceAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    failedComposition = failedComposition ?? composition;
                }
            }

            if (failedComposition != null)
            {
                RestoreComposition(failedComposition);
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Legacy bootstrap shutdown did not complete cleanly.",
                    failures);
            }

            lock (_lifecycleGate)
            {
                _shutdownSucceeded = true;
            }
        }

        private void EnsureRunner()
        {
            if (_runner != null)
            {
                return;
            }

            _runner = new BootstrapRunner(
                new YooAssetBootstrapBackend(),
                new LegacyGameEntry(this),
                NoopBootstrapTelemetrySink.Instance,
                new LegacyProgressSink(),
                NoopBootstrapCodeLoader.Instance,
                confirmation: new LegacyConfirmation(this));
        }

        private async UniTask EnterGameAsync(
            BootstrapReadyContext context,
            CancellationToken cancellationToken)
        {
            lock (_lifecycleGate)
            {
                if (_lifecycleRequested)
                {
                    throw new OperationCanceledException(
                        "Legacy bootstrap lifecycle cleanup has started.",
                        cancellationToken);
                }

                if (_composition != null)
                {
                    throw new InvalidOperationException(
                        "Legacy resource composition already exists. Reset before running again.");
                }

                _entryInProgress = true;
            }

            YooAssetBootstrapComposition created = null;
            var entered = false;
            try
            {
                created = YooAssetBootstrapComposition.Create(context);
                lock (_lifecycleGate)
                {
                    if (_lifecycleRequested)
                    {
                        throw new OperationCanceledException(
                            "Legacy bootstrap lifecycle cleanup has started.",
                            cancellationToken);
                    }

                    _composition = created;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (_gameEntry != null)
                {
                    await _gameEntry(context, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                entered = true;
            }
            finally
            {
                var close = false;
                lock (_lifecycleGate)
                {
                    _entryInProgress = false;
                    if ((!entered || _lifecycleRequested) &&
                        ReferenceEquals(_composition, created))
                    {
                        _composition = null;
                        close = true;
                    }
                    else if (!entered && created != null && _composition == null)
                    {
                        close = true;
                    }
                }

                if (close)
                {
                    await created.ShutdownResourceServiceAsync();
                }
            }
        }

        private YooAssetBootstrapComposition DetachCompositionIfEntryIdle()
        {
            lock (_lifecycleGate)
            {
                if (_entryInProgress)
                {
                    return null;
                }

                var composition = _composition;
                _composition = null;
                return composition;
            }
        }

        private void RestoreComposition(YooAssetBootstrapComposition composition)
        {
            lock (_lifecycleGate)
            {
                if (_composition == null)
                {
                    _composition = composition;
                }
            }
        }

        private BootstrapProfile CopyProfile(
            BootstrapMode? mode = null,
            Uri primary = null,
            Uri fallback = null,
            bool replaceCdn = false,
            TimeSpan? timeout = null,
            int? maximumAttempts = null,
            int? downloadConcurrency = null)
        {
            return new BootstrapProfile(
                mode ?? _profile.Mode,
                _profile.Packages,
                _profile.ApplicationId,
                _profile.Channel,
                _profile.ApplicationVersion,
                replaceCdn ? primary : _profile.PrimaryCdn,
                replaceCdn ? fallback : _profile.FallbackCdn,
                timeout ?? _profile.OperationTimeout,
                maximumAttempts ?? _profile.MaximumAttempts,
                _profile.InitialRetryBackoff,
                _profile.MaximumRetryBackoff,
                downloadConcurrency ?? _profile.DownloadConcurrency,
                _profile.FallbackPolicy,
                _profile.DiskSafetyMarginBytes,
                _profile.RequireDownloadConfirmation);
        }

        private static BootstrapProfile CreateDefaultProfile()
        {
            var applicationId = string.IsNullOrWhiteSpace(Application.identifier)
                ? "YUIFramework.Application"
                : Application.identifier;
            var applicationVersion = string.IsNullOrWhiteSpace(Application.version)
                ? "0"
                : Application.version;
            return new BootstrapProfile(
                BootstrapMode.EditorSimulate,
                new[] { new BootstrapPackageProfile("DefaultPackage") },
                applicationId,
                "default",
                applicationVersion,
                new Uri("http://127.0.0.1:8080/"),
                null,
                TimeSpan.FromSeconds(30),
                3,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(4),
                8,
                BootstrapFallbackPolicy.VerifiedLocalOrBuiltin,
                64L * 1024L * 1024L,
                true);
        }

        private sealed class LegacyGameEntry : IBootstrapGameEntry
        {
            private readonly LegacyBootstrapRuntime _owner;

            public LegacyGameEntry(LegacyBootstrapRuntime owner)
            {
                _owner = owner;
            }

            public UniTask EnterAsync(
                BootstrapReadyContext context,
                CancellationToken cancellationToken)
            {
                return _owner.EnterGameAsync(context, cancellationToken);
            }
        }

        private sealed class LegacyProgressSink : IBootstrapProgressSink
        {
            public void Report(BootstrapProgress progress)
            {
                HotUpdateLauncher.Publish(progress);
            }
        }

        private sealed class LegacyConfirmation : IBootstrapConfirmation
        {
            private readonly LegacyBootstrapRuntime _owner;

            public LegacyConfirmation(LegacyBootstrapRuntime owner)
            {
                _owner = owner;
            }

            public UniTask<bool> ConfirmAsync(
                BootstrapDownloadPlan plan,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HotUpdateLauncher.PublishDownloadSize(plan.TotalBytes);
                var handler = _owner._confirmationOverride ??
                              HotUpdateLauncher.ConfirmDownloadHandler;
                return handler == null
                    ? UniTask.FromResult(true)
                    : handler(plan.TotalBytes);
            }
        }
    }
}
