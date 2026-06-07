using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 示例启动脚本：挂在空物体即可运行。
    /// </summary>
    public class HelloUIBootstrap : MonoBehaviour
    {
        private bool _isHandlingBackNavigation;

        private async void Start()
        {
            var uiManager = UIManager.Instance;
            uiManager.Init(new CodeViewLoader());
            uiManager.Register<SampleHelloPage>(new UIConfig
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
            uiManager.Register<SecondSamplePage>(new UIConfig
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
            uiManager.Register<VirtualListSamplePage>(new UIConfig
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

            await uiManager.Navigator.PushAsync<SampleHelloPage>("Hello YUIFramework!");
        }

        private async void Update()
        {
            if (_isHandlingBackNavigation || !Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }

            _isHandlingBackNavigation = true;
            try
            {
                await UIManager.Instance.Navigator.BackAsync();
            }
            finally
            {
                _isHandlingBackNavigation = false;
            }
        }
    }
}
