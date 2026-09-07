using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;

namespace YUIFramework.HotUpdate
{
    [Obsolete("Compose and own BootstrapRunner explicitly in new startup code.")]
    public sealed class GameLauncher : MonoBehaviour, IBootstrapGameEntry
    {
        [Header("Bootstrap profile")]
        [SerializeField] private BootstrapMode playMode = BootstrapMode.EditorSimulate;
        [SerializeField] private string packageName = "DefaultPackage";
        [SerializeField] private string applicationId = "YUIFramework.Application";
        [SerializeField] private string channel = "default";
        [SerializeField] private string applicationVersion = "0";

        [Header("CDN (Host only)")]
        [SerializeField] private string hostServerURL = "http://127.0.0.1:8080";
        [SerializeField] private string fallbackServerURL = "";

        [Header("Policy")]
        [SerializeField] private float timeoutSeconds = 30f;
        [SerializeField] private int maximumAttempts = 3;
        [SerializeField] private float initialRetryBackoffSeconds = 0.25f;
        [SerializeField] private float maximumRetryBackoffSeconds = 4f;
        [SerializeField] private int downloadConcurrency = 8;
        [SerializeField] private long diskSafetyMarginBytes = 67108864;
        [SerializeField] private bool allowVerifiedFallback = true;
        [SerializeField] private bool requireDownloadConfirmation = true;

        [Header("Flow")]
        [SerializeField] private bool autoInitUIManager = true;
        [SerializeField] private GameObject loadingRoot;
        [SerializeField] private BootstrapProgressUI progressUI;
        [SerializeField] private UnityEvent onResourcesReady;

        private BootstrapRunner _runner;
        private YooAssetBootstrapComposition _composition;
        private bool _ownsUIManager;
        private readonly object _launchGate = new object();
        private Task _launchTask;

        public bool IsReady { get; private set; }

        private void Start()
        {
            LaunchAsync(destroyCancellationToken).Forget(HandleLaunchException);
        }

        private void OnDestroy()
        {
            ShutdownAsync().Forget(Debug.LogException);
        }

        public UniTask LaunchAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task task;
            lock (_launchGate)
            {
                if (_composition != null || IsReady)
                {
                    throw new InvalidOperationException(
                        "GameLauncher is already ready. Shut it down before launching again.");
                }

                if (_launchTask == null || _launchTask.IsCompleted)
                {
                    if (_runner == null)
                    {
                        _runner = new BootstrapRunner(
                            new YooAssetBootstrapBackend(),
                            this,
                            progress: progressUI);
                    }

                    _launchTask = LaunchCoreAsync(CreateProfile()).AsTask();
                }

                task = _launchTask;
            }

            return AwaitLaunchAsync(task, cancellationToken);
        }

        private async UniTask LaunchCoreAsync(BootstrapProfile profile)
        {
            IsReady = false;
            if (loadingRoot != null)
            {
                loadingRoot.SetActive(true);
            }

            var result = await _runner.RunAsync(profile);
            if (!result.IsSuccess)
            {
                Debug.LogError(
                    $"[Bootstrap] Startup failed: {result.ErrorCode} at {result.Failure?.Operation}.");
            }
        }

        public UniTask EnterAsync(
            BootstrapReadyContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_composition != null)
            {
                throw new InvalidOperationException(
                    "Resource composition already exists. Reset before launching again.");
            }

            _composition = YooAssetBootstrapComposition.Create(context);
            if (autoInitUIManager && !UIManager.Instance.IsInitialized)
            {
                UIManager.Instance.Initialize(
                    _composition.ResourceService,
                    UIRootRuntime.CreateOwned());
                _ownsUIManager = true;
            }

            onResourcesReady?.Invoke();
            IsReady = true;
            if (loadingRoot != null)
            {
                loadingRoot.SetActive(false);
            }

            return UniTask.CompletedTask;
        }

        private static async UniTask AwaitLaunchAsync(
            Task task,
            CancellationToken cancellationToken)
        {
            await task.AsUniTask().AttachExternalCancellation(cancellationToken);
        }

        private async UniTask ShutdownAsync()
        {
            IsReady = false;
            var failures = new List<Exception>();
            if (_ownsUIManager && UIManager.Instance.IsInitialized)
            {
                try
                {
                    await UIManager.Instance.ShutdownAsync();
                    _ownsUIManager = false;
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
                    "GameLauncher shutdown did not complete cleanly.",
                    failures);
            }
        }

        private BootstrapProfile CreateProfile()
        {
            var resolvedApplicationId = string.IsNullOrWhiteSpace(applicationId)
                ? Application.identifier
                : applicationId;
            var resolvedApplicationVersion = string.IsNullOrWhiteSpace(applicationVersion)
                ? Application.version
                : applicationVersion;
            var primary = string.IsNullOrWhiteSpace(hostServerURL)
                ? null
                : new Uri(hostServerURL.Trim().TrimEnd('/') + "/", UriKind.Absolute);
            var fallback = string.IsNullOrWhiteSpace(fallbackServerURL)
                ? null
                : new Uri(fallbackServerURL.Trim().TrimEnd('/') + "/", UriKind.Absolute);

            return new BootstrapProfile(
                playMode,
                new[] { new BootstrapPackageProfile(packageName) },
                resolvedApplicationId,
                channel,
                resolvedApplicationVersion,
                primary,
                fallback,
                TimeSpan.FromSeconds(timeoutSeconds),
                maximumAttempts,
                TimeSpan.FromSeconds(initialRetryBackoffSeconds),
                TimeSpan.FromSeconds(maximumRetryBackoffSeconds),
                downloadConcurrency,
                allowVerifiedFallback
                    ? BootstrapFallbackPolicy.VerifiedLocalOrBuiltin
                    : BootstrapFallbackPolicy.Disabled,
                diskSafetyMarginBytes,
                requireDownloadConfirmation);
        }

        private static void HandleLaunchException(Exception exception)
        {
            if (!(exception is OperationCanceledException))
            {
                Debug.LogException(exception);
            }
        }
    }
}
