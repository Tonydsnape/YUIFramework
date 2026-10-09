using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Configuration;

namespace YUIFramework.Examples
{
    /// <summary>
    /// Y2 example: owns an injected UI service instead of using UIManager.Instance.
    /// </summary>
    public sealed class Y2ServiceBootstrap : MonoBehaviour
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
                await _uiService.InitializeAsync(
                    new CodeViewLoader(),
                    cancellationToken: cancellationToken);
                await ConfigUIStartup.EnterAsync(configOwner.Service, _uiService,
                    snapshot => SampleUIConfiguration.Map(snapshot, "y2"), async token =>
                    {
                        await _uiService.PrewarmRegisteredAsync(cancellationToken: token);
                        await _uiService.Navigator.PushAsync<SampleHelloPage>(
                            "Hello YUIFramework Y2!", cancellationToken: token);
                    }, cancellationToken);
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
