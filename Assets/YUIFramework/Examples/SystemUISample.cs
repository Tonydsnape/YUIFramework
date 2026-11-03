using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YUIFramework.Configuration;

namespace YUIFramework
{
    public sealed class SystemUISample : MonoBehaviour
    {
        private void Start() => RunAsync(destroyCancellationToken).Forget(Debug.LogException);

        private async UniTask RunAsync(CancellationToken token)
        {
            var ui = new UIManager();
            using var loader = new CodeViewLoader();
            var configs = new SampleConfigOwner();
            SystemUISampleSession session = null;
            try
            {
                await ui.InitializeAsync(loader, UIRootRuntime.CreateOwned(), cancellationToken: token);
                await ConfigUIStartup.EnterAsync(configs.Service, ui,
                    snapshot => SampleUIConfiguration.Map(snapshot, "system"),
                    async ct =>
                    {
                        session = new SystemUISampleSession(ui);
                        await ui.Scopes.Global.PushAsync<SystemUISamplePage>(session, ct);
                    }, token);
                await UniTask.WaitUntilCanceled(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                try
                {
                    if (session != null) await session.ShutdownAsync();
                }
                finally
                {
                    try { await ui.ShutdownAsync(); }
                    finally { await configs.ShutdownAsync(); }
                }
            }
        }
    }

    public sealed class SystemUISampleSession
    {
        private readonly UIManager _ui;
        private readonly OverlayPresenter<SampleLoadingContext> _loadingView;
        private readonly OverlayPresenter<SampleToastContext> _toastView;
        private readonly List<Scene> _createdScenes = new List<Scene>();
        private readonly Scene _original;
        private bool _stopped;
        public SystemUISampleSession(UIManager ui, IUISystemClock clock = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _original = SceneManager.GetActiveScene();
            Loading = new UILoadingService();
            Toasts = new UIToastService(clock);
            Dialogs = new UIConfirmationService<SampleConfirmationContext>(ui);
            System = new UISystemUIService<SampleSystemContext>(ui);
            _loadingView = new OverlayPresenter<SampleLoadingContext>(ui, Loading);
            _toastView = new OverlayPresenter<SampleToastContext>(ui, Toasts);
            Loading.Changed += OnLoading;
            Toasts.Changed += OnToast;
        }
        public UILoadingService Loading { get; }
        public UIToastService Toasts { get; }
        public UIConfirmationService<SampleConfirmationContext> Dialogs { get; }
        public UISystemUIService<SampleSystemContext> System { get; }
        public event Action<string> StatusChanged;
        public void Tick() { if (!_stopped) Toasts.Tick(); }
        private void OnLoading(UILoadingState state) => _loadingView.SetVisible(state.IsVisible);
        private void OnToast(UIToastItem item) => _toastView.SetVisible(item != null);

        public async UniTask RunLoadingAsync()
        {
            var owner = _ui.Scopes.CurrentScene;
            await UniTask.WhenAll(WorkAsync(owner, "Short operation", 6), WorkAsync(owner, "Long operation", 15));
        }
        private async UniTask WorkAsync(UIScope owner, string message, int steps)
        {
            try
            {
                await owner.RunAsync(async ct =>
                {
                    using var operation = Loading.Begin(owner, message, cancellationToken: ct);
                    for (var i = 0; i < steps; i++)
                    {
                        await UniTask.Delay(200, ignoreTimeScale: true, cancellationToken: operation.Token);
                        operation.Report((i + 1f) / steps);
                    }
                });
                Report(message + " completed");
            }
            catch (OperationCanceledException) { Report(message + " canceled"); }
        }
        public void ToastBurst()
        {
            for (var i = 0; i < 100; i++)
                Toasts.Enqueue(_ui.Scopes.CurrentScene, new UIToastRequest("sample." + i % 3,
                    "Merged notification " + i % 3, priority: i % 3));
            Report("100 notifications admitted as at most 3 merged items");
        }
        public async UniTask ConfirmAsync()
        {
            try
            {
                var result = await Dialogs.ConfirmAsync(_ui.Scopes.CurrentScene,
                    new UIConfirmationRequest("Scene confirmation", "This dialog belongs to the current scene.",
                        "Continue", "Cancel"));
                Report("Dialog: " + result);
            }
            catch (OperationCanceledException) { Report("Scene dialog canceled"); }
        }
        public async UniTask ShowSystemAsync(UISystemNotice notice)
        {
            try
            {
                var result = await System.ShowAsync(notice,
                    new UIConfirmationRequest(notice.ToString(),
                        "Application-owned policy; no network request or store launch is performed.",
                        notice == UISystemNotice.Disconnected ? "Retry" :
                        notice == UISystemNotice.ForceUpdate ? "Update" : "Acknowledge",
                        "Cancel", notice == UISystemNotice.Disconnected));
                Report("System decision: " + result);
            }
            catch (OperationCanceledException) when (_stopped) { }
        }
        public async UniTask ChangeSceneAsync()
        {
            var previous = _ui.Scopes.CurrentScene;
            using (_ui.InputArbiter.Acquire(this, "sample scene transition", UILayer.Loading, UILayer.System))
            {
                var next = SceneManager.CreateScene("SystemUISample_" + Guid.NewGuid().ToString("N"));
                _createdScenes.Add(next);
                SceneManager.SetActiveScene(next);
                await previous.ReleaseAsync();
            }
            Report("Old scene UI/tasks ended; global system UI retained");
        }
        private void Report(string text) => StatusChanged?.Invoke(text);

        public async UniTask ShutdownAsync()
        {
            if (_stopped) return;
            _stopped = true;
            Loading.Changed -= OnLoading;
            Toasts.Changed -= OnToast;
            var errors = new List<Exception>();
            try { Loading.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { Toasts.Dispose(); } catch (Exception e) { errors.Add(e); }
            try { await Dialogs.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
            try { await System.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
            try { await _loadingView.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
            try { await _toastView.ShutdownAsync(); } catch (Exception e) { errors.Add(e); }
            if (_original.IsValid() && _original.isLoaded) SceneManager.SetActiveScene(_original);
            foreach (var scene in _createdScenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                try { await SceneManager.UnloadSceneAsync(scene).ToUniTask(); }
                catch (Exception e) { errors.Add(e); }
            }
            _createdScenes.Clear();
            StatusChanged = null;
            if (errors.Count > 0) throw new AggregateException("System UI sample teardown failed.", errors);
        }

        private sealed class OverlayPresenter<T> where T : BaseContext
        {
            private readonly UIManager _ui;
            private readonly object _model;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private CancellationTokenSource _display;
            private bool _visible, _dirty, _running, _stopped;
            private Task _run;
            public OverlayPresenter(UIManager ui, object model) { _ui = ui; _model = model; }
            public void SetVisible(bool visible)
            {
                if (_stopped) return;
                _visible = visible;
                _dirty = true;
                if (!visible) _display?.Cancel();
                if (_running) return;
                _running = true;
                _run = RunAsync().AsTask();
                _run.AsUniTask().Forget(Debug.LogException);
            }
            private async UniTask RunAsync()
            {
                try
                {
                    while (_dirty && !_stopped)
                    {
                        _dirty = false;
                        if (_visible && _ui.Get<T>() == null)
                        {
                            using var display = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                            _display = display;
                            try { await _ui.Scopes.Global.OpenAsync<T>(_model, display.Token); }
                            catch (OperationCanceledException) when (display.IsCancellationRequested) { _dirty = true; }
                            finally { _display = null; }
                        }
                        else if (!_visible && _ui.Get<T>() != null)
                            await _ui.CloseAsync<T>();
                    }
                }
                finally { _running = false; }
            }
            public async UniTask ShutdownAsync()
            {
                _stopped = true;
                _stop.Cancel();
                try { if (_run != null) await _run.AsUniTask(); }
                finally
                {
                    try { if (_ui.IsInitialized && _ui.Get<T>() != null) await _ui.CloseAsync<T>(); }
                    finally { _stop.Dispose(); }
                }
            }
        }
    }
}
