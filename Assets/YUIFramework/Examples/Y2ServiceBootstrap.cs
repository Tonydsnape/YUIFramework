using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework.Examples
{
    /// <summary>
    /// Y2 example: owns an injected UI service instead of using UIManager.Instance.
    /// </summary>
    public sealed class Y2ServiceBootstrap : MonoBehaviour
    {
        private IUIService _uiService;

        private void Start()
        {
            RunAsync(destroyCancellationToken).Forget();
        }

        private async UniTask RunAsync(CancellationToken cancellationToken)
        {
            _uiService = new UIManager();
            try
            {
                await _uiService.InitializeAsync(
                    new CodeViewLoader(),
                    cancellationToken: cancellationToken);
                _uiService.Register<SampleHelloPage>(new UIConfig
                {
                    Id = nameof(SampleHelloPage),
                    PrefabKey = "SampleHelloPage",
                    Layer = UILayer.Normal,
                    CacheOnClose = true,
                    MaxPoolSize = 1,
                    FullScreen = true
                });

                await _uiService.Navigator.PushAsync<SampleHelloPage>(
                    "Hello YUIFramework Y2!",
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
