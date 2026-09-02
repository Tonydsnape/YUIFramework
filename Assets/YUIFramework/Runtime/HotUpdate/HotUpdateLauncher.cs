using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 启动期热更入口。由启动脚本（GameLauncher）在进入业务界面前调用，
    /// 保证联机模式下热更资源在业务加载前就绪。
    ///
    /// 设计原则：
    /// - 只跑一次（<see cref="HasRun"/>）。
    /// - 失败不硬阻塞启动：记录日志后放行，业务侧通过 Resources 回退继续工作。
    /// - 暴露进度/状态/体积事件供 Loading UI 订阅。
    /// </summary>
    public static class HotUpdateLauncher
    {
        public static bool HasRun { get; private set; }

        /// <summary>热更进度回调 (0-1)。</summary>
        public static event Action<float> OnProgress;

        /// <summary>热更阶段文本回调。</summary>
        public static event Action<string> OnStatus;

        /// <summary>联机模式即将下载资源时回调（待下载字节数）。</summary>
        public static event Action<long> OnDownloadSize;

        /// <summary>
        /// 下载确认钩子（可选）。入参为待下载字节数，返回 true 继续 / false 取消。
        /// 不设置则默认直接下载。商业化可在此接"移动网络提示/低存储提示"弹窗。
        /// </summary>
        public static Func<long, UniTask<bool>> ConfirmDownloadHandler;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            HasRun = false;
            ConfirmDownloadHandler = null;
        }

        /// <summary>
        /// 执行启动热更。返回是否走完整 YooAsset 流程（false 表示回退/未启用）。
        /// 无论成败都不会抛出，启动流程可放心 await。
        /// </summary>
        public static async UniTask<bool> RunAsync()
        {
            if (HasRun)
            {
                StartupFlowTrace.Step(
                    "hot-update-launcher.skip",
                    $"alreadyRun=true yooReady={HotUpdateManager.Instance.IsYooAssetReady}");
                return HotUpdateManager.Instance.IsYooAssetReady;
            }

            HasRun = true;

            try
            {
                StartupFlowTrace.Step("hot-update-launcher.begin");
                ReportStatus("Initializing...");
                bool ok = await HotUpdateManager.Instance.RunHotUpdateAsync(OnDownloadProgress, OnConfirmDownload);

                if (!ok)
                {
                    StartupFlowTrace.Warning(
                        "hot-update-launcher.incomplete",
                        $"yooReady={HotUpdateManager.Instance.IsYooAssetReady}");
                    ReportStatus("Starting with built-in resources");
                    ReportProgress(1f);
                    return false;
                }

                ReportStatus("Resources ready");
                ReportProgress(1f);
                StartupFlowTrace.Step(
                    "hot-update-launcher.end",
                    $"yooReady={HotUpdateManager.Instance.IsYooAssetReady}");
                return HotUpdateManager.Instance.IsYooAssetReady;
            }
            catch (Exception e)
            {
                StartupFlowTrace.Error("hot-update-launcher.exception", e.ToString());
                Debug.LogError($"[HotUpdateLauncher] 热更异常，启动继续: {e}");
                ReportStatus("Resource error, using built-in resources");
                ReportProgress(1f);
                return false;
            }
        }

        private static async UniTask<bool> OnConfirmDownload(long bytes)
        {
            StartupFlowTrace.Warning("hot-update-launcher.download-confirm", $"bytes={bytes}");
            OnDownloadSize?.Invoke(bytes);
            ReportStatus($"Update required: {FormatBytes(bytes)}");
            if (ConfirmDownloadHandler != null)
            {
                return await ConfirmDownloadHandler(bytes);
            }

            return true;
        }

        private static void OnDownloadProgress(int current, int total, long currentBytes, long totalBytes)
        {
            float progress = totalBytes > 0 ? (float)currentBytes / totalBytes : 1f;
            ReportProgress(progress);
            ReportStatus($"Downloading {current}/{total}  {FormatBytes(currentBytes)}/{FormatBytes(totalBytes)}");
        }

        /// <summary>字节数格式化为可读字符串 (B/KB/MB/GB)。</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }

            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";
        }

        private static void ReportProgress(float value) => OnProgress?.Invoke(Mathf.Clamp01(value));

        private static void ReportStatus(string status) => OnStatus?.Invoke(status);
    }
}
