using System;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use an immutable YUIFramework.Bootstrap.BootstrapProfile.")]
    public static class HotUpdateConfig
    {
        public const string DefaultPackageName = "DefaultPackage";

        public static HotUpdatePlayMode PlayMode
        {
            get => (HotUpdatePlayMode)LegacyBootstrapRuntime.Current.Profile.Mode;
            set => LegacyBootstrapRuntime.Current.SetMode((BootstrapMode)value);
        }

        public static bool UseYooAsset
        {
            get => true;
            set
            {
                if (!value)
                {
                    throw new NotSupportedException(
                        "Y2 bootstrap uses YooAsset as its only production resource backend.");
                }
            }
        }

        public static string HostServerURL
        {
            get => LegacyBootstrapRuntime.Current.Profile.PrimaryCdn?.AbsoluteUri ?? string.Empty;
            set => ConfigureHost(value, FallbackHostServerURL);
        }

        public static string FallbackHostServerURL
        {
            get => LegacyBootstrapRuntime.Current.Profile.FallbackCdn?.AbsoluteUri ?? string.Empty;
            set => ConfigureHost(HostServerURL, value);
        }

        public static int DownloadingMaxNumber
        {
            get => LegacyBootstrapRuntime.Current.Profile.DownloadConcurrency;
            set => LegacyBootstrapRuntime.Current.SetDownloadConcurrency(value);
        }

        public static int FailedTryAgain
        {
            get => LegacyBootstrapRuntime.Current.Profile.MaximumAttempts - 1;
            set => LegacyBootstrapRuntime.Current.SetMaximumAttempts(checked(value + 1));
        }

        public static int StartupVersionTimeout
        {
            get => (int)Math.Ceiling(
                LegacyBootstrapRuntime.Current.Profile.OperationTimeout.TotalSeconds);
            set => LegacyBootstrapRuntime.Current.SetTimeout(TimeSpan.FromSeconds(value));
        }

        public static int ManifestLoadTimeout
        {
            get => StartupVersionTimeout;
            set => StartupVersionTimeout = value;
        }

        public static bool AppendPlatformSegment
        {
            get => true;
            set
            {
                if (!value)
                {
                    throw new NotSupportedException(
                        "Y2 bootstrap always scopes CDN paths by application, channel, version, platform, and package.");
                }
            }
        }

        public static string PlatformName =>
            UnityBootstrapRuntimeEnvironment.Instance.PlatformName;

        public static void ConfigureHost(string main, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(main))
            {
                throw new ArgumentException("Primary CDN must not be empty.", nameof(main));
            }

            var primaryUri = new Uri(main.Trim().TrimEnd('/') + "/", UriKind.Absolute);
            var fallbackUri = string.IsNullOrWhiteSpace(fallback)
                ? null
                : new Uri(fallback.Trim().TrimEnd('/') + "/", UriKind.Absolute);
            LegacyBootstrapRuntime.Current.SetCdn(primaryUri, fallbackUri);
        }

        public static string GetRemoteMainURL(string fileName)
        {
            var profile = LegacyBootstrapRuntime.Current.Profile;
            return YooAssetBootstrapUrl.BuildCdnUrl(
                profile,
                profile.PackageName,
                PlatformName,
                fileName,
                BootstrapEndpoint.Primary);
        }

        public static string GetRemoteFallbackURL(string fileName)
        {
            var profile = LegacyBootstrapRuntime.Current.Profile;
            if (profile.FallbackCdn == null)
            {
                return GetRemoteMainURL(fileName);
            }

            return YooAssetBootstrapUrl.BuildCdnUrl(
                profile,
                profile.PackageName,
                PlatformName,
                fileName,
                BootstrapEndpoint.Fallback);
        }
    }
}
