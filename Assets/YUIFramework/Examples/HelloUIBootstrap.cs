using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Configuration;

namespace YUIFramework
{
    /// <summary>
    /// 示例启动脚本：挂在空物体即可运行。
    /// </summary>
    public class HelloUIBootstrap : MonoBehaviour
    {
        private UIManager _uiService;

        private void Start()
        {
            RunAsync(destroyCancellationToken).Forget(Debug.LogException);
        }

        private async UniTask RunAsync(CancellationToken cancellationToken)
        {
            _uiService = new UIManager();
            var configOwner = new SampleConfigOwner();
            try
            {
                var rootRuntime = UIRootRuntime.CreateOwned();
                await _uiService.InitializeAsync(
                    new CodeViewLoader(),
                    rootRuntime,
                    cancellationToken: cancellationToken);

                await ConfigUIStartup.EnterAsync(configOwner.Service, _uiService,
                    snapshot => SampleUIConfiguration.Map(snapshot, "hello"),
                    async token => { await _uiService.Navigator.PushAsync<SampleHelloPage>(
                        "Hello YUIFramework!", cancellationToken: token); }, cancellationToken);
                await UniTask.WaitUntilCanceled(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                try { if (_uiService.IsInitialized) await _uiService.ShutdownAsync(); }
                finally { await configOwner.ShutdownAsync(); }
            }
        }
    }
}
