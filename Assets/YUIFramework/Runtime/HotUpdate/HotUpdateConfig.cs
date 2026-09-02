using UnityEngine;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 热更配置（概念性）。集中管理包名、运行模式、CDN 地址与下载参数。
    /// 参考项目里 channel/environment/marker 等多环境矩阵在示例中一律省略，
    /// CDN 确定后只需改这里或用编辑器工具 <c>Tools/YUIFramework/HotUpdate</c> 切换。
    /// </summary>
    public static class HotUpdateConfig
    {
        /// <summary>YooAsset 资源包名，需与打包时的 Package 名一致。</summary>
        public const string DefaultPackageName = "DefaultPackage";

        /// <summary>
        /// 当前运行模式。默认编辑器模拟；真机由启动流程 / 编辑器工具覆盖。
        /// </summary>
        public static HotUpdatePlayMode PlayMode = HotUpdatePlayMode.EditorSimulate;

        /// <summary>是否启用 YooAsset。false 时 <see cref="YooAssetLoader"/> 全部回退 Resources。</summary>
        public static bool UseYooAsset = true;

        /// <summary>CDN 主地址（联机模式）。示例默认指向本地 CDN 占位。</summary>
        public static string HostServerURL = "http://127.0.0.1:8080";

        /// <summary>CDN 备用地址。留空则回退到主地址。</summary>
        public static string FallbackHostServerURL = "http://127.0.0.1:8080";

        /// <summary>下载并发数。</summary>
        public static int DownloadingMaxNumber = 10;

        /// <summary>单文件下载失败重试次数。</summary>
        public static int FailedTryAgain = 3;

        /// <summary>启动期请求远端版本的超时（秒）。弱网/无网超时后回退内置清单。</summary>
        public static int StartupVersionTimeout = 15;

        /// <summary>加载资源清单的超时（秒）。</summary>
        public static int ManifestLoadTimeout = 60;

        /// <summary>是否在远端 URL 中插入平台目录（{host}/{platform}/{file}），匹配 YooAsset 常见 CDN 布局。</summary>
        public static bool AppendPlatformSegment = true;

        /// <summary>配置 CDN 地址。fallback 为空时与 main 相同。</summary>
        public static void ConfigureHost(string main, string fallback = null)
        {
            if (!string.IsNullOrWhiteSpace(main))
            {
                HostServerURL = main.Trim().TrimEnd('/');
            }

            FallbackHostServerURL = string.IsNullOrWhiteSpace(fallback)
                ? HostServerURL
                : fallback.Trim().TrimEnd('/');
        }

        /// <summary>当前平台名（用于 CDN 目录）。</summary>
        public static string PlatformName
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

        /// <summary>解析某个资源文件的远端主地址。</summary>
        public static string GetRemoteMainURL(string fileName)
        {
            return $"{HostServerURL}/{GetRemoteRelativePath(fileName)}";
        }

        /// <summary>解析某个资源文件的远端备用地址。</summary>
        public static string GetRemoteFallbackURL(string fileName)
        {
            return $"{FallbackHostServerURL}/{GetRemoteRelativePath(fileName)}";
        }

        private static string GetRemoteRelativePath(string fileName)
        {
            return AppendPlatformSegment
                ? $"{PlatformName}/{DefaultPackageName}/{fileName}"
                : $"{DefaultPackageName}/{fileName}";
        }
    }
}
