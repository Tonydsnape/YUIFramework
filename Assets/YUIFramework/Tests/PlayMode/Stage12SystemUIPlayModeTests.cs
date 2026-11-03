using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    public sealed class Stage12SystemUIPlayModeTests
    {
        private UIManager _ui;
        private UIResourceService _resources;
        private Provider _provider;
        private readonly List<Scene> _scenes = new List<Scene>();
        private Scene _original;
        private SystemUISampleSession _sample;
        private SampleConfigOwner _configs;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _original = SceneManager.GetActiveScene();
            _provider = new Provider();
            _resources = new UIResourceService();
            _resources.Packages.Register(_provider, true);
            _ui = new UIManager();
            _ui.Initialize(_resources);
            Register<ScenePage>();
            Register<GlobalPage>();
            Register<ModuleWidget>();
            Register<TestDialog>(UILayer.Popup);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _provider.Complete();
            if (_sample != null) yield return Await(_sample.ShutdownAsync().AsTask());
            yield return Await(_ui.ShutdownAsync().AsTask());
            if (_configs != null) yield return Await(_configs.ShutdownAsync().AsTask());
            yield return Await(_resources.ShutdownAsync().AsTask());
            SceneManager.SetActiveScene(_original);
            foreach (var scene in _scenes)
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            _scenes.Clear();
            _provider.Dispose();
            _sample = null;
            _configs = null;
            yield return null;
        }

        private void Register<T>(UILayer layer = UILayer.Normal) where T : BaseContext =>
            _ui.Register<T>(new UIConfig { Id = typeof(T).Name, PrefabKey = typeof(T).Name,
                Layer = layer, CacheOnClose = true, MaxPoolSize = 2 });
        private Scene NewScene(string name)
        {
            var scene = SceneManager.CreateScene(name);
            _scenes.Add(scene);
            return scene;
        }
        private static IEnumerator Await(Task task) => ResourceOwnershipPlayModeTests.Await(task);
        private static IEnumerator Canceled(Task task, string phase = "operation")
        {
            while (!task.IsCompleted) yield return null;
            Assert.That(task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException,
                Is.True, phase + ": " + task.Status + " " + task.Exception);
        }
        private IEnumerator WaitFor<T>(bool open = true) where T : BaseContext
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (_ui.IsOpen<T>() != open && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(_ui.IsOpen<T>(), Is.EqualTo(open), typeof(T).Name + " timed out");
        }

        [UnityTest]
        public IEnumerator SceneSwitch_CancelsTasks_ClosesActiveAndIdle_RetainsGlobalAndModule()
        {
            var scene = NewScene("Stage12A");
            SceneManager.SetActiveScene(scene);
            var scope = _ui.Scopes.CurrentScene;
            var module = _ui.Scopes.CreateModule("module");
            var page = scope.PushAsync<ScenePage>().AsTask();
            yield return Await(page);
            var global = _ui.Scopes.Global.OpenAsync<GlobalPage>().AsTask();
            var widget = module.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(global);
            yield return Await(widget);
            var work = scope.RunAsync(ct => UniTask.WaitUntilCanceled(ct)).AsTask();
            Assert.That(scope.Identity, Is.EqualTo(_ui.GetSceneScope(scene)));

            SceneManager.SetActiveScene(NewScene("Stage12B"));
            yield return Await(scope.ReleaseAsync().AsTask());
            yield return Canceled(work);
            Assert.That(page.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_ui.Navigator.Count, Is.Zero);
            Assert.That(_ui.IsOpen<GlobalPage>(), Is.True);
            Assert.That(_ui.IsOpen<ModuleWidget>(), Is.True);
            Assert.That(scope.OperationCount, Is.Zero);
            Assert.That(_ui.PoolDiagnostics.IdleCount, Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.EqualTo(2));
            yield return Await(module.ReleaseAsync().AsTask());
        }

        [UnityTest]
        public IEnumerator AdditiveUnload_UsesSceneHandleAndCleansActiveUI()
        {
            var a = NewScene("Stage12SameName");
            var b = NewScene("Stage12OtherName");
            b.name = a.name;
            var first = _ui.Scopes.ForScene(a);
            var second = _ui.Scopes.ForScene(b);
            Assert.That(first.Identity, Is.Not.EqualTo(second.Identity));
            var open = first.OpenAsync<ScenePage>().AsTask();
            yield return Await(open);
            yield return SceneManager.UnloadSceneAsync(a);
            yield return Await(first.ReleaseAsync().AsTask());
            Assert.That(open.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(second.IsEnded, Is.False);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RapidABC_AndLateNativeLoad_CannotPublishOldScene()
        {
            SceneManager.SetActiveScene(NewScene("Stage12RapidA"));
            var a = _ui.Scopes.CurrentScene;
            _provider.Delayed = true;
            var open = a.OpenAsync<ScenePage>().AsTask();
            for (var i = 0; _provider.LoadCount == 0 && i < 600; i++) yield return null;
            Assert.That(_provider.LoadCount, Is.EqualTo(1));
            SceneManager.SetActiveScene(NewScene("Stage12RapidB"));
            var b = _ui.Scopes.CurrentScene;
            SceneManager.SetActiveScene(NewScene("Stage12RapidC"));
            var c = _ui.Scopes.CurrentScene;
            yield return Await(a.ReleaseAsync().AsTask());
            yield return Await(b.ReleaseAsync().AsTask());
            yield return Canceled(open);
            _provider.Complete();
            yield return null;
            yield return null;
            Assert.That(_ui.Get<ScenePage>(), Is.Null);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.That(_provider.ReleaseCount, Is.EqualTo(1));
            _provider.Delayed = false;
            yield return Await(c.OpenAsync<ScenePage>().AsTask());
            Assert.That(_ui.IsOpen<ScenePage>(), Is.True);
        }

        [UnityTest]
        public IEnumerator SceneNavigationPrune_RestoresHiddenGlobalPageAndFocus()
        {
            var global = _ui.Scopes.Global.PushAsync<GlobalPage>().AsTask();
            yield return Await(global);
            var scene = _ui.Scopes.ForScene(NewScene("Stage12Nav"));
            var page = scene.PushAsync<ScenePage>().AsTask();
            yield return Await(page);
            Assert.That(global.Result.State, Is.EqualTo(UIContextState.Hidden));
            yield return Await(scene.ReleaseAsync().AsTask());
            Assert.That(_ui.Navigator.Count, Is.EqualTo(1));
            Assert.That(_ui.Navigator.CurrentPage, Is.SameAs(global.Result));
            Assert.That(global.Result.State, Is.EqualTo(UIContextState.Opened));
        }

        [UnityTest]
        public IEnumerator LegacyPoolScopeRelease_DoesNotDestroyActiveUI()
        {
            var poolScope = _ui.CreateModuleScope("legacy");
            var open = _ui.OpenInScopeAsync<ModuleWidget>(poolScope).AsTask();
            yield return Await(open);
            yield return Await(_ui.ReleaseScopeAsync(poolScope).AsTask());
            Assert.That(open.Result.State, Is.EqualTo(UIContextState.Opened));
            yield return Await(_ui.CloseAsync(open.Result).AsTask());
            Assert.That(open.Result.State, Is.EqualTo(UIContextState.Released));
        }

        [UnityTest]
        public IEnumerator Confirmation_ClicksExternalCloseAndCallerCancellation_HaveStableResults()
        {
            var service = new UIConfirmationService<TestDialog>(_ui);
            var owner = _ui.Scopes.Global;
            var first = service.ConfirmAsync(owner, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            var session = _ui.Get<TestDialog>().Session;
            Assert.That(session.Confirm(), Is.True);
            Assert.That(session.Decline(), Is.False);
            yield return Await(first);
            Assert.That(first.Result, Is.EqualTo(UIDialogResult.Confirmed));
            Assert.That(_ui.Modals.Count, Is.Zero);
            var external = service.ConfirmAsync(owner, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            yield return Await(_ui.CloseAsync<TestDialog>().AsTask());
            yield return Await(external);
            Assert.That(external.Result, Is.EqualTo(UIDialogResult.Dismissed));
            using var cancel = new CancellationTokenSource();
            var third = service.ConfirmAsync(owner, Request(), cancel.Token).AsTask();
            yield return WaitFor<TestDialog>();
            cancel.Cancel();
            yield return Canceled(third);
            Assert.That(_ui.IsOpen<TestDialog>(), Is.False);
            yield return Await(service.ShutdownAsync().AsTask());
        }

        [UnityTest]
        public IEnumerator Confirmation_SceneEndCancelsActiveAndQueued_AndShutdownDrainsGlobal()
        {
            var owner = _ui.Scopes.CreateModule("dialog");
            var service = new UIConfirmationService<TestDialog>(_ui);
            var first = service.ConfirmAsync(owner, Request()).AsTask();
            var second = service.ConfirmAsync(owner, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            yield return Await(owner.ReleaseAsync().AsTask());
            yield return Canceled(first, "active scoped confirmation");
            yield return Canceled(second, "queued scoped confirmation");
            Assert.That(_ui.Modals.Count, Is.Zero);
            var global = service.ConfirmAsync(_ui.Scopes.Global, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            yield return Await(_ui.ShutdownAsync().AsTask());
            yield return Canceled(global, "global shutdown confirmation");
            yield return Await(service.ShutdownAsync().AsTask());
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InputArbiter_UsesSameNestedLocksAndModalStack()
        {
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.True);
            var one = _ui.InputArbiter.Acquire(this, "scene transition", UILayer.System);
            var two = _ui.InputLocks.Acquire(this, "nested", UILayer.System);
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.False);
            one.Dispose();
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.False);
            two.Dispose();
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.True);
            var service = new UIConfirmationService<TestDialog>(_ui);
            var dialog = service.ConfirmAsync(_ui.Scopes.Global, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.False);
            _ui.Get<TestDialog>().Session.Decline();
            yield return Await(dialog);
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.True);
            yield return Await(service.ShutdownAsync().AsTask());
            var widget = _ui.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(widget);
            var events = _ui.RootRuntime.EventSystem;
            events.SetSelectedGameObject(widget.Result.ViewObject);
            Assert.That(_ui.InputArbiter.CanProcessSceneKeyboard(), Is.False);
            events.SetSelectedGameObject(null);
            Assert.That(_ui.InputArbiter.CanProcessSceneKeyboard(), Is.True);
            foreach (var input in events.GetComponents<BaseInputModule>()) input.enabled = false;
            var pointers = events.gameObject.AddComponent<ArbiterPointerModule>();
            yield return null;
            Assert.That(events.currentInputModule, Is.SameAs(pointers));
            pointers.PointAt(17, widget.Result.ViewObject);
            Assert.That(_ui.InputArbiter.CanProcessScenePointer(17), Is.False);
            Assert.That(_ui.InputArbiter.CanProcessScenePointer(18), Is.True);
            pointers.PointAt(17, null);
            Assert.That(_ui.InputArbiter.CanProcessScenePointer(17), Is.True);
            yield return Await(_ui.OpenAsync<GlobalPage>().AsTask());
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.False);
            yield return Await(_ui.CloseAsync<GlobalPage>().AsTask());
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.True);
        }

        [UnityTest]
        public IEnumerator ScopeEnd_ClearsPrewarmedAndActiveUI_AndSharedReleaseWaitsForNoncooperativeWork()
        {
            _ui.TryGetConfig(typeof(ScenePage), out var config);
            config.PreloadCount = 1;
            var scope = _ui.Scopes.CreateModule("drain");
            yield return Await(_ui.PrewarmAsync<ScenePage>(scope.Identity).AsTask());
            var open = scope.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(open);
            var completion = new UniTaskCompletionSource<int>();
            var late = scope.RunAsync(_ => completion.Task).AsTask();
            var first = scope.ReleaseAsync().AsTask();
            var second = scope.ReleaseAsync().AsTask();
            for (var i = 0; i < 20; i++) yield return null;
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(_ui.PoolDiagnostics.IdleCount, Is.Zero);
            Assert.That(open.Result.State, Is.EqualTo(UIContextState.Released));
            completion.TrySetResult(1);
            yield return Canceled(late);
            yield return Await(first);
            yield return Await(second);
            Assert.That(scope.OperationCount, Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ScopeEnd_ContinuesAfterCancellationAndDestroyFailures()
        {
            Register<FailingWidget>();
            var scope = _ui.Scopes.CreateModule("fail");
            var broken = scope.OpenAsync<FailingWidget>().AsTask();
            var normal = scope.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(broken);
            yield return Await(normal);
            using var registration = scope.Token.Register(() => throw new InvalidOperationException("cancel failure"));
            var release = scope.ReleaseAsync().AsTask();
            yield return ResourceOwnershipPlayModeTests.AwaitFailure(release);
            Assert.That(release.Exception.Flatten().InnerExceptions.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(broken.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(normal.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            Assert.That(_ui.PoolDiagnostics.IdleCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ScopeEnd_CancelsNavigationGuardBeforeItCanEnterOldScene()
        {
            var scope = _ui.Scopes.CreateModule("guard");
            var entered = false;
            _ui.Navigator.Guard = async (_, ct) =>
            {
                entered = true;
                await UniTask.WaitUntilCanceled(ct);
                ct.ThrowIfCancellationRequested();
                return true;
            };
            var push = scope.PushAsync<ScenePage>().AsTask();
            for (var i = 0; !entered && i < 600; i++) yield return null;
            Assert.That(entered, Is.True);
            yield return Await(scope.ReleaseAsync().AsTask());
            yield return Canceled(push);
            Assert.That(_ui.Navigator.Count, Is.Zero);
            Assert.That(_provider.LoadCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LoadingAndToastOwnerEnd_RemoveOnlyThatOwnersWork()
        {
            using var loading = new UILoadingService();
            using var toasts = new UIToastService();
            var owner = _ui.Scopes.CreateModule("owned models");
            var one = loading.Begin(owner, "scoped");
            using var global = loading.Begin(_ui.Scopes.Global, "global");
            var a = toasts.Enqueue(owner, new UIToastRequest("a", "scoped"));
            var b = toasts.Enqueue(_ui.Scopes.Global, new UIToastRequest("b", "global"));
            toasts.Tick();
            yield return Await(owner.ReleaseAsync().AsTask());
            Assert.That(one.Token.IsCancellationRequested, Is.True);
            Assert.That(loading.State.Count, Is.EqualTo(1));
            Assert.That(toasts.Count, Is.EqualTo(1));
            Assert.That(a.Completion.GetAwaiter().GetResult(), Is.EqualTo(UIToastOutcome.Canceled));
            Assert.That(b.IsCompleted, Is.False);
        }

        [UnityTest]
        public IEnumerator Confirmation_CanceledCloseRollsBackWithoutResolvingSession()
        {
            _ui.TryGetConfig(typeof(TestDialog), out var config);
            config.UseTransition = true;
            config.TransitionType = UITransitionType.Fade;
            config.ShowDuration = 0;
            config.HideDuration = .1f;
            var service = new UIConfirmationService<TestDialog>(_ui);
            var result = service.ConfirmAsync(_ui.Scopes.Global, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            var context = _ui.Get<TestDialog>();
            var session = context.Session;
            using var cancellation = new CancellationTokenSource();
            var close = _ui.CloseAsync(context, cancellation.Token).AsTask();
            for (var i = 0; context.State != UIContextState.Hiding && i < 600; i++) yield return null;
            Assert.That(context.State, Is.EqualTo(UIContextState.Hiding));
            cancellation.Cancel();
            yield return Canceled(close);
            Assert.That(context.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(context.Session, Is.SameAs(session));
            Assert.That(result.IsCompleted, Is.False);
            session.Decline();
            yield return Await(result);
            Assert.That(result.Result, Is.EqualTo(UIDialogResult.Declined));
            yield return Await(service.ShutdownAsync().AsTask());
        }

        [UnityTest]
        public IEnumerator RepeatedModuleCycles_DoNotRetainActivePoolOrResourceLeases()
        {
            for (var i = 0; i < 100; i++)
            {
                var scope = _ui.Scopes.CreateModule("cycle");
                yield return Await(scope.OpenAsync<ModuleWidget>().AsTask());
                yield return Await(scope.ReleaseAsync().AsTask());
                Assert.That(_ui.Get<ModuleWidget>(), Is.Null);
                Assert.That(scope.OperationCount, Is.Zero);
                Assert.That(_ui.PoolDiagnostics.IdleCount, Is.Zero);
                Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator NewScopeOpen_DuringPreviousTeardownHandsOffTypeWithoutClosingReplacement()
        {
            var first = _ui.Scopes.CreateModule("first");
            var second = _ui.Scopes.CreateModule("second");
            var old = first.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(old);
            var ending = first.ReleaseAsync().AsTask();
            var next = second.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(next);
            yield return Await(ending);
            Assert.That(old.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_ui.Get<ModuleWidget>(), Is.SameAs(next.Result));
            Assert.That(next.Result.State, Is.EqualTo(UIContextState.Opened));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.EqualTo(1));
        }

        private IEnumerator CreateSample()
        {
            _configs = new SampleConfigOwner();
            var snapshot = _configs.Service.InitializeAsync().AsTask();
            yield return Await(snapshot);
            _ui.RegisterBatch(SampleUIConfiguration.Map(snapshot.Result, "system"));
            _sample = new SystemUISampleSession(_ui);
        }

        [UnityTest]
        public IEnumerator ActualSample_LoadingRemainsUntilLastCallerAndPendingOpenCanBeCanceled()
        {
            yield return CreateSample();
            var first = _sample.Loading.Begin(_ui.Scopes.Global, "First operation");
            var second = _sample.Loading.Begin(_ui.Scopes.Global, "Second operation");
            yield return WaitFor<SampleLoadingContext>();
            first.Report(1); second.Report(.5f);
            first.Dispose();
            yield return null;
            Assert.That(_ui.IsOpen<SampleLoadingContext>(), Is.True);
            Assert.That(_ui.Get<SampleLoadingContext>().ViewObject.GetComponentInChildren<Text>().text,
                Is.EqualTo("Second operation"));
            second.Dispose();
            yield return WaitFor<SampleLoadingContext>(false);
            Assert.That(_ui.Modals.Count, Is.Zero);
            var canceled = _sample.Loading.Begin(_ui.Scopes.Global, "Canceled before publication");
            canceled.Dispose();
            for (var i = 0; i < 10; i++) yield return null;
            Assert.That(_ui.IsOpen<SampleLoadingContext>(), Is.False);
            Assert.That(_sample.Loading.State.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ActualSample_DialogButtonsFocusAndGlobalSystemPolicyUseExistingModalRuntime()
        {
            yield return CreateSample();
            var page = _ui.Scopes.Global.PushAsync<GlobalPage>().AsTask();
            yield return Await(page);
            var prior = _ui.RootRuntime.EventSystem.currentSelectedGameObject;
            var dialog = _sample.Dialogs.ConfirmAsync(_ui.Scopes.CurrentScene, Request()).AsTask();
            yield return WaitFor<SampleConfirmationContext>();
            var context = _ui.Get<SampleConfirmationContext>();
            Assert.That(_ui.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(context.DefaultFocus));
            context.DefaultFocus.GetComponent<Button>().onClick.Invoke();
            yield return Await(dialog);
            Assert.That(dialog.Result, Is.EqualTo(UIDialogResult.Confirmed));
            Assert.That(_ui.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(prior));
            var system = _sample.System.ShowAsync(UISystemNotice.ForceUpdate,
                new UIConfirmationRequest("Update required", "Install the supported version.", "Update", allowCancel: false)).AsTask();
            yield return WaitFor<SampleSystemContext>();
            var notice = _ui.Get<SampleSystemContext>();
            Assert.That(_ui.Modals.Top, Is.SameAs(notice));
            Assert.That(notice.ViewObject.GetComponentsInChildren<Button>().Length, Is.EqualTo(1));
            yield return Await(_sample.ChangeSceneAsync().AsTask());
            Assert.That(_ui.IsOpen<SampleSystemContext>(), Is.True);
            notice.DefaultFocus.GetComponent<Button>().onClick.Invoke();
            yield return Await(system);
            Assert.That(system.Result, Is.EqualTo(UISystemDecision.UpdateRequired));
        }

        [UnityTest]
        public IEnumerator ActualSample_ToastIsNonModalAndHighFrequencyDisplayUsesBoundedQueue()
        {
            yield return CreateSample();
            for (var i = 0; i < 10000; i++)
                _sample.Toasts.Enqueue(_ui.Scopes.Global, new UIToastRequest("merged", "Real Toast"));
            Assert.That(_sample.Toasts.Count, Is.EqualTo(1));
            _sample.Tick();
            yield return WaitFor<SampleToastContext>();
            var context = _ui.Get<SampleToastContext>();
            StringAssert.Contains("x10000", context.ViewObject.GetComponentInChildren<Text>().text);
            Assert.That(_ui.Modals.Count, Is.Zero);
            Assert.That(_ui.InputArbiter.IsSceneInputAllowed, Is.True);
            foreach (var graphic in context.ViewObject.GetComponentsInChildren<Graphic>())
                Assert.That(graphic.raycastTarget, Is.False);
        }

        private static UIConfirmationRequest Request() => new UIConfirmationRequest("Confirm", "Continue?");

        [UnityTest]
        public IEnumerator Back_DeclinesTopDialog_BlocksLoadingAndForceUpdate_DoesNotPopCoveredPage()
        {
            yield return CreateSample();
            yield return Await(_ui.Scopes.Global.PushAsync<GlobalPage>().AsTask());
            yield return Await(_ui.Scopes.Global.PushAsync<ScenePage>().AsTask());
            var confirmation = _sample.Dialogs.ConfirmAsync(_ui.Scopes.Global, Request()).AsTask();
            yield return WaitFor<SampleConfirmationContext>();
            Assert.That(_ui.Input.RequestBack(), Is.True);
            Assert.That(_ui.Input.RequestBack(), Is.False);
            yield return Await(confirmation);
            Assert.That(confirmation.Result, Is.EqualTo(UIDialogResult.Declined));
            Assert.That(_ui.Navigator.Count, Is.EqualTo(2));
            var notice = _sample.System.ShowAsync(UISystemNotice.ForceUpdate,
                new UIConfirmationRequest("Update", "Update required", allowCancel: false)).AsTask();
            yield return WaitFor<SampleSystemContext>();
            Assert.That(_ui.Input.RequestBack(), Is.False);
            Assert.That(_ui.Navigator.Count, Is.EqualTo(2));
            _ui.Get<SampleSystemContext>().Session.Confirm();
            yield return Await(notice);
            using var loading = _sample.Loading.Begin(_ui.Scopes.Global, "Loading");
            yield return WaitFor<SampleLoadingContext>();
            Assert.That(_ui.Input.RequestBack(), Is.False);
            Assert.That(_ui.Navigator.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Confirmation_ConcurrentShutdownCallersAwaitTheSameDrain()
        {
            var service = new UIConfirmationService<TestDialog>(_ui);
            var dialog = service.ConfirmAsync(_ui.Scopes.Global, Request()).AsTask();
            yield return WaitFor<TestDialog>();
            var first = service.ShutdownAsync().AsTask();
            var second = service.ShutdownAsync().AsTask();
            yield return Await(first);
            yield return Await(second);
            yield return Canceled(dialog);
            Assert.That(_ui.IsOpen<TestDialog>(), Is.False);
            Assert.That(_ui.Modals.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ScopeEnd_CancelsAnInflightCloseBeforeCleanupClosesTheIdentity()
        {
            _ui.TryGetConfig(typeof(ModuleWidget), out var config);
            config.UseTransition = true;
            config.TransitionType = UITransitionType.Fade;
            config.ShowDuration = 0;
            config.HideDuration = .05f;
            var scope = _ui.Scopes.CreateModule("closing");
            var opened = scope.OpenAsync<ModuleWidget>().AsTask();
            yield return Await(opened);
            var close = _ui.CloseAsync(opened.Result).AsTask();
            for (var i = 0; opened.Result.State != UIContextState.Hiding && i < 600; i++) yield return null;
            Assert.That(opened.Result.State, Is.EqualTo(UIContextState.Hiding));
            var end = scope.ReleaseAsync().AsTask();
            yield return Canceled(close);
            yield return Await(end);
            Assert.That(opened.Result.State, Is.EqualTo(UIContextState.Released));
            Assert.That(_resources.GetDiagnostics().TotalLeases, Is.Zero);
        }

        public sealed class ScenePage : BasePageContext { }
        public sealed class ArbiterPointerModule : PointerInputModule
        {
            public override void Process() { }
            public void PointAt(int pointerId, GameObject target)
            {
                GetPointerData(pointerId, out var data, true);
                data.pointerEnter = target;
            }
        }
        public sealed class GlobalPage : BasePageContext
        {
            private Button _button;
            public override GameObject DefaultFocus => _button?.gameObject;
            protected override void HandleInit()
            {
                var go = new GameObject("Focus", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(ViewObject.transform, false);
                _button = go.GetComponent<Button>();
            }
        }
        public sealed class ModuleWidget : BaseWidgetContext { }
        public sealed class FailingWidget : BaseWidgetContext
        {
            protected override void HandleDestroy() => throw new InvalidOperationException("destroy failure");
        }
        public sealed class TestDialog : UIConfirmationContext
        {
            protected override void Present(UIConfirmationSession session) { }
        }

        private sealed class Provider : IUIResourceProvider, IDisposable
        {
            private readonly List<Handle> _handles = new List<Handle>();
            private readonly List<UniTaskCompletionSource<IUINativeAssetHandle>> _pending =
                new List<UniTaskCompletionSource<IUINativeAssetHandle>>();
            public string PackageName => "Stage12";
            public bool Delayed;
            public int LoadCount, ReleaseCount;
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            {
                LoadCount++;
                if (!Delayed) return UniTask.FromResult(Create());
                var completion = new UniTaskCompletionSource<IUINativeAssetHandle>();
                _pending.Add(completion);
                return completion.Task;
            }
            private IUINativeAssetHandle Create()
            {
                var go = new GameObject("SystemTestPrefab", typeof(RectTransform), typeof(UIView));
                go.SetActive(false);
                var handle = new Handle(go, () => ReleaseCount++);
                _handles.Add(handle);
                return handle;
            }
            public void Complete()
            {
                foreach (var pending in _pending) pending.TrySetResult(Create());
                _pending.Clear();
            }
            public UniTask ShutdownAsync() => UniTask.CompletedTask;
            public void Dispose()
            {
                foreach (var handle in _handles) UnityEngine.Object.Destroy(handle.Asset);
            }
            private sealed class Handle : IUINativeAssetHandle
            {
                private readonly Action _release;
                public Handle(GameObject asset, Action release) { Asset = asset; _release = release; }
                public UnityEngine.Object Asset { get; }
                public bool IsValid { get; private set; } = true;
                public void Release() { if (!IsValid) return; IsValid = false; _release(); }
            }
        }
    }
}
