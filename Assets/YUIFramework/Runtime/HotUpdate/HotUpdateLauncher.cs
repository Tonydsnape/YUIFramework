using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Use an injected BootstrapRunner instance.")]
    public static class HotUpdateLauncher
    {
        private static Action<float> _progress;
        private static Action<string> _status;
        private static Action<long> _downloadSize;
        private static Action<BootstrapProgress> _structuredProgress;
        private static Func<long, UniTask<bool>> _confirmDownloadHandler;

        public static bool HasRun => LegacyBootstrapRuntime.Current.LastResult != null;

        public static BootstrapRunResult LastResult =>
            LegacyBootstrapRuntime.Current.LastResult;

        public static event Action<float> OnProgress
        {
            add => _progress += value;
            remove => _progress -= value;
        }

        public static event Action<string> OnStatus
        {
            add => _status += value;
            remove => _status -= value;
        }

        public static event Action<long> OnDownloadSize
        {
            add => _downloadSize += value;
            remove => _downloadSize -= value;
        }

        internal static event Action<BootstrapProgress> StructuredProgress
        {
            add => _structuredProgress += value;
            remove => _structuredProgress -= value;
        }

        public static Func<long, UniTask<bool>> ConfirmDownloadHandler
        {
            get => _confirmDownloadHandler;
            set => _confirmDownloadHandler = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            _progress = null;
            _status = null;
            _downloadSize = null;
            _structuredProgress = null;
            _confirmDownloadHandler = null;
        }

        public static async UniTask<bool> RunAsync(
            CancellationToken cancellationToken = default)
        {
            var result = await LegacyBootstrapRuntime.Current.RunOnceAsync(cancellationToken);
            return result.IsSuccess;
        }

        public static UniTask ResetAsync(CancellationToken cancellationToken = default)
        {
            return LegacyBootstrapRuntime.Current.ResetAsync(cancellationToken);
        }

        public static UniTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            return LegacyBootstrapRuntime.Current.ShutdownAsync(cancellationToken);
        }

        internal static void Publish(BootstrapProgress progress)
        {
            InvokeSafely(_structuredProgress, progress);
            InvokeSafely(_progress, Mathf.Clamp01(progress.Normalized));
            InvokeSafely(_status, progress.State.ToString());
            if (progress.State == BootstrapState.AwaitingConfirmation &&
                progress.TotalBytes > 0)
            {
                InvokeSafely(_downloadSize, progress.TotalBytes);
            }
        }

        internal static void PublishDownloadSize(long bytes)
        {
            InvokeSafely(_downloadSize, bytes);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024d && unit < units.Length - 1)
            {
                size /= 1024d;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        private static void InvokeSafely<T>(Action<T> handlers, T value)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
