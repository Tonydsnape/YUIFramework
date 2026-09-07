using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;

namespace YUIFramework
{
    /// <summary>
    /// Resource bootstrap sample. The historical file name is retained so existing scenes keep
    /// their MonoScript reference; the implementation uses only the Y2 Bootstrap APIs.
    /// </summary>
    public sealed class HotUpdateStartupSample : MonoBehaviour, IBootstrapGameEntry
    {
        [SerializeField] private BootstrapMode playMode = BootstrapMode.EditorSimulate;
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private string channel = "default";
        [SerializeField] private string primaryCdn = "http://127.0.0.1:8080";
        [SerializeField] private string fallbackCdn = "";

        private BootstrapRunner _runner;
        private YooAssetBootstrapComposition _composition;
        private UIManager _uiService;

        private void Start()
        {
            RunAsync(destroyCancellationToken).Forget(HandleException);
        }

        private void OnDestroy()
        {
            ShutdownAsync().Forget(Debug.LogException);
        }

        private async UniTask RunAsync(CancellationToken cancellationToken)
        {
            _runner = new BootstrapRunner(
                new YooAssetBootstrapBackend(),
                this);
            var result = await _runner.RunAsync(CreateProfile(), cancellationToken);
            if (!result.IsSuccess)
            {
                Debug.LogError(
                    $"[BootstrapSample] Failed: {result.ErrorCode} at {result.Failure?.Operation}.");
            }
        }

        public async UniTask EnterAsync(
            BootstrapReadyContext context,
            CancellationToken cancellationToken)
        {
            _composition = YooAssetBootstrapComposition.Create(context);
            _uiService = new UIManager();
            await _uiService.InitializeAsync(
                _composition.ResourceService,
                cancellationToken: cancellationToken);

            _uiService.Register<SampleHelloPage>(new UIConfig
            {
                Id = "HelloPage",
                PrefabKey = "SampleHelloPage",
                Layer = UILayer.Normal,
                CacheOnClose = true,
                MaxPoolSize = 1,
                FullScreen = true,
            });
            await _uiService.Navigator.PushAsync<SampleHelloPage>(
                "Hello YUIFramework Bootstrap!",
                cancellationToken: cancellationToken);
        }

        private BootstrapProfile CreateProfile()
        {
            var appId = string.IsNullOrWhiteSpace(Application.identifier)
                ? "YUIFramework.Application"
                : Application.identifier;
            var appVersion = string.IsNullOrWhiteSpace(Application.version)
                ? "0"
                : Application.version;
            return new BootstrapProfile(
                playMode,
                new[] { new BootstrapPackageProfile(packageName) },
                appId,
                channel,
                appVersion,
                ParseUri(primaryCdn),
                ParseUri(fallbackCdn),
                fallbackPolicy: BootstrapFallbackPolicy.VerifiedLocalOrBuiltin);
        }

        private async UniTask ShutdownAsync()
        {
            var failures = new List<Exception>();
            if (_uiService != null && _uiService.IsInitialized)
            {
                try
                {
                    await _uiService.ShutdownAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (_composition != null)
            {
                try
                {
                    await _composition.ShutdownResourceServiceAsync();
                    _composition = null;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (_runner != null)
            {
                try
                {
                    await _runner.ShutdownAsync();
                    _runner = null;
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Bootstrap sample shutdown did not complete cleanly.",
                    failures);
            }
        }

        private static Uri ParseUri(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : new Uri(value.Trim().TrimEnd('/') + "/", UriKind.Absolute);
        }

        private static void HandleException(Exception exception)
        {
            if (!(exception is OperationCanceledException))
            {
                Debug.LogException(exception);
            }
        }
    }
}
