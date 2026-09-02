using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 热更资源管理器（概念性精简版）�?
    /// 职责：初始化 YooAsset 资源�?-> 请求版本 -> 更新清单 -> 下载差异 -> 提供资源加载�?
    /// 相比参考项�?67KB �?ResourceManager，这里去掉了 DurableSeed / Atlas / Prefetch /
    /// 多环�?Profile / 严格无回退诊断等业务专用逻辑，只保留可教学的核心链路�?
    ///
    /// 典型用法（由 <see cref="HotUpdateLauncher"/> 驱动）：
    ///   await HotUpdateManager.Instance.RunHotUpdateAsync(onProgress, confirm);
    /// 之后即可 UIManager.Initialize(new YooAssetLoader())�?
    /// </summary>
    public sealed class HotUpdateManager
    {
        private static readonly Lazy<HotUpdateManager> LazyInstance =
            new Lazy<HotUpdateManager>(() => new HotUpdateManager());

        public static HotUpdateManager Instance => LazyInstance.Value;

        private ResourcePackage _package;
        private RemoteServices _remoteServices;
        private bool _initialized;

        private HotUpdateManager()
        {
        }

        /// <summary>当前激活的资源版本号�?/summary>
        public string PackageVersion { get; private set; }

        /// <summary>YooAsset 包是否已初始化�?/summary>
        public bool IsPackageInitialized => _initialized && _package != null;

        /// <summary>资源系统是否就绪（可通过 YooAsset 加载资源）�?/summary>
        public bool IsYooAssetReady => IsPackageInitialized && _package.InitializeStatus == EOperationStatus.Succeeded;

        /// <summary>当前是否以离线（内置清单）会话运行�?/summary>
        public bool IsOfflineSession { get; private set; }

        /// <summary>网络是否可达�?/summary>
        public static bool IsNetworkReachable => Application.internetReachability != NetworkReachability.NotReachable;

        /// <summary>当前使用的资源包（可能为 null）�?/summary>
        public ResourcePackage Package => _package;

        /// <summary>
        /// 一站式启动热更：初始化 -> 请求版本 -> 更新清单 -> 下载差异�?
        /// 关键行为�?
        ///   - 非联机模式：请求版本并激活清单即可（资源已在本地）�?
        ///   - 联机 + 有网：请求远端版本、更新清单、下载差异�?
        ///   - 联机 + 无网/弱网：版本请求超时后回退内置清单，首包直接可玩�?
        /// 任意关键环节失败返回 false；但无网回退成功视为 true（可进游戏）�?
        /// 该方法不抛异常，启动流程可放�?await�?
        /// </summary>
        public async UniTask<bool> RunHotUpdateAsync(
            Action<int, int, long, long> onProgress = null,
            Func<long, UniTask<bool>> confirm = null)
        {
            StartupFlowTrace.Step(
                "resource-hot-update.begin",
                $"mode={HotUpdateConfig.PlayMode} network={Application.internetReachability}");

            if (!await InitializeAsync())
            {
                return false;
            }

            // 非联机模式（编辑器模�?离线）：请求版本并激活清单即可�?
            if (HotUpdateConfig.PlayMode != HotUpdatePlayMode.Host)
            {
                var localVer = await RequestPackageVersionAsync();
                if (string.IsNullOrEmpty(localVer))
                {
                    return false;
                }

                bool activated = await UpdatePackageManifestAsync(localVer);
                IsOfflineSession = activated && HotUpdateConfig.PlayMode == HotUpdatePlayMode.Offline;
                return activated;
            }

            // 联机模式且无网：直接激活随包内置清单�?
            if (!IsNetworkReachable)
            {
                StartupFlowTrace.Warning("resource-hot-update.offline", "activating build-in manifest");
                return await TryActivateBuildinManifestAsync();
            }

            // 联机模式：先请求远端版本（带超时，弱�?无网尽快回退）�?
            string version = await RequestPackageVersionAsync(HotUpdateConfig.StartupVersionTimeout);
            if (string.IsNullOrEmpty(version))
            {
                StartupFlowTrace.Warning(
                    "resource-hot-update.remote-version-missing",
                    "falling back to build-in manifest");
                return await TryActivateBuildinManifestAsync();
            }

            if (!await UpdatePackageManifestAsync(version))
            {
                return false;
            }

            bool downloaded = await DownloadAsync(onProgress, confirm);
            if (!downloaded)
            {
                StartupFlowTrace.Warning("resource-hot-update.download-incomplete", "download cancelled or failed");
                return false;
            }

            IsOfflineSession = false;
            StartupFlowTrace.Step("resource-hot-update.ready", $"version={PackageVersion} offlineSession=false");
            return true;
        }

        /// <summary>初始�?YooAsset 系统与资源包�?/summary>
        public async UniTask<bool> InitializeAsync(string packageName = null)
        {
            if (_initialized)
            {
                StartupFlowTrace.Step("resource-manager.initialize.skip", "alreadyInitialized=true");
                return IsPackageInitialized;
            }

            if (!HotUpdateConfig.UseYooAsset)
            {
                StartupFlowTrace.Warning("resource-manager.initialize.disabled", "UseYooAsset=false");
                return false;
            }

            packageName ??= HotUpdateConfig.DefaultPackageName;
            StartupFlowTrace.Step(
                "resource-manager.initialize.begin",
                $"package={packageName} mode={HotUpdateConfig.PlayMode}");

            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }

            _package = YooAssets.TryGetPackage(packageName, out var package) ? package : YooAssets.CreatePackage(packageName);

            InitializePackageOptions initParameters;
            switch (HotUpdateConfig.PlayMode)
            {
                case HotUpdatePlayMode.EditorSimulate:
                    initParameters = BuildEditorSimulateParameters(packageName);
                    break;
                case HotUpdatePlayMode.Offline:
                    initParameters = BuildOfflineParameters();
                    break;
                case HotUpdatePlayMode.Host:
                    initParameters = BuildHostParameters();
                    break;
                default:
                    throw new NotSupportedException($"未支持的运行模式: {HotUpdateConfig.PlayMode}");
            }

            var initOp = _package.InitializePackageAsync(initParameters);
            await initOp;

            if (initOp.Status != EOperationStatus.Succeeded)
            {
                StartupFlowTrace.Error("resource-manager.initialize.failed", initOp.Error);
                Debug.LogError($"[HotUpdateManager] YooAsset 初始化失�? {initOp.Error}");
                _package = null;
                return false;
            }

            _initialized = true;
            StartupFlowTrace.Step(
                "resource-manager.initialize.end",
                $"package={packageName} status={initOp.Status}");
            return true;
        }

        /// <summary>请求最新资源版本号。timeout 秒内无响应视为失败（返回 null），便于弱网/无网快速回退�?/summary>
        public async UniTask<string> RequestPackageVersionAsync(int timeout = 60)
        {
            if (!IsPackageInitialized)
            {
                return null;
            }

            StartupFlowTrace.Step("resource-version.request.begin", $"timeout={timeout}s");
            var op = _package.RequestPackageVersionAsync(new RequestPackageVersionOptions(true, timeout));
            await op;
            if (op.Status != EOperationStatus.Succeeded)
            {
                StartupFlowTrace.Warning("resource-version.request.failed", op.Error);
                return null;
            }

            PackageVersion = op.PackageVersion;
            StartupFlowTrace.Step("resource-version.request.end", $"version={PackageVersion}");
            return PackageVersion;
        }

        /// <summary>更新资源清单到指定版本�?/summary>
        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion)
        {
            if (!IsPackageInitialized || string.IsNullOrEmpty(packageVersion))
            {
                return false;
            }

            StartupFlowTrace.Step("resource-manifest.update.begin", $"version={packageVersion}");
            var op = _package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(packageVersion, HotUpdateConfig.ManifestLoadTimeout));
            await op;
            if (op.Status != EOperationStatus.Succeeded)
            {
                StartupFlowTrace.Error("resource-manifest.update.failed", op.Error);
                Debug.LogError($"[HotUpdateManager] 更新资源清单失败: {op.Error}");
                return false;
            }

            PackageVersion = packageVersion;
            StartupFlowTrace.Step("resource-manifest.update.end", $"version={PackageVersion}");
            return true;
        }

        /// <summary>
        /// 下载需要热更的资源。onProgress 回调�?已下载文件数, 总文件数, 已下载字�? 总字�?�?
        /// confirm 为下载前体积确认钩子，返�?false 视为取消�?
        /// </summary>
        public async UniTask<bool> DownloadAsync(
            Action<int, int, long, long> onProgress = null,
            Func<long, UniTask<bool>> confirm = null)
        {
            if (!IsYooAssetReady)
            {
                return true; // 非热更模式视为无需下载
            }

            var downloader = _package.CreateResourceDownloader(
                new ResourceDownloaderOptions(
                    HotUpdateConfig.DownloadingMaxNumber, HotUpdateConfig.FailedTryAgain));

            if (downloader.TotalDownloadCount == 0)
            {
                StartupFlowTrace.Step("resource-download.none", "nothing to download");
                return true;
            }

            if (confirm != null)
            {
                bool agreed = await confirm(downloader.TotalDownloadBytes);
                if (!agreed)
                {
                    StartupFlowTrace.Warning(
                        "resource-download.cancelled",
                        $"bytes={downloader.TotalDownloadBytes}");
                    return false;
                }
            }

            downloader.DownloadProgressChanged += args =>
            {
                onProgress?.Invoke(
                    args.CurrentDownloadCount, args.TotalDownloadCount,
                    args.CurrentDownloadBytes, args.TotalDownloadBytes);
            };

            downloader.StartDownload();
            await downloader;

            if (downloader.Status != EOperationStatus.Succeeded)
            {
                StartupFlowTrace.Error("resource-download.failed", downloader.Error);
                Debug.LogError($"[HotUpdateManager] 资源下载失败: {downloader.Error}");
                return false;
            }

            StartupFlowTrace.Step("resource-download.end", "download complete");
            return true;
        }

        /// <summary>查询待下载资源的文件数与字节数。非就绪时返�?(0,0)�?/summary>
        public (int count, long bytes) GetDownloadSize()
        {
            if (!IsYooAssetReady)
            {
                return (0, 0);
            }

            var downloader = _package.CreateResourceDownloader(
                new ResourceDownloaderOptions(
                    HotUpdateConfig.DownloadingMaxNumber, HotUpdateConfig.FailedTryAgain));
            return (downloader.TotalDownloadCount, downloader.TotalDownloadBytes);
        }

        /// <summary>某个资源 location 是否被 YooAsset 清单收录（新版无 CheckLocationValid，改用 GetAssetInfo 判断）。</summary>
        public bool CheckLocationValid(string location)
        {
            if (!IsYooAssetReady || string.IsNullOrEmpty(location))
            {
                return false;
            }

            var info = _package.GetAssetInfo(location);
            return info != null && info.IsValid;
        }

        /// <summary>
        /// 异步加载资源。返�?YooAsset 句柄，调用方负责在不再使用时 Release�?
        /// 未就绪或未收录时返回 null（由 <see cref="YooAssetLoader"/> 决定是否回退 Resources）�?
        /// </summary>
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

            var handle = _package.LoadAssetAsync<T>(location);
            try
            {
                while (!handle.IsDone)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                ReleaseHandleWhenDoneAsync(handle).Forget();
                throw;
            }

            if (handle.Status != EOperationStatus.Succeeded)
            {
                StartupFlowTrace.Warning("resource-load.failed", $"{location}: {handle.Error}");
                handle.Release();
                return null;
            }

            return handle;
        }

        private static async UniTaskVoid ReleaseHandleWhenDoneAsync(AssetHandle handle)
        {
            if (handle == null || !handle.IsValid)
            {
                return;
            }

            try
            {
                if (!handle.IsDone)
                {
                    await handle;
                }
            }
            finally
            {
                if (handle.IsValid)
                {
                    handle.Release();
                }
            }
        }

        private async UniTask<bool> TryActivateBuildinManifestAsync()
        {
            StartupFlowTrace.Step("resource-manifest.build-in.begin");
            string buildinVer = await ReadBuildinPackageVersionAsync();
            if (string.IsNullOrEmpty(buildinVer))
            {
                Debug.LogError("[HotUpdateManager] 远端与内置版本均获取失败，无法进入离线玩法");
                return false;
            }

            if (_remoteServices == null)
            {
                Debug.LogError("[HotUpdateManager] 内置清单回退仅支持 Host 模式的文件系统");
                return false;
            }

            _remoteServices.UseBuildinManifestSource = true;
            try
            {
                if (!await UpdatePackageManifestAsync(buildinVer))
                {
                    return false;
                }
            }
            finally
            {
                _remoteServices.UseBuildinManifestSource = false;
            }

            IsOfflineSession = true;
            StartupFlowTrace.Step("resource-manifest.build-in.end", $"version={buildinVer}");
            Debug.LogWarning($"[HotUpdateManager] 已激活内置清单 {buildinVer}，使用包内资源进入离线玩法");
            return true;
        }

        private static async UniTask<string> ReadBuildinPackageVersionAsync()
        {
            string pkg = HotUpdateConfig.DefaultPackageName;
            string url = $"{Application.streamingAssetsPath}/yoo/{pkg}/{pkg}.version";
            if (!url.Contains("://"))
            {
                url = "file://" + url;
            }

            using (var req = UnityWebRequest.Get(url))
            {
                try
                {
                    await req.SendWebRequest().ToUniTask();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HotUpdateManager] 读取内置版本异常: {e.Message} ({url})");
                    return null;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[HotUpdateManager] 读取内置版本失败: {req.error} ({url})");
                    return null;
                }

                string ver = req.downloadHandler.text?.Trim();
                return string.IsNullOrEmpty(ver) ? null : ver;
            }
        }

        private InitializePackageOptions BuildEditorSimulateParameters(string packageName)
        {
#if UNITY_EDITOR
            var buildResult = EditorSimulateBuildInvoker.Build(
                packageName, (int)EBundleType.VirtualAssetBundle);
            var packageRoot = buildResult.PackageRootDirectory;
            var editorFileSystem = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = editorFileSystem
            };
#else
            return BuildOfflineParameters();
#endif
        }

        private InitializePackageOptions BuildOfflineParameters()
        {
            var buildinFileSystem = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            return new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = buildinFileSystem
            };
        }

        private InitializePackageOptions BuildHostParameters()
        {
            _remoteServices = new RemoteServices();
            var cacheFileSystem = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(_remoteServices);
            var buildinFileSystem = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            return new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = buildinFileSystem,
                CacheFileSystemParameters = cacheFileSystem
            };
        }
    }
}
