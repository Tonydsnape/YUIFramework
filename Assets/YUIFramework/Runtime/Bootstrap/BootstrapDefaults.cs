using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework.Bootstrap
{
    public sealed class SystemBootstrapClock : IBootstrapClock
    {
        public static readonly SystemBootstrapClock Instance = new SystemBootstrapClock();

        private SystemBootstrapClock()
        {
        }

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    public sealed class UniTaskBootstrapDelay : IBootstrapDelay
    {
        public static readonly UniTaskBootstrapDelay Instance = new UniTaskBootstrapDelay();

        private UniTaskBootstrapDelay()
        {
        }

        public UniTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            return UniTask.Delay(delay, true, PlayerLoopTiming.Update, cancellationToken);
        }
    }

    public sealed class UnityBootstrapNetworkMonitor : IBootstrapNetworkMonitor
    {
        public static readonly UnityBootstrapNetworkMonitor Instance = new UnityBootstrapNetworkMonitor();

        private UnityBootstrapNetworkMonitor()
        {
        }

        public bool IsNetworkReachable =>
            Application.internetReachability != NetworkReachability.NotReachable;
    }

    public sealed class UnityBootstrapRuntimeEnvironment : IBootstrapRuntimeEnvironment
    {
        public static readonly UnityBootstrapRuntimeEnvironment Instance =
            new UnityBootstrapRuntimeEnvironment();

        private UnityBootstrapRuntimeEnvironment()
        {
        }

        public bool IsEditor
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public string PlatformName
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.Android:
                        return "Android";
                    case RuntimePlatform.IPhonePlayer:
                        return "IOS";
                    case RuntimePlatform.WebGLPlayer:
                        return "WebGL";
                    default:
                        return "PC";
                }
            }
        }
    }

    public sealed class UnityBootstrapDiskSpace : IBootstrapDiskSpace
    {
        private readonly string _storagePath;

        public UnityBootstrapDiskSpace(string storagePath = null)
        {
            _storagePath = string.IsNullOrWhiteSpace(storagePath)
                ? Application.persistentDataPath
                : Path.GetFullPath(storagePath);
        }

        public long GetAvailableBytes(string packageName)
        {
            var root = Path.GetPathRoot(_storagePath);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new IOException("Unable to resolve the persistent storage root.");
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }

        public string GetStorageScope(string packageName)
        {
            var root = Path.GetPathRoot(_storagePath);
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new IOException("Unable to resolve the persistent storage root.");
            }

            var fullRoot = Path.GetFullPath(root);
            var normalized = fullRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return normalized.Length == 0 ? fullRoot : normalized;
        }
    }
}
