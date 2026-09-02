using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.HotUpdate;

namespace YUIFramework
{
    /// <summary>
    /// 端到端启动示例：演示「热更启动链路 -> UIManager -> 打开首页」的完整接线。
    /// 挂到 LoadScene 的空物体即可运行。
    ///
    /// 为保证在未构建任何 YooAsset 包时也能直接运行，本示例：
    /// - 用 <see cref="HotUpdateLauncher"/> 跑一遍热更链路（无包时会优雅回退，不阻塞启动）。
    /// - UI 部分沿用纯代码的 <see cref="CodeViewLoader"/>，便于开箱即跑。
    ///
    /// 生产接线：把 UIManager.Init 的 loader 换成 <c>new YooAssetLoader()</c>，
    /// 并用 Tools/YUIFramework/HotUpdate 设置运行模式与 CDN。
    /// </summary>
    public sealed class HotUpdateStartupSample : MonoBehaviour
    {
        [SerializeField] private HotUpdatePlayMode playMode = HotUpdatePlayMode.EditorSimulate;
        [SerializeField] private bool useYooAssetLoaderForUI = false;

        private void Start()
        {
            RunAsync(destroyCancellationToken).Forget(Debug.LogException);
        }

        private async UniTask RunAsync(CancellationToken cancellationToken)
        {
            StartupFlowTrace.Begin("HotUpdateStartupSample");

            // 1. 配置运行模式（真机可由渠道 SDK / 编辑器工具覆盖）。
            HotUpdateConfig.PlayMode = playMode;

            // 2. 跑热更启动链路：初始化 YooAsset -> 版本 -> 清单 -> 下载差异。
            //    无包 / 无网时返回 false 并回退，不会抛异常。
            cancellationToken.ThrowIfCancellationRequested();
            bool yooReady = await HotUpdateLauncher.RunAsync();
            Debug.Log($"[HotUpdateStartupSample] 热更完成 yooReady={yooReady}");

            // 3. 初始化 UI 框架。生产用 YooAssetLoader；示例默认用 CodeViewLoader 以便零资源运行。
            IResourceLoader loader = useYooAssetLoaderForUI
                ? new YooAssetLoader()
                : (IResourceLoader)new CodeViewLoader();
            if (!UIManager.Instance.IsInitialized)
            {
                UIManager.Instance.Initialize(
                    loader,
                    UIRootRuntime.CreateOwned());
            }

            // 4. 注册并打开首页（复用现有示例页）。
            UIManager.Instance.Register<SampleHelloPage>(new UIConfig
            {
                Id = "HelloPage",
                PrefabKey = "SampleHelloPage",
                Layer = UILayer.Normal,
                CacheOnClose = true,
                MaxPoolSize = 1,
                FullScreen = true,
            });

            await UIManager.Instance.Navigator.PushAsync<SampleHelloPage>(
                "Hello YUIFramework + YooAsset!",
                cancellationToken: cancellationToken);
            StartupFlowTrace.Complete("home page pushed");
        }
    }
}
