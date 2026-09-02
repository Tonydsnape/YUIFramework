using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 示例启动器：串联「设置运行模式 -> 启动热更(带 Loading UI) -> 初始化 UIManager -> 通知业务」。
    /// 挂在 LoadScene 的空物体上即可。业务层通过 <see cref="onResourcesReady"/> 注册并打开首页，
    /// 从而把热更资源系统与现有 UI 框架解耦。
    /// </summary>
    public sealed class GameLauncher : MonoBehaviour
    {
        [Header("运行模式")]
        [SerializeField] private HotUpdatePlayMode playMode = HotUpdatePlayMode.EditorSimulate;
        [SerializeField] private bool useYooAsset = true;

        [Header("CDN（仅 Host 模式）")]
        [SerializeField] private string hostServerURL = "http://127.0.0.1:8080";
        [SerializeField] private string fallbackServerURL = "";

        [Header("流程")]
        [Tooltip("热更完成后是否自动初始化 UIManager")]
        [SerializeField] private bool autoInitUIManager = true;

        [Tooltip("热更期间显示、完成后隐藏的 Loading 根节点（可空）")]
        [SerializeField] private GameObject loadingRoot;

        [Header("就绪回调")]
        [Tooltip("资源与 UIManager 就绪后触发；业务层在此注册并打开首页")]
        [SerializeField] private UnityEvent onResourcesReady;

        /// <summary>资源系统是否已就绪。</summary>
        public bool IsReady { get; private set; }

        private void Start()
        {
            LaunchAsync(destroyCancellationToken).Forget(Debug.LogException);
        }

        /// <summary>执行完整启动链路。可在业务侧手动调用（例如重试）。</summary>
        public async UniTask LaunchAsync(CancellationToken cancellationToken = default)
        {
            StartupFlowTrace.Begin($"GameLauncher mode={playMode}");
            ApplyConfig();

            if (loadingRoot != null)
            {
                loadingRoot.SetActive(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await HotUpdateLauncher.RunAsync();

            if (autoInitUIManager)
            {
                if (!UIManager.Instance.IsInitialized)
                {
                    UIManager.Instance.Initialize(
                        new YooAssetLoader(),
                        UIRootRuntime.CreateOwned());
                }

                StartupFlowTrace.Step("game-launcher.uimanager-ready");
            }

            IsReady = true;
            onResourcesReady?.Invoke();

            if (loadingRoot != null)
            {
                loadingRoot.SetActive(false);
            }

            StartupFlowTrace.Complete($"yooReady={HotUpdateManager.Instance.IsYooAssetReady}");
        }

        private void ApplyConfig()
        {
            HotUpdateConfig.PlayMode = playMode;
            HotUpdateConfig.UseYooAsset = useYooAsset;
            if (playMode == HotUpdatePlayMode.Host)
            {
                HotUpdateConfig.ConfigureHost(hostServerURL, fallbackServerURL);
            }
        }
    }
}
