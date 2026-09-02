using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 示例启动脚本：挂在空物体即可运行。
    /// </summary>
    public class HelloUIBootstrap : MonoBehaviour
    {
        private IUIService _uiService;

        private void Start()
        {
            RunAsync(destroyCancellationToken).Forget(Debug.LogException);
        }

        private async UniTask RunAsync(CancellationToken cancellationToken)
        {
            _uiService = new UIManager();
            try
            {
                var rootRuntime = UIRootRuntime.CreateOwned();
                await _uiService.InitializeAsync(
                    new CodeViewLoader(),
                    rootRuntime,
                    cancellationToken: cancellationToken);

                _uiService.Register<SampleHelloPage>(new UIConfig
                {
                    Id = "HelloPage",
                    PrefabKey = "SampleHelloPage",
                    Layer = UILayer.Normal,
                    CacheOnClose = true,
                    MaxPoolSize = 1,
                    FullScreen = true,
                    UseTransition = true,
                    TransitionType = UITransitionType.Fade,
                    ShowDuration = 0.2f,
                    HideDuration = 0.15f,
                });
                _uiService.Register<SecondSamplePage>(new UIConfig
                {
                    Id = "SecondSamplePage",
                    PrefabKey = "SecondSamplePage",
                    Layer = UILayer.Normal,
                    CacheOnClose = false,
                    FullScreen = true,
                    UseTransition = true,
                    TransitionType = UITransitionType.SlideLeft,
                    ShowDuration = 0.25f,
                    HideDuration = 0.2f,
                    SlideDistance = 900f,
                });
                _uiService.Register<VirtualListSamplePage>(new UIConfig
                {
                    Id = "VirtualListSamplePage",
                    PrefabKey = "VirtualListSamplePage",
                    Layer = UILayer.Normal,
                    CacheOnClose = true,
                    MaxPoolSize = 1,
                    FullScreen = true,
                    UseTransition = true,
                    TransitionType = UITransitionType.Scale,
                    ShowDuration = 0.2f,
                    HideDuration = 0.15f,
                    StartScale = 0.92f,
                });
                _uiService.Register<MvvmSamplePage>(new UIConfig
                {
                    Id = "MvvmSamplePage",
                    PrefabKey = "MvvmSamplePage",
                    Layer = UILayer.Normal,
                    CacheOnClose = true,
                    MaxPoolSize = 1,
                    FullScreen = true,
                    UseTransition = true,
                    TransitionType = UITransitionType.Fade,
                    ShowDuration = 0.18f,
                    HideDuration = 0.15f,
                });

                await _uiService.Navigator.PushAsync<SampleHelloPage>(
                    "Hello YUIFramework!",
                    cancellationToken: cancellationToken);
                await UniTask.WaitUntilCanceled(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (_uiService.IsInitialized)
                {
                    await _uiService.ShutdownAsync();
                }
            }
        }
    }
}
