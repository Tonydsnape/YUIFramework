using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 示例启动脚本：挂在空物体即可运行。
    /// </summary>
    public class HelloUIBootstrap : MonoBehaviour
    {
        private async void Start()
        {
            var uiManager = UIManager.Instance;
            uiManager.Init(new CodeViewLoader());
            uiManager.Register<SampleHelloPage>(new UIConfig
            {
                Id = "HelloPage",
                PrefabKey = "SampleHelloPage",
                Layer = UILayer.Normal,
                CacheOnClose = false,
                FullScreen = true,
            });

            await uiManager.OpenAsync<SampleHelloPage>("Hello YUIFramework!");
        }
    }
}
