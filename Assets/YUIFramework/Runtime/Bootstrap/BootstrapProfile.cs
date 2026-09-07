using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace YUIFramework.Bootstrap
{
    public enum BootstrapMode
    {
        EditorSimulate,
        Offline,
        Host,
    }

    public enum BootstrapFallbackPolicy
    {
        Disabled,
        VerifiedLocalOrBuiltin,
    }

    public sealed class BootstrapPackageProfile : IEquatable<BootstrapPackageProfile>
    {
        public BootstrapPackageProfile(string packageName)
        {
            PackageName = ValidateFileSegment(packageName, nameof(packageName), 128);
        }

        public string PackageName { get; }

        public bool Equals(BootstrapPackageProfile other)
        {
            return other != null &&
                   string.Equals(PackageName, other.PackageName, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as BootstrapPackageProfile);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(PackageName);

        public override string ToString() => PackageName;

        internal static string ValidateIdentifier(string value, string parameterName, int maximumLength)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            value = value.Trim();
            if (value.Length == 0)
            {
                throw new ArgumentException("Value must not be empty or whitespace.", parameterName);
            }

            if (value.Length > maximumLength)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value.Length,
                    $"Value must be no longer than {maximumLength} characters.");
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    throw new ArgumentException("Value must not contain control characters.", parameterName);
                }
            }

            return value;
        }

        internal static string ValidateFileSegment(
            string value,
            string parameterName,
            int maximumLength)
        {
            value = ValidateIdentifier(value, parameterName, maximumLength);
            if (value == "." || value == "..")
            {
                throw new ArgumentException("Value must not be a relative path segment.", parameterName);
            }

            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (!char.IsLetterOrDigit(character) &&
                    character != '.' &&
                    character != '_' &&
                    character != '-' &&
                    character != '+')
                {
                    throw new ArgumentException(
                        "Value may contain only letters, digits, '.', '_', '-', and '+'.",
                        parameterName);
                }
            }

            if (value[value.Length - 1] == '.')
            {
                throw new ArgumentException("Value must not end with '.'.", parameterName);
            }

            var stem = value.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" ||
                stem == "PRN" ||
                stem == "AUX" ||
                stem == "NUL" ||
                stem == "CLOCK$" ||
                (stem.Length == 4 &&
                 (stem.StartsWith("COM", StringComparison.Ordinal) ||
                  stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 stem[3] >= '1' &&
                 stem[3] <= '9'))
            {
                throw new ArgumentException(
                    "Value is a reserved filesystem name.",
                    parameterName);
            }

            return value;
        }
    }

    public sealed class BootstrapProfile : IEquatable<BootstrapProfile>
    {
        public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultInitialRetryBackoff = TimeSpan.FromMilliseconds(250);
        public static readonly TimeSpan DefaultMaximumRetryBackoff = TimeSpan.FromSeconds(4);

        private readonly ReadOnlyCollection<BootstrapPackageProfile> _packages;

        public BootstrapProfile(
            BootstrapMode mode,
            IEnumerable<BootstrapPackageProfile> packages,
            string applicationId,
            string channel,
            string applicationVersion,
            Uri primaryCdn = null,
            Uri fallbackCdn = null,
            TimeSpan? operationTimeout = null,
            int maximumAttempts = 3,
            TimeSpan? initialRetryBackoff = null,
            TimeSpan? maximumRetryBackoff = null,
            int downloadConcurrency = 8,
            BootstrapFallbackPolicy fallbackPolicy = BootstrapFallbackPolicy.Disabled,
            long diskSafetyMarginBytes = 64L * 1024L * 1024L,
            bool requireDownloadConfirmation = true)
        {
            if (!Enum.IsDefined(typeof(BootstrapMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown bootstrap mode.");
            }

            if (!Enum.IsDefined(typeof(BootstrapFallbackPolicy), fallbackPolicy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fallbackPolicy),
                    fallbackPolicy,
                    "Unknown fallback policy.");
            }

            Mode = mode;
            _packages = CopyPackages(packages);
            ApplicationId = BootstrapPackageProfile.ValidateIdentifier(applicationId, nameof(applicationId), 256);
            Channel = BootstrapPackageProfile.ValidateIdentifier(channel, nameof(channel), 128);
            ApplicationVersion = BootstrapPackageProfile.ValidateIdentifier(
                applicationVersion,
                nameof(applicationVersion),
                128);

            PrimaryCdn = NormalizeCdn(primaryCdn, nameof(primaryCdn));
            FallbackCdn = NormalizeCdn(fallbackCdn, nameof(fallbackCdn));
            if (mode == BootstrapMode.Host && PrimaryCdn == null)
            {
                throw new ArgumentException("Host mode requires a primary CDN.", nameof(primaryCdn));
            }

            OperationTimeout = operationTimeout ?? DefaultOperationTimeout;
            if (OperationTimeout <= TimeSpan.Zero || OperationTimeout > TimeSpan.FromMinutes(10))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(operationTimeout),
                    OperationTimeout,
                    "Operation timeout must be greater than zero and no longer than ten minutes.");
            }

            if (maximumAttempts < 1 || maximumAttempts > 10)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumAttempts),
                    maximumAttempts,
                    "Maximum attempts must be between 1 and 10.");
            }

            MaximumAttempts = maximumAttempts;
            InitialRetryBackoff = initialRetryBackoff ?? DefaultInitialRetryBackoff;
            MaximumRetryBackoff = maximumRetryBackoff ?? DefaultMaximumRetryBackoff;
            if (InitialRetryBackoff < TimeSpan.Zero || InitialRetryBackoff > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialRetryBackoff),
                    InitialRetryBackoff,
                    "Initial retry backoff must be between zero and one minute.");
            }

            if (MaximumRetryBackoff < InitialRetryBackoff ||
                MaximumRetryBackoff > TimeSpan.FromMinutes(5))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRetryBackoff),
                    MaximumRetryBackoff,
                    "Maximum retry backoff must be at least the initial backoff and no longer than five minutes.");
            }

            if (downloadConcurrency < 1 || downloadConcurrency > 64)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(downloadConcurrency),
                    downloadConcurrency,
                    "Download concurrency must be between 1 and 64.");
            }

            if (diskSafetyMarginBytes < 0 || diskSafetyMarginBytes > 1024L * 1024L * 1024L * 1024L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(diskSafetyMarginBytes),
                    diskSafetyMarginBytes,
                    "Disk safety margin must be between zero and one tebibyte.");
            }

            DownloadConcurrency = downloadConcurrency;
            FallbackPolicy = fallbackPolicy;
            DiskSafetyMarginBytes = diskSafetyMarginBytes;
            RequireDownloadConfirmation = requireDownloadConfirmation;
        }

        public BootstrapMode Mode { get; }

        public IReadOnlyList<BootstrapPackageProfile> Packages => _packages;

        public string PackageName => _packages[0].PackageName;

        public string ApplicationId { get; }

        public string Channel { get; }

        public string ApplicationVersion { get; }

        public Uri PrimaryCdn { get; }

        public Uri FallbackCdn { get; }

        public TimeSpan OperationTimeout { get; }

        public int MaximumAttempts { get; }

        public TimeSpan InitialRetryBackoff { get; }

        public TimeSpan MaximumRetryBackoff { get; }

        public int DownloadConcurrency { get; }

        public BootstrapFallbackPolicy FallbackPolicy { get; }

        public long DiskSafetyMarginBytes { get; }

        public bool RequireDownloadConfirmation { get; }

        public bool Equals(BootstrapProfile other)
        {
            if (other == null ||
                Mode != other.Mode ||
                !string.Equals(ApplicationId, other.ApplicationId, StringComparison.Ordinal) ||
                !string.Equals(Channel, other.Channel, StringComparison.Ordinal) ||
                !string.Equals(ApplicationVersion, other.ApplicationVersion, StringComparison.Ordinal) ||
                !Equals(PrimaryCdn, other.PrimaryCdn) ||
                !Equals(FallbackCdn, other.FallbackCdn) ||
                OperationTimeout != other.OperationTimeout ||
                MaximumAttempts != other.MaximumAttempts ||
                InitialRetryBackoff != other.InitialRetryBackoff ||
                MaximumRetryBackoff != other.MaximumRetryBackoff ||
                DownloadConcurrency != other.DownloadConcurrency ||
                FallbackPolicy != other.FallbackPolicy ||
                DiskSafetyMarginBytes != other.DiskSafetyMarginBytes ||
                RequireDownloadConfirmation != other.RequireDownloadConfirmation ||
                _packages.Count != other._packages.Count)
            {
                return false;
            }

            for (var i = 0; i < _packages.Count; i++)
            {
                if (!_packages[i].Equals(other._packages[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as BootstrapProfile);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Mode;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ApplicationId);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Channel);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ApplicationVersion);
                hash = (hash * 397) ^ (PrimaryCdn?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (FallbackCdn?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ OperationTimeout.GetHashCode();
                hash = (hash * 397) ^ MaximumAttempts;
                hash = (hash * 397) ^ InitialRetryBackoff.GetHashCode();
                hash = (hash * 397) ^ MaximumRetryBackoff.GetHashCode();
                hash = (hash * 397) ^ DownloadConcurrency;
                hash = (hash * 397) ^ (int)FallbackPolicy;
                hash = (hash * 397) ^ DiskSafetyMarginBytes.GetHashCode();
                hash = (hash * 397) ^ RequireDownloadConfirmation.GetHashCode();
                for (var i = 0; i < _packages.Count; i++)
                {
                    hash = (hash * 397) ^ _packages[i].GetHashCode();
                }

                return hash;
            }
        }

        private static ReadOnlyCollection<BootstrapPackageProfile> CopyPackages(
            IEnumerable<BootstrapPackageProfile> packages)
        {
            if (packages == null)
            {
                throw new ArgumentNullException(nameof(packages));
            }

            var copy = new List<BootstrapPackageProfile>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var package in packages)
            {
                if (package == null)
                {
                    throw new ArgumentException("Package collection must not contain null.", nameof(packages));
                }

                if (!names.Add(package.PackageName))
                {
                    throw new ArgumentException(
                        $"Package \"{package.PackageName}\" is registered more than once.",
                        nameof(packages));
                }

                copy.Add(package);
            }

            if (copy.Count == 0)
            {
                throw new ArgumentException("At least one package is required.", nameof(packages));
            }

            if (copy.Count > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(packages), "At most 32 packages are supported.");
            }

            return new ReadOnlyCollection<BootstrapPackageProfile>(copy);
        }

        private static Uri NormalizeCdn(Uri value, string parameterName)
        {
            if (value == null)
            {
                return null;
            }

            if (!value.IsAbsoluteUri ||
                (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("CDN must be an absolute HTTP or HTTPS URI.", parameterName);
            }

            if (!string.IsNullOrEmpty(value.UserInfo) ||
                !string.IsNullOrEmpty(value.Query) ||
                !string.IsNullOrEmpty(value.Fragment))
            {
                throw new ArgumentException(
                    "CDN must not contain credentials, query parameters, or fragments.",
                    parameterName);
            }

            var builder = new UriBuilder(value)
            {
                Path = value.AbsolutePath.TrimEnd('/') + "/",
                Query = string.Empty,
                Fragment = string.Empty,
            };
            return builder.Uri;
        }
    }
}
