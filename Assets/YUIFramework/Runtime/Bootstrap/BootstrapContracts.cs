using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace YUIFramework.Bootstrap
{
    public interface IBootstrapPackageHandle
    {
        string PackageName { get; }

        object NativePackage { get; }

        string ActiveVersion { get; }

        bool IsManifestVerified { get; }
    }

    public sealed class BootstrapBackendFailure
    {
        public BootstrapBackendFailure(
            BootstrapBackendErrorKind kind,
            string operation,
            string reason,
            string exceptionType = null)
        {
            if (kind == BootstrapBackendErrorKind.None)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Operation = operation ?? string.Empty;
            Reason = reason ?? string.Empty;
            ExceptionType = exceptionType ?? string.Empty;
        }

        public BootstrapBackendErrorKind Kind { get; }

        public string Operation { get; }

        public string Reason { get; }

        public string ExceptionType { get; }

        public bool IsRetryable =>
            Kind == BootstrapBackendErrorKind.TransientNetwork ||
            Kind == BootstrapBackendErrorKind.Timeout;
    }

    public sealed class BootstrapBackendResult<T>
    {
        private BootstrapBackendResult(T value, BootstrapBackendFailure failure)
        {
            Value = value;
            Failure = failure;
        }

        public T Value { get; }

        public BootstrapBackendFailure Failure { get; }

        public bool IsSuccess => Failure == null;

        public static BootstrapBackendResult<T> Succeeded(T value)
        {
            return new BootstrapBackendResult<T>(value, null);
        }

        public static BootstrapBackendResult<T> Failed(BootstrapBackendFailure failure)
        {
            return new BootstrapBackendResult<T>(
                default(T),
                failure ?? throw new ArgumentNullException(nameof(failure)));
        }
    }

    public sealed class BootstrapVersion
    {
        public BootstrapVersion(string value, BootstrapEndpoint source, bool isVerified)
        {
            Value = BootstrapPackageProfile.ValidateFileSegment(value, nameof(value), 128);
            Source = source;
            IsVerified = isVerified;
        }

        public string Value { get; }

        public BootstrapEndpoint Source { get; }

        public bool IsVerified { get; }
    }

    public sealed class BootstrapDownloadPlan
    {
        public BootstrapDownloadPlan(
            IBootstrapPackageHandle package,
            int fileCount,
            long totalBytes,
            object backendToken)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            if (fileCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileCount));
            }

            if (totalBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalBytes));
            }

            FileCount = fileCount;
            TotalBytes = totalBytes;
            BackendToken = backendToken;
        }

        public IBootstrapPackageHandle Package { get; }

        public int FileCount { get; }

        public long TotalBytes { get; }

        public object BackendToken { get; }

        public bool HasDownload => FileCount > 0 || TotalBytes > 0;
    }

    public sealed class BootstrapDownloadProgress
    {
        public BootstrapDownloadProgress(
            int completedFiles,
            int totalFiles,
            long completedBytes,
            long totalBytes)
        {
            if (completedFiles < 0 || totalFiles < 0 || completedFiles > totalFiles)
            {
                throw new ArgumentOutOfRangeException(nameof(completedFiles));
            }

            if (completedBytes < 0 || totalBytes < 0 || completedBytes > totalBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(completedBytes));
            }

            CompletedFiles = completedFiles;
            TotalFiles = totalFiles;
            CompletedBytes = completedBytes;
            TotalBytes = totalBytes;
        }

        public int CompletedFiles { get; }

        public int TotalFiles { get; }

        public long CompletedBytes { get; }

        public long TotalBytes { get; }
    }

    public interface IBootstrapBackend
    {
        UniTask<BootstrapBackendResult<IBootstrapPackageHandle>> InitializePackageAsync(
            BootstrapPackageProfile package,
            BootstrapProfile profile,
            CancellationToken cancellationToken);

        UniTask<BootstrapBackendResult<BootstrapVersion>> RequestVersionAsync(
            IBootstrapPackageHandle package,
            BootstrapProfile profile,
            BootstrapEndpoint endpoint,
            CancellationToken cancellationToken);

        UniTask<BootstrapBackendResult<bool>> ActivateManifestAsync(
            IBootstrapPackageHandle package,
            BootstrapVersion version,
            BootstrapProfile profile,
            BootstrapEndpoint endpoint,
            CancellationToken cancellationToken);

        UniTask<BootstrapBackendResult<BootstrapDownloadPlan>> CalculateDownloadAsync(
            IBootstrapPackageHandle package,
            BootstrapProfile profile,
            CancellationToken cancellationToken);

        UniTask<BootstrapBackendResult<bool>> DownloadAsync(
            BootstrapDownloadPlan plan,
            Action<BootstrapDownloadProgress> progress,
            CancellationToken cancellationToken);

        UniTask<BootstrapBackendResult<bool>> VerifyAsync(
            BootstrapDownloadPlan plan,
            CancellationToken cancellationToken);

        UniTask<BootstrapBackendResult<BootstrapVersion>> FindVerifiedFallbackAsync(
            IBootstrapPackageHandle package,
            BootstrapProfile profile,
            CancellationToken cancellationToken);

        UniTask ResetAsync(CancellationToken cancellationToken);

        UniTask ShutdownAsync(CancellationToken cancellationToken);
    }

    public interface IBootstrapTelemetrySink
    {
        void Record(BootstrapTelemetryEvent telemetryEvent);
    }

    public interface IBootstrapProgressSink
    {
        void Report(BootstrapProgress progress);
    }

    public interface IBootstrapCodeLoader
    {
        UniTask LoadAsync(BootstrapReadyContext context, CancellationToken cancellationToken);
    }

    public interface IBootstrapGameEntry
    {
        UniTask EnterAsync(BootstrapReadyContext context, CancellationToken cancellationToken);
    }

    public interface IBootstrapClock
    {
        DateTimeOffset UtcNow { get; }
    }

    public interface IBootstrapDelay
    {
        UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public interface IBootstrapDiskSpace
    {
        string GetStorageScope(string packageName);

        long GetAvailableBytes(string packageName);
    }

    public interface IBootstrapConfirmation
    {
        UniTask<bool> ConfirmAsync(BootstrapDownloadPlan plan, CancellationToken cancellationToken);
    }

    public interface IBootstrapNetworkMonitor
    {
        bool IsNetworkReachable { get; }
    }

    public interface IBootstrapRuntimeEnvironment
    {
        bool IsEditor { get; }

        string PlatformName { get; }
    }

    public sealed class NoopBootstrapTelemetrySink : IBootstrapTelemetrySink
    {
        public static readonly NoopBootstrapTelemetrySink Instance = new NoopBootstrapTelemetrySink();

        private NoopBootstrapTelemetrySink()
        {
        }

        public void Record(BootstrapTelemetryEvent telemetryEvent)
        {
        }
    }

    public sealed class NoopBootstrapProgressSink : IBootstrapProgressSink
    {
        public static readonly NoopBootstrapProgressSink Instance = new NoopBootstrapProgressSink();

        private NoopBootstrapProgressSink()
        {
        }

        public void Report(BootstrapProgress progress)
        {
        }
    }

    public sealed class NoopBootstrapCodeLoader : IBootstrapCodeLoader
    {
        public static readonly NoopBootstrapCodeLoader Instance = new NoopBootstrapCodeLoader();

        private NoopBootstrapCodeLoader()
        {
        }

        public UniTask LoadAsync(BootstrapReadyContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }

    public sealed class AcceptBootstrapConfirmation : IBootstrapConfirmation
    {
        public static readonly AcceptBootstrapConfirmation Instance = new AcceptBootstrapConfirmation();

        private AcceptBootstrapConfirmation()
        {
        }

        public UniTask<bool> ConfirmAsync(
            BootstrapDownloadPlan plan,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(true);
        }
    }
}
