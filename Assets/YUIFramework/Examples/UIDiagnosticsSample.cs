using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    public sealed class UIDiagnosticsSample : MonoBehaviour
    {
        private UIDiagnosticsSampleSession _session;
        private void Start()
        {
            if (!UIProfilerSampler.DevelopmentAllowed)
            {
                Debug.LogWarning("UIDiagnosticsSample is available only in Editor/Development Build.");
                enabled = false;
                return;
            }
            _session = new UIDiagnosticsSampleSession();
            _session.StartAsync(this.GetCancellationTokenOnDestroy()).Forget(Debug.LogException);
        }
        private void OnDestroy() => _session?.ShutdownAsync().Forget(Debug.LogException);
    }

    public sealed class UIDiagnosticsSampleSession
    {
        private readonly SampleProvider _provider = new SampleProvider();
        private readonly List<IDisposable> _injected = new List<IDisposable>();
        private readonly Action<int> _demoHandler = _ => { };
        private UniTaskCompletionSource _pending;
        private Task _work, _release, _shutdown;
        public UIDiagnosticsSampleSession()
        {
            Diagnostics = new UIDiagnostics();
            Sampler = new UIProfilerSampler();
            Resources = new UIResourceService(diagnostics: Diagnostics);
            Resources.Packages.Register(_provider, true);
            UI = new UIManager(diagnostics: Diagnostics);
            UI.Initialize(Resources);
            Register<DiagnosticsSamplePage>(UILayer.Normal, false);
            Register<DiagnosticsSampleWidget>(UILayer.Fixed, true);
            Register<DiagnosticsLeakProbe>(UILayer.Normal, false);
            Register<UIDiagnosticsPanelContext>(UILayer.Debug, false, true);
            Panel = new UIDiagnosticsPanelModel(UI, Diagnostics, Sampler);
        }
        public UIManager UI { get; }
        public UIResourceService Resources { get; }
        public UIDiagnostics Diagnostics { get; }
        public UIProfilerSampler Sampler { get; }
        public UIDiagnosticsPanelModel Panel { get; }
        private void Register<T>(UILayer layer, bool cache, bool modal = false) where T : BaseContext =>
            UI.Register<T>(new UIConfig { Id = typeof(T).Name, PrefabKey = typeof(T).Name,
                Layer = layer, CacheOnClose = cache, MaxPoolSize = cache ? 1 : 0,
                UseLayerModalPolicy = false, Modal = modal, UseTransition = true,
                TransitionType = UITransitionType.Fade, ShowDuration = .05f, HideDuration = .05f });

        public async UniTask StartAsync(CancellationToken token = default)
        {
            await UI.Scopes.Global.PushAsync<DiagnosticsSamplePage>(this, token);
            await ShowPanelAsync(token);
        }
        public UniTask<UIDiagnosticsPanelContext> ShowPanelAsync(CancellationToken token = default) =>
            UI.OpenAsync<UIDiagnosticsPanelContext>(Panel, token);
        public UniTask<DiagnosticsSampleWidget> OpenWidgetAsync() => UI.OpenAsync<DiagnosticsSampleWidget>();
        public UniTask CloseWidgetAsync() => UI.CloseAsync<DiagnosticsSampleWidget>();

        public async UniTask InjectFindingsAsync()
        {
            await ClearFindingsAsync();
            var scope = UI.Scopes.CreateModule("Diagnostic fault demonstration");
            var owner = await scope.OpenAsync<DiagnosticsLeakProbe>();
            var topic = new UIMessageTopic<int>("diagnostics.sample.duplicate");
            _injected.Add(UI.InputLocks.Acquire(owner, "Injected ended-owner lock", UILayer.Debug, UILayer.Normal));
            _injected.Add(await Resources.LoadOwnedAssetAsync<GameObject>(
                owner, UIResourceKey.Of<GameObject>("InjectedLease"), scope));
            _pending = new UniTaskCompletionSource();
            _work = scope.RunAsync(_ => _pending.Task).AsTask();
            _release = scope.ReleaseAsync().AsTask();
            while (owner.State != UIContextState.Released) await UniTask.Yield();
            // Deliberately simulate a late callback subscribing after normal owner cleanup.
            _injected.Add(UI.MessageCenter.Subscribe(topic, _demoHandler, owner: owner));
            _injected.Add(UI.MessageCenter.Subscribe(topic, _demoHandler, owner: owner));
            await ShowPanelAsync();
        }

        public async UniTask ClearFindingsAsync()
        {
            var errors = new List<Exception>();
            foreach (var item in _injected)
            {
                try { item.Dispose(); } catch (Exception exception) { errors.Add(exception); }
            }
            _injected.Clear();
            _pending?.TrySetResult();
            if (_work != null)
            {
                try { await _work.AsUniTask(); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { errors.Add(exception); }
            }
            if (_release != null)
            {
                try { await _release.AsUniTask(); } catch (Exception exception) { errors.Add(exception); }
            }
            _work = null; _release = null; _pending = null;
            if (errors.Count != 0) throw new AggregateException("Injected findings failed to clean up.", errors);
        }

        public UniTask ShutdownAsync()
        {
            if (_shutdown == null) _shutdown = ShutdownCoreAsync().AsTask();
            return _shutdown.AsUniTask();
        }
        private async UniTask ShutdownCoreAsync()
        {
            var errors = new List<Exception>();
            try { await ClearFindingsAsync(); } catch (Exception exception) { errors.Add(exception); }
            try { await UI.ShutdownAsync(); } catch (Exception exception) { errors.Add(exception); }
            try { await Resources.ShutdownAsync(); } catch (Exception exception) { errors.Add(exception); }
            Sampler.Dispose(); Diagnostics.Dispose();
            if (errors.Count != 0) throw new AggregateException("Diagnostics sample shutdown failed.", errors);
        }

        private sealed class SampleProvider : IUIResourceProvider
        {
            private readonly CodeViewLoader _loader = new CodeViewLoader();
            public string PackageName => "DiagnosticsMinimalSample";
            public async UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key) =>
                new Handle(await _loader.LoadPrefabAsync(key.Location));
            public UniTask ShutdownAsync() { _loader.Dispose(); return UniTask.CompletedTask; }
            private sealed class Handle : IUINativeAssetHandle
            {
                public Handle(GameObject asset) { Asset = asset; }
                public UnityEngine.Object Asset { get; private set; }
                public bool IsValid => Asset != null;
                public void Release() => Asset = null;
            }
        }
    }

    public sealed class DiagnosticsSamplePage : BasePageContext
    {
        private UIDiagnosticsSampleSession _session;
        private UnityEngine.UI.Button _first;
        public override GameObject DefaultFocus => _first?.gameObject;
        protected override void HandleInit()
        {
            var content = SystemUISampleLayout.Panel(this, TrackBinding, .05f, .95f);
            SystemUISampleLayout.Text(content, "Stage 13 - diagnostics sample (minimal code assets)", 28);
            _first = SystemUISampleLayout.Button(content, "Show diagnostics",
                () => _session.ShowPanelAsync().Forget(Debug.LogException));
            SystemUISampleLayout.Button(content, "Open / reuse widget", () => _session.OpenWidgetAsync().Forget(Debug.LogException));
            SystemUISampleLayout.Button(content, "Close / pool widget", () => _session.CloseWidgetAsync().Forget(Debug.LogException));
            SystemUISampleLayout.Button(content, "Inject real owner leaks / duplicate subscribers / pending task",
                () => _session.InjectFindingsAsync().Forget(Debug.LogException));
            SystemUISampleLayout.Button(content, "Clear injected findings", () => _session.ClearFindingsAsync().Forget(Debug.LogException));
        }
        protected override void HandleShow(object args)
        {
            _session = args as UIDiagnosticsSampleSession ??
                throw new ArgumentException("A diagnostic sample session is required.", nameof(args));
            TrackDisplayBinding(new BindingToken(() => _session = null));
        }
    }
    public sealed class DiagnosticsSampleWidget : BaseWidgetContext
    {
        public override UILayer DefaultLayer => UILayer.Fixed;
        protected override void HandleInit() =>
            SystemUISampleLayout.Text(SystemUISampleLayout.Panel(this, TrackBinding, .02f, .18f),
                "Observed pooled widget - its idle lease is legitimate.", 24);
    }
    public sealed class DiagnosticsLeakProbe : BaseWidgetContext { }
}
