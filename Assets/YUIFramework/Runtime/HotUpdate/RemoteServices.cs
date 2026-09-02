using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 远端资源地址查询服务。YooAsset 在联机（Host）模式下通过它解析每个文件的下载地址。
    /// 地址规则集中在 <see cref="HotUpdateConfig"/>，CDN 确定后只改配置即可。
    /// 采用新版 YooAsset 3.x 的 <see cref="IRemoteService"/>：一次返回按优先级排序的候选地址，
    /// YooAsset 会依次尝试（主 CDN -> 备用 CDN），无需再区分 main/fallback 两个方法。
    /// </summary>
    public sealed class RemoteServices : IRemoteService
    {
        /// <summary>
        /// 更新清单时若需临时从包内（StreamingAssets）读取清单元数据，置 true，
        /// 用于无网回退到内置清单的场景。
        /// </summary>
        public bool UseBuildinManifestSource { get; set; }

        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            if (UseBuildinManifestSource)
            {
                return new List<string> { GetBuildinURL(fileName) };
            }

            return new List<string>
            {
                HotUpdateConfig.GetRemoteMainURL(fileName),
                HotUpdateConfig.GetRemoteFallbackURL(fileName),
            };
        }

        private static string GetBuildinURL(string fileName)
        {
            string url =
                $"{Application.streamingAssetsPath}/yoo/{HotUpdateConfig.DefaultPackageName}/{fileName}";
            if (!url.Contains("://"))
            {
                url = "file://" + url;
            }

            return url;
        }
    }
}
