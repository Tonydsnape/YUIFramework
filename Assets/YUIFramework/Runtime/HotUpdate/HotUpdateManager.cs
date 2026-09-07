using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;
using global::YooAsset;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use BootstrapRunner and YooAssetBootstrapBackend through dependency injection.")]
    public sealed class HotUpdateManager
    {
        private static readonly HotUpdateManager Facade = new HotUpdateManager();

        private HotUpdateManager()
        {
        }

        public static HotUpdateManager Instance => Facade;

        public string PackageVersion =>
            LegacyBootstrapRuntime.Current.PrimaryPackage?.ActiveVersion ?? string.Empty;

        public bool IsPackageInitialized =>
            LegacyBootstrapRuntime.Current.PrimaryPackage != null;

        public bool IsYooAssetReady =>
            LegacyBootstrapRuntime.Current.LastResult?.IsSuccess == true;

        public bool IsOfflineSession =>
            LegacyBootstrapRuntime.Current.LastResult?.Degraded == true;

        public static bool IsNetworkReachable =>
            UnityBootstrapNetworkMonitor.Instance.IsNetworkReachable;

        public ResourcePackage Package =>
            LegacyBootstrapRuntime.Current.PrimaryPackage?.Package;

        public async UniTask<bool> RunHotUpdateAsync(
            Action<int, int, long, long> onProgress = null,
            Func<long, UniTask<bool>> confirm = null)
        {
            void Report(BootstrapProgress value)
            {
                if (value.State == BootstrapState.Downloading)
                {
                    onProgress?.Invoke(
                        value.CompletedFiles,
                        value.TotalFiles,
                        value.CompletedBytes,
                        value.TotalBytes);
                }
            }

            if (onProgress != null)
            {
                HotUpdateLauncher.StructuredProgress += Report;
            }

            LegacyBootstrapRuntime.Current.SetConfirmationOverride(confirm);
            try
            {
                return await HotUpdateLauncher.RunAsync();
            }
            finally
            {
                LegacyBootstrapRuntime.Current.SetConfirmationOverride(null);
                if (onProgress != null)
                {
                    HotUpdateLauncher.StructuredProgress -= Report;
                }
            }
        }

        public UniTask<bool> InitializeAsync(string packageName = null)
        {
            ValidatePackageName(packageName);
            return RunHotUpdateAsync();
        }

        public async UniTask<string> RequestPackageVersionAsync(int timeout = 60)
        {
            var succeeded = await RunHotUpdateAsync();
            return succeeded ? PackageVersion : null;
        }

        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion)
        {
            if (string.IsNullOrWhiteSpace(packageVersion))
            {
                throw new ArgumentException("Package version must not be empty.", nameof(packageVersion));
            }

            var succeeded = await RunHotUpdateAsync();
            return succeeded &&
                   string.Equals(PackageVersion, packageVersion, StringComparison.Ordinal);
        }

        public UniTask<bool> DownloadAsync(
            Action<int, int, long, long> onProgress = null,
            Func<long, UniTask<bool>> confirm = null)
        {
            return RunHotUpdateAsync(onProgress, confirm);
        }

        public (int count, long bytes) GetDownloadSize()
        {
            throw new NotSupportedException(
                "The compatibility facade cannot expose a stable pre-run plan. Observe BootstrapProgress instead.");
        }

        public bool CheckLocationValid(string location)
        {
            if (Package == null || string.IsNullOrWhiteSpace(location))
            {
                return false;
            }

            var info = Package.GetAssetInfo(location);
            return info != null && info.IsValid;
        }

        public async UniTask<AssetHandle> LoadAssetAsync<T>(
            string location,
            CancellationToken cancellationToken = default)
            where T : UnityEngine.Object
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CheckLocationValid(location))
            {
                return null;
            }

            var handle = Package.LoadAssetAsync<T>(location);
            var succeeded = await WaitForHandleAsync(
                () => handle.IsDone,
                () => handle.IsValid,
                () => handle.Status == EOperationStatus.Succeeded,
                handle.Release,
                cancellationToken);
            return succeeded ? handle : null;
        }

        internal static async UniTask<bool> WaitForHandleAsync(
            Func<bool> isDone,
            Func<bool> isValid,
            Func<bool> succeeded,
            Action release,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!isDone())
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                ReleaseHandleWhenDoneAsync(isDone, isValid, release)
                    .Forget(Debug.LogException);
                throw;
            }

            if (succeeded())
            {
                return true;
            }

            if (isValid())
            {
                release();
            }

            return false;
        }

        private static async UniTask ReleaseHandleWhenDoneAsync(
            Func<bool> isDone,
            Func<bool> isValid,
            Action release)
        {
            while (!isDone())
            {
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (isValid())
            {
                release();
            }
        }

        private static void ValidatePackageName(string packageName)
        {
            if (!string.IsNullOrWhiteSpace(packageName) &&
                !string.Equals(
                    packageName,
                    LegacyBootstrapRuntime.Current.Profile.PackageName,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "Configure packages through BootstrapProfile before running the compatibility facade.");
            }
        }
    }
}
