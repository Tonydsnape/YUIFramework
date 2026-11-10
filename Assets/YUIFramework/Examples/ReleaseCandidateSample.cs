using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework.Bootstrap;
using YUIFramework.Bootstrap.YooAsset;
using YUIFramework.Localization;

namespace YUIFramework
{
    [Serializable]
    public sealed class ReleaseCandidateSmokeReport
    {
        public string Status, StartedUtc, EndedUtc, Platform, Unity, Marker, Error, Screenshot;
        public string[] Checks;
        public bool BootstrapReady, UiShutdown, SceneRestored, TemplatesDestroyed;
    }

    public sealed class ReleaseCandidateSample : MonoBehaviour, IBootstrapGameEntry
    {
        public const string PackageName = "YUIReleaseCandidate";
        public const string MarkerAddress = "RcMarker";
        public const string ApplicationId = "com.yuiframework.rc.stage16";
        [SerializeField] private bool automaticSmoke;
        [SerializeField] private bool quitAfterSmoke;
        private Task _run = Task.CompletedTask;
        private Task _cleanup;
        private CancellationTokenSource _lifetime;
        private bool _stopping;
        private BootstrapRunner _runner;
        private YooAssetBootstrapComposition _composition;
        private SampleConfigOwner _config;
        private TextLocalizationService _text;
        private CodeViewLoader _views;
        private UIManager _ui;
        private ReleaseCandidateFlow _flow;
        public ReleaseCandidateSmokeReport Report { get; } = new ReleaseCandidateSmokeReport();
        public Task Completion => _run;
        public static string SmokeOutputDirectory
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var directory = activity.Call<AndroidJavaObject>("getCacheDir");
                return directory.Call<string>("getCanonicalPath");
#else
                return Application.temporaryCachePath;
#endif
            }
        }

        public void ConfigureSmoke(bool quit) { automaticSmoke = true; quitAfterSmoke = quit; }
        private void Start()
        {
            if (_stopping) return;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            _run = RunAsync(_lifetime.Token).AsTask();
            _run.AsUniTask().Forget(Debug.LogException);
        }
        private void OnDestroy() => CleanupAsync().Forget(Debug.LogException);

        private async UniTask RunAsync(CancellationToken token)
        {
            Report.StartedUtc = DateTime.UtcNow.ToString("O");
            Report.Platform = Application.platform.ToString(); Report.Unity = Application.unityVersion;
            Exception failure = null;
            try
            {
                _runner = new BootstrapRunner(new YooAssetBootstrapBackend(), this);
                var mode = Application.isEditor ? BootstrapMode.EditorSimulate : BootstrapMode.Offline;
                var profile = new BootstrapProfile(mode, new[] { new BootstrapPackageProfile(PackageName) },
                    ApplicationId, "rc-local", Application.version);
                var result = await _runner.RunAsync(profile, token);
                token.ThrowIfCancellationRequested();
                if (!result.IsSuccess) throw new InvalidOperationException("RC resource bootstrap failed: " +
                    result.ErrorCode + " / " + result.Failure?.Reason);
                if (automaticSmoke)
                {
                    using var smoke = CancellationTokenSource.CreateLinkedTokenSource(token);
                    smoke.CancelAfterSlim(TimeSpan.FromSeconds(30));
                    Report.Checks = await _flow.SmokeAsync(smoke.Token);
                    if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                        Report.Screenshot = "Unavailable: Null graphics device";
                    else
                    {
                        await UniTask.WaitForEndOfFrame(this, smoke.Token);
                        var image = ScreenCapture.CaptureScreenshotAsTexture();
                        if (!image) throw new InvalidOperationException("The graphics device did not return a screenshot.");
                        try
                        {
                            Directory.CreateDirectory(SmokeOutputDirectory);
                            File.WriteAllBytes(Path.Combine(SmokeOutputDirectory, "yui-rc-ui.png"), image.EncodeToPNG());
                        }
                        finally { Destroy(image); }
                        Report.Screenshot = "yui-rc-ui.png";
                    }
                    Report.Status = "Passed";
                }
                else await UniTask.WaitUntilCanceled(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { Report.Status = "Canceled"; }
            catch (Exception error)
            { failure = error; Report.Status = "Failed"; Report.Error = error.ToString(); }
            finally
            {
                if (automaticSmoke || failure != null)
                {
                    try { await ReleaseOwnedAsync(); }
                    catch (Exception cleanup)
                    {
                        failure = failure == null ? cleanup : new AggregateException(failure, cleanup);
                        Report.Status = "Failed"; Report.Error = failure.ToString();
                    }
                    Report.EndedUtc = DateTime.UtcNow.ToString("O");
                    Directory.CreateDirectory(SmokeOutputDirectory);
                    File.WriteAllText(Path.Combine(SmokeOutputDirectory, "yui-rc-smoke.json"), JsonUtility.ToJson(Report, true));
                    Debug.Log("[YUI-RC] " + JsonUtility.ToJson(Report));
                    if (quitAfterSmoke) Application.Quit(failure == null ? 0 : 1);
                }
            }
            if (failure != null) throw failure;
        }

        public async UniTask EnterAsync(BootstrapReadyContext ready, CancellationToken token)
        {
            Report.BootstrapReady = true;
            _composition = YooAssetBootstrapComposition.Create(ready);
            using (var marker = await _composition.ResourceService.LoadAssetAsync<TextAsset>(
                UIResourceKey.Of<TextAsset>(MarkerAddress, PackageName), token))
            {
                Report.Marker = marker.Asset.text.Trim();
                if (Report.Marker != "YUIFramework RC resource ready") throw new InvalidOperationException("Unexpected RC package marker.");
            }
            _config = new SampleConfigOwner();
            var snapshot = await _config.Service.InitializeAsync(token);
            _text = new TextLocalizationService(_config.Service, "en");
            _views = new CodeViewLoader();
            _ui = new UIManager();
            await _ui.InitializeAsync(_views, cancellationToken: token);
            _flow = new ReleaseCandidateFlow(_ui, _text);
            await _flow.EnterAsync(snapshot, token);
        }

        public UniTask CleanupAsync()
        {
            _stopping = true;
            _lifetime?.Cancel();
            _cleanup ??= DrainAndReleaseAsync().AsTask();
            return _cleanup.AsUniTask();
        }
        private async UniTask DrainAndReleaseAsync()
        {
            try { await _run; }
            catch (OperationCanceledException) { }
            finally
            {
                try { await ReleaseOwnedAsync(); }
                finally { _lifetime?.Dispose(); _lifetime = null; }
            }
        }
        private async UniTask ReleaseOwnedAsync()
        {
            var errors = new List<Exception>();
            async UniTask Attempt(Func<UniTask> action)
            { try { await action(); } catch (Exception error) { errors.Add(error); } }
            if (_ui != null && _ui.IsInitialized) await Attempt(() => _ui.ShutdownAsync());
            Report.UiShutdown = _ui == null || !_ui.IsInitialized;
            if (_flow != null)
            {
                await Attempt(() => _flow.RestoreScenesAsync());
                Report.SceneRestored = errors.Count == 0;
                try { _flow.Dispose(); } catch (Exception error) { errors.Add(error); }
                _flow = null;
            }
            if (_views != null)
            {
                var templates = _views.CaptureTemplates();
                _views.Dispose(); _views = null;
                await UniTask.NextFrame();
                Report.TemplatesDestroyed = Array.TrueForAll(templates, template => template == null);
                if (!Report.TemplatesDestroyed) errors.Add(new InvalidOperationException("RC view templates survived disposal."));
            }
            if (_text != null) { _text.Dispose(); _text = null; }
            if (_config != null) { var owner = _config; _config = null; await Attempt(owner.ShutdownAsync); }
            if (_composition != null) { var owned = _composition; _composition = null; await Attempt(owned.ShutdownResourceServiceAsync); }
            if (_runner != null) { var owned = _runner; _runner = null; await Attempt(() => owned.ShutdownAsync()); }
            if (errors.Count != 0) throw new AggregateException("RC cleanup failed.", errors);
        }
    }
}
