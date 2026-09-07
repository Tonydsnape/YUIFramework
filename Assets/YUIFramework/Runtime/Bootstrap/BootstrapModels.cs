using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YUIFramework.Bootstrap
{
    public enum BootstrapErrorCode
    {
        None,
        InvalidProfile,
        AlreadyRunningDifferentProfile,
        ResetInProgress,
        RunnerShutDown,
        EditorSimulateUnavailable,
        NetworkUnavailable,
        InitializationFailed,
        VersionRequestFailed,
        ManifestActivationFailed,
        ConfirmationDeclined,
        InsufficientDiskSpace,
        DownloadFailed,
        VerificationFailed,
        CodeLoadFailed,
        EnterGameFailed,
        Timeout,
        Canceled,
        FallbackUnavailable,
        BackendFailure,
        InvalidStateTransition,
        Unexpected,
    }

    public enum BootstrapEndpoint
    {
        None,
        Primary,
        Fallback,
        VerifiedLocal,
        VerifiedBuiltin,
    }

    public enum BootstrapBackendErrorKind
    {
        None,
        TransientNetwork,
        Timeout,
        Permanent,
        Integrity,
        Canceled,
    }

    public sealed class BootstrapFailureContext
    {
        public BootstrapFailureContext(
            BootstrapState state,
            BootstrapErrorCode errorCode,
            string packageName,
            string operation,
            string reason,
            int attempt = 0,
            BootstrapEndpoint endpoint = BootstrapEndpoint.None,
            string exceptionType = null)
        {
            State = state;
            ErrorCode = errorCode;
            PackageName = packageName ?? string.Empty;
            Operation = operation ?? string.Empty;
            Reason = reason ?? string.Empty;
            Attempt = attempt;
            Endpoint = endpoint;
            ExceptionType = exceptionType ?? string.Empty;
        }

        public BootstrapState State { get; }

        public BootstrapErrorCode ErrorCode { get; }

        public string PackageName { get; }

        public string Operation { get; }

        public string Reason { get; }

        public int Attempt { get; }

        public BootstrapEndpoint Endpoint { get; }

        public string ExceptionType { get; }
    }

    public sealed class BootstrapProgress
    {
        public BootstrapProgress(
            Guid runId,
            BootstrapState state,
            float normalized,
            string packageName = null,
            int completedFiles = 0,
            int totalFiles = 0,
            long completedBytes = 0,
            long totalBytes = 0,
            bool degraded = false)
        {
            if (normalized < 0f || normalized > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(normalized));
            }

            if (completedFiles < 0 || totalFiles < 0 || completedFiles > totalFiles)
            {
                throw new ArgumentOutOfRangeException(nameof(completedFiles));
            }

            if (completedBytes < 0 || totalBytes < 0 || completedBytes > totalBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(completedBytes));
            }

            RunId = runId;
            State = state;
            Normalized = normalized;
            PackageName = packageName ?? string.Empty;
            CompletedFiles = completedFiles;
            TotalFiles = totalFiles;
            CompletedBytes = completedBytes;
            TotalBytes = totalBytes;
            Degraded = degraded;
        }

        public Guid RunId { get; }

        public BootstrapState State { get; }

        public float Normalized { get; }

        public string PackageName { get; }

        public int CompletedFiles { get; }

        public int TotalFiles { get; }

        public long CompletedBytes { get; }

        public long TotalBytes { get; }

        public bool Degraded { get; }
    }

    public enum BootstrapTelemetryKind
    {
        RunStarted,
        StateChanged,
        AttemptStarted,
        AttemptFailed,
        FallbackActivated,
        Progress,
        SinkFailure,
        RunCompleted,
    }

    public sealed class BootstrapTelemetryEvent
    {
        public BootstrapTelemetryEvent(
            Guid runId,
            BootstrapTelemetryKind kind,
            BootstrapState state,
            DateTimeOffset timestamp,
            string packageName = null,
            string operation = null,
            int attempt = 0,
            BootstrapEndpoint endpoint = BootstrapEndpoint.None,
            BootstrapErrorCode errorCode = BootstrapErrorCode.None,
            bool degraded = false)
        {
            RunId = runId;
            Kind = kind;
            State = state;
            Timestamp = timestamp;
            PackageName = packageName ?? string.Empty;
            Operation = operation ?? string.Empty;
            Attempt = attempt;
            Endpoint = endpoint;
            ErrorCode = errorCode;
            Degraded = degraded;
        }

        public Guid RunId { get; }

        public BootstrapTelemetryKind Kind { get; }

        public BootstrapState State { get; }

        public DateTimeOffset Timestamp { get; }

        public string PackageName { get; }

        public string Operation { get; }

        public int Attempt { get; }

        public BootstrapEndpoint Endpoint { get; }

        public BootstrapErrorCode ErrorCode { get; }

        public bool Degraded { get; }
    }

    public sealed class BootstrapSinkDiagnostic
    {
        public BootstrapSinkDiagnostic(
            Guid runId,
            string sink,
            BootstrapState state,
            string exceptionType,
            DateTimeOffset timestamp)
        {
            RunId = runId;
            Sink = sink ?? string.Empty;
            State = state;
            ExceptionType = exceptionType ?? string.Empty;
            Timestamp = timestamp;
        }

        public Guid RunId { get; }

        public string Sink { get; }

        public BootstrapState State { get; }

        public string ExceptionType { get; }

        public DateTimeOffset Timestamp { get; }
    }

    public sealed class BootstrapReadyContext
    {
        private readonly ReadOnlyCollection<IBootstrapPackageHandle> _packages;

        public BootstrapReadyContext(
            Guid runId,
            IEnumerable<IBootstrapPackageHandle> packages,
            bool degraded,
            bool usedFallback)
        {
            if (packages == null)
            {
                throw new ArgumentNullException(nameof(packages));
            }

            var copy = new List<IBootstrapPackageHandle>();
            foreach (var package in packages)
            {
                if (package == null)
                {
                    throw new ArgumentException("Ready package collection must not contain null.", nameof(packages));
                }

                copy.Add(package);
            }

            if (copy.Count == 0)
            {
                throw new ArgumentException("Ready context requires at least one package.", nameof(packages));
            }

            RunId = runId;
            _packages = new ReadOnlyCollection<IBootstrapPackageHandle>(copy);
            Degraded = degraded;
            UsedFallback = usedFallback;
        }

        public Guid RunId { get; }

        public IReadOnlyList<IBootstrapPackageHandle> Packages => _packages;

        public bool Degraded { get; }

        public bool UsedFallback { get; }
    }

    public sealed class BootstrapRunResult
    {
        private BootstrapRunResult(
            Guid runId,
            BootstrapState finalState,
            BootstrapErrorCode errorCode,
            BootstrapFailureContext failure,
            BootstrapReadyContext readyContext,
            bool degraded,
            bool usedFallback,
            TimeSpan elapsed)
        {
            RunId = runId;
            FinalState = finalState;
            ErrorCode = errorCode;
            Failure = failure;
            ReadyContext = readyContext;
            Degraded = degraded;
            UsedFallback = usedFallback;
            Elapsed = elapsed;
        }

        public Guid RunId { get; }

        public BootstrapState FinalState { get; }

        public BootstrapErrorCode ErrorCode { get; }

        public BootstrapFailureContext Failure { get; }

        public BootstrapReadyContext ReadyContext { get; }

        public bool Degraded { get; }

        public bool UsedFallback { get; }

        public TimeSpan Elapsed { get; }

        public bool IsSuccess => FinalState == BootstrapState.Completed &&
                                 ErrorCode == BootstrapErrorCode.None &&
                                 ReadyContext != null;

        public static BootstrapRunResult Succeeded(
            Guid runId,
            BootstrapReadyContext readyContext,
            TimeSpan elapsed)
        {
            if (readyContext == null)
            {
                throw new ArgumentNullException(nameof(readyContext));
            }

            return new BootstrapRunResult(
                runId,
                BootstrapState.Completed,
                BootstrapErrorCode.None,
                null,
                readyContext,
                readyContext.Degraded,
                readyContext.UsedFallback,
                elapsed);
        }

        public static BootstrapRunResult Failed(
            Guid runId,
            BootstrapState finalState,
            BootstrapErrorCode errorCode,
            BootstrapFailureContext failure,
            TimeSpan elapsed,
            bool degraded = false,
            bool usedFallback = false)
        {
            if (finalState != BootstrapState.Failed && finalState != BootstrapState.Canceled)
            {
                throw new ArgumentException("A failed result must end in Failed or Canceled.", nameof(finalState));
            }

            if (errorCode == BootstrapErrorCode.None)
            {
                throw new ArgumentException("A failed result requires a non-success error code.", nameof(errorCode));
            }

            return new BootstrapRunResult(
                runId,
                finalState,
                errorCode,
                failure,
                null,
                degraded,
                usedFallback,
                elapsed);
        }
    }
}
