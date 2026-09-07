using System;
using System.Collections.Generic;
using UnityEngine;
using YUIFramework.Bootstrap.YooAsset;
using global::YooAsset;

namespace YUIFramework.HotUpdate
{
    [Obsolete("YooAssetBootstrapBackend owns remote URL resolution.")]
    public sealed class RemoteServices : IRemoteService
    {
        public bool UseBuildinManifestSource { get; set; }

        public IReadOnlyList<string> GetRemoteUrls(string fileName)
        {
            if (UseBuildinManifestSource)
            {
                var path =
                    Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/') +
                    "/yoo/" + HotUpdateConfig.DefaultPackageName + "/" +
                    YooAssetBootstrapUrl.EscapeRelativePath(fileName);
                return new[] { path.Contains("://") ? path : "file://" + path };
            }

            return new[]
            {
                HotUpdateConfig.GetRemoteMainURL(fileName),
                HotUpdateConfig.GetRemoteFallbackURL(fileName),
            };
        }
    }
}
