using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace YUIFramework.Tests
{
    public sealed class Stage9BindingsLifecyclePlayModeTests
    {
        private static readonly UIMessageTopic<int> LifetimeTopic =
            new UIMessageTopic<int>("stage9.lifetime");
        private static readonly UIMessageTopic<int> DisplayTopic =
            new UIMessageTopic<int>("stage9.display");

        private GameObject _root;
        private BindingResourceLoader _loader;
        private UIManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LifecycleBindingPage.Reset();
            OwnedViewModelPage.Reset();
            FailingOpenPage.Reset();
            _root = new GameObject(
                "Stage9Root",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _root.AddComponent<UIRoot>();
            _loader = new BindingResourceLoader();
            _manager = new UIManager();
            _manager.Initialize(_loader, new UIObjectPool());
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null && _manager.IsInitialized)
            {
                var shutdown = _manager.ShutdownAsync().AsTask();
                yield return Await(shutdown, 120);
            }

            _loader?.Dispose();
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }

            yield return null;
        }

        [Test]
        public void UguiAndTmpBindings_InitialTwoWayValidationAndUnbindAreStable()
        {
            var objects = new List<GameObject>();
            var name = new ValidatedProperty<string>(
                string.Empty,
                value => string.IsNullOrWhiteSpace(value) ? "Required" : null);
            var enabled = new ObservableProperty<bool>(true);
            var progress = new ObservableProperty<float>(0.25f);
            var selection = new ObservableProperty<int>(1);
            var text = Create<Text>("Text", objects);
            var tmpText = Create<TextMeshProUGUI>("TmpText", objects);
            var toggle = Create<Toggle>("Toggle", objects);
            var slider = Create<Slider>("Slider", objects);
            var scrollbar = Create<Scrollbar>("Scrollbar", objects);
            var input = Create<InputField>("Input", objects);
            var inputText = Create<Text>("InputText", objects, input.transform);
            input.textComponent = inputText;
            var validationText = Create<Text>("Validation", objects);
            var tmpInput = Create<TMP_InputField>("TmpInput", objects);
            var tmpInputText = Create<TextMeshProUGUI>(
                "TmpInputText",
                objects,
                tmpInput.transform);
            tmpInput.textComponent = tmpInputText;
            var dropdown = Create<Dropdown>("Dropdown", objects);
            dropdown.options.Add(new Dropdown.OptionData("Zero"));
            dropdown.options.Add(new Dropdown.OptionData("One"));
            var tmpDropdown = Create<TMP_Dropdown>("TmpDropdown", objects);
            tmpDropdown.options.Add(new TMP_Dropdown.OptionData("Zero"));
            tmpDropdown.options.Add(new TMP_Dropdown.OptionData("One"));
            var bindings = new BindingToken();

            try
            {
                bindings.Add(UIDataBinding.BindText(text, name));
                bindings.Add(UIDataBinding.BindText(tmpText, name));
                bindings.Add(UIDataBinding.BindToggle(toggle, enabled));
                bindings.Add(UIDataBinding.BindSlider(slider, progress));
                bindings.Add(UIDataBinding.BindScrollbar(scrollbar, progress));
                bindings.Add(
                    UIDataBinding.BindInputField(
                        input,
                        name,
                        validation: name,
                        validationText: validationText));
                bindings.Add(UIDataBinding.BindInputField(tmpInput, name));
                bindings.Add(UIDataBinding.BindDropdown(dropdown, selection));
                bindings.Add(UIDataBinding.BindDropdown(tmpDropdown, selection));

                Assert.That(text.text, Is.Empty);
                Assert.That(tmpText.text, Is.Empty);
                Assert.That(toggle.isOn, Is.True);
                Assert.That(slider.value, Is.EqualTo(0.25f));
                Assert.That(scrollbar.value, Is.EqualTo(0.25f));
                Assert.That(dropdown.value, Is.EqualTo(1));
                Assert.That(tmpDropdown.value, Is.EqualTo(1));
                Assert.That(validationText.text, Is.EqualTo("Required"));
                Assert.That(validationText.gameObject.activeSelf, Is.True);

                var nameChanges = 0;
                using var observation = name.Subscribe(_ => nameChanges++, false);
                input.onValueChanged.Invoke("Alice");
                Assert.That(name.Value, Is.EqualTo("Alice"));
                Assert.That(nameChanges, Is.EqualTo(1));
                Assert.That(text.text, Is.EqualTo("Alice"));
                Assert.That(tmpInput.text, Is.EqualTo("Alice"));
                Assert.That(validationText.gameObject.activeSelf, Is.False);

                progress.Value = 0.75f;
                Assert.That(slider.value, Is.EqualTo(0.75f));
                Assert.That(scrollbar.value, Is.EqualTo(0.75f));
                tmpDropdown.onValueChanged.Invoke(0);
                Assert.That(selection.Value, Is.Zero);
                Assert.That(dropdown.value, Is.Zero);

                bindings.Dispose();
                input.onValueChanged.Invoke("detached");
                Assert.That(name.Value, Is.EqualTo("Alice"));

                var rebound = UIDataBinding.BindInputField(input, name);
                input.onValueChanged.Invoke("rebound");
                Assert.That(name.Value, Is.EqualTo("rebound"));
                Assert.That(nameChanges, Is.EqualTo(2));
                UnityEngine.Object.DestroyImmediate(input.gameObject);
                Assert.DoesNotThrow(() => name.Value = "after-destroy");
                Assert.DoesNotThrow(rebound.Dispose);
            }
            finally
            {
                if (!bindings.IsDisposed)
                {
                    bindings.Dispose();
                }

                for (var index = objects.Count - 1; index >= 0; index--)
                {
                    if (objects[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(objects[index]);
                    }
                }
            }
        }

        [Test]
        public void TypedSliderAndToggleBinding_WarmedUpdatesStayWithinManagedBudget()
        {
            var objects = new List<GameObject>();
            var progress = new ObservableProperty<float>(0.25f);
            var enabled = new ObservableProperty<bool>(false);
            var slider = Create<Slider>("GcSlider", objects);
            var toggle = Create<Toggle>("GcToggle", objects);
            var bindings = new BindingToken();

            try
            {
                bindings.Add(UIDataBinding.BindSlider(slider, progress));
                bindings.Add(UIDataBinding.BindToggle(toggle, enabled));
                for (var index = 0; index < 64; index++)
                {
                    progress.Value = (index & 1) == 0 ? 0.25f : 0.75f;
                    enabled.Value = (index & 1) == 0;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var index = 0; index < 10000; index++)
                {
                    progress.Value = (index & 1) == 0 ? 0.25f : 0.75f;
                    enabled.Value = (index & 1) == 0;
                }

                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine(
                    $"Typed Slider/Toggle binding steady-state allocation: {allocated} bytes / 10000 paired updates.");
                Assert.That(
                    allocated,
                    Is.LessThanOrEqualTo(4096),
                    "The budget covers Editor/Mono and Unity control bookkeeping only; binding updates must not allocate per change.");
                Assert.That(slider.value, Is.EqualTo(0.75f));
                Assert.That(toggle.isOn, Is.False);
            }
            finally
            {
                bindings.Dispose();
                for (var index = objects.Count - 1; index >= 0; index--)
                {
                    if (objects[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(objects[index]);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator ButtonBinding_EnforcesCanExecuteAndBusyWithoutDuplicateRuns()
        {
            var buttonObject = new GameObject(
                "CommandButton",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            var button = buttonObject.GetComponent<Button>();
            var gate = new UniTaskCompletionSource();
            var executions = 0;
            var allowed = true;
            var command = new UIAsyncCommand(
                async cancellationToken =>
                {
                    executions++;
                    await gate.Task.AttachExternalCancellation(cancellationToken);
                },
                () => allowed);
            var binding = UIDataBinding.BindButton(button, command);

            try
            {
                Assert.That(button.interactable, Is.True);
                button.onClick.Invoke();
                button.onClick.Invoke();
                Assert.That(executions, Is.EqualTo(1));
                Assert.That(command.IsExecuting, Is.True);
                Assert.That(button.interactable, Is.False);

                gate.TrySetResult();
                yield return null;
                Assert.That(command.IsExecuting, Is.False);
                Assert.That(button.interactable, Is.True);

                allowed = false;
                command.NotifyCanExecuteChanged();
                button.onClick.Invoke();
                Assert.That(executions, Is.EqualTo(1));
                Assert.That(button.interactable, Is.False);
            }
            finally
            {
                binding.Dispose();
                command.Dispose();
                UnityEngine.Object.Destroy(buttonObject);
            }
        }

        [UnityTest]
        public IEnumerator ButtonRebind_OldAsyncCompletionCannotChangeNewBinding()
        {
            var buttonObject = new GameObject(
                "ReboundCommandButton",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            var button = buttonObject.GetComponent<Button>();
            var gate = new UniTaskCompletionSource();
            var oldCommand = new UIAsyncCommand(
                async cancellationToken =>
                {
                    await gate.Task.AttachExternalCancellation(cancellationToken);
                });
            var newCommand = new UICommand(() => { });
            var oldBinding = UIDataBinding.BindButton(button, oldCommand);
            BindingToken newBinding = null;

            try
            {
                button.onClick.Invoke();
                Assert.That(oldCommand.IsExecuting, Is.True);
                Assert.That(button.interactable, Is.False);

                oldBinding.Dispose();
                newBinding = UIDataBinding.BindButton(button, newCommand);
                Assert.That(button.interactable, Is.True);

                gate.TrySetResult();
                yield return null;
                Assert.That(oldCommand.IsExecuting, Is.False);
                Assert.That(button.interactable, Is.True);
            }
            finally
            {
                if (!oldBinding.IsDisposed)
                {
                    oldBinding.Dispose();
                }

                newBinding?.Dispose();
                oldCommand.Dispose();
                newCommand.Dispose();
                UnityEngine.Object.Destroy(buttonObject);
            }
        }

        [Test]
        public void ButtonBinding_SetupFailureRemovesCommandAndUnityListeners()
        {
            var buttonObject = new GameObject(
                "FailingCommandButton",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            var button = buttonObject.GetComponent<Button>();
            var command = new FailingCanExecuteCommand
            {
                ThrowOnCanExecute = true
            };

            try
            {
                Assert.Throws<InvalidOperationException>(
                    () => UIDataBinding.BindButton(button, command));
                Assert.That(command.ObserverCount, Is.Zero);

                command.ThrowOnCanExecute = false;
                button.onClick.Invoke();
                Assert.That(command.ExecuteCount, Is.Zero);
            }
            finally
            {
                command.Dispose();
                UnityEngine.Object.DestroyImmediate(buttonObject);
            }
        }

        [UnityTest]
        public IEnumerator PoolReuse_OneThousandCyclesKeepsDisplayAndLifetimeOwnership()
        {
            var config = Register<LifecycleBindingPage>(
                "Stage9/Lifecycle",
                cache: true,
                transition: false);
            var external = new TestViewModel();
            LifecycleBindingPage.ExpectedViewModel = external;

            for (var cycle = 0; cycle < 1000; cycle++)
            {
                var open = _manager.OpenAsync<LifecycleBindingPage>(external).AsTask();
                yield return Await(open, 30);
                Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(2));
                Assert.That(open.Result.CurrentViewModel, Is.SameAs(external));
                _manager.MessageCenter.Publish(LifetimeTopic, cycle);
                _manager.MessageCenter.Publish(DisplayTopic, cycle);
                var close = _manager.CloseAsync(open.Result).AsTask();
                yield return Await(close, 30);
                Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(1));
            }

            Assert.That(LifecycleBindingPage.InitCount, Is.EqualTo(1));
            Assert.That(LifecycleBindingPage.LifetimeMessages, Is.EqualTo(1000));
            Assert.That(LifecycleBindingPage.DisplayMessages, Is.EqualTo(1000));
            Assert.That(LifecycleBindingPage.DisplayBindingDisposals, Is.EqualTo(1000));
            Assert.That(external.IsDisposed, Is.False);

            _manager.ClearPool<LifecycleBindingPage>();
            Assert.That(_manager.MessageCenter.ListenerCount, Is.Zero);
            Assert.That(LifecycleBindingPage.LifetimeBindingDisposals, Is.EqualTo(1));
            Assert.That(external.IsDisposed, Is.False);
            config.CacheOnClose = false;
        }

        [UnityTest]
        public IEnumerator CanceledCloseRollbackPreservesDisplayBindingAndOwnedExternalPolicies()
        {
            var transition = new CancellationGateTransition();
            _manager.Transitions.RegisterCustom("stage9-cancel", transition);
            var config = Register<LifecycleBindingPage>(
                "Stage9/CanceledClose",
                cache: true,
                transition: true);
            config.TransitionType = UITransitionType.Custom;
            config.CustomTransitionId = "stage9-cancel";
            var external = new TestViewModel();
            LifecycleBindingPage.ExpectedViewModel = external;
            var open = _manager.OpenAsync<LifecycleBindingPage>(external).AsTask();
            yield return Await(open, 30);
            var page = open.Result;

            using var cancellation = new CancellationTokenSource();
            var close = _manager.CloseAsync(page, cancellation.Token).AsTask();
            for (var frame = 0; frame < 30 && !transition.HideStarted; frame++)
            {
                yield return null;
            }

            Assert.That(transition.HideStarted, Is.True);
            cancellation.Cancel();
            yield return AwaitCompletion(close, 30);
            Assert.That(
                close.IsCanceled ||
                close.Exception?.GetBaseException() is OperationCanceledException,
                Is.True);
            Assert.That(page.State, Is.EqualTo(UIContextState.Opened));
            _manager.MessageCenter.Publish(DisplayTopic, 1);
            Assert.That(LifecycleBindingPage.DisplayMessages, Is.EqualTo(1));
            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(2));

            config.UseTransition = false;
            yield return Await(_manager.CloseAsync(page).AsTask(), 30);
            Assert.That(external.IsDisposed, Is.False);
            var scope = _manager.CreateModuleScope("stage9");
            var scopedOpen = _manager.OpenInScopeAsync<LifecycleBindingPage>(
                    scope,
                    external)
                .AsTask();
            yield return Await(scopedOpen, 30);
            yield return Await(
                _manager.CloseAsync(scopedOpen.Result).AsTask(),
                30);
            yield return Await(
                _manager.ReleaseScopeAsync(scope).AsTask(),
                30);
            Assert.That(external.IsDisposed, Is.False);

            Register<OwnedViewModelPage>(
                "Stage9/Owned",
                cache: false,
                transition: false);
            var ownedOpen = _manager.OpenAsync<OwnedViewModelPage>().AsTask();
            yield return Await(ownedOpen, 30);
            yield return Await(
                _manager.CloseAsync(ownedOpen.Result).AsTask(),
                30);
            Assert.That(OwnedViewModelPage.LastViewModel.IsDisposed, Is.True);
        }

        [UnityTest]
        public IEnumerator FailedOpenScopeReleaseAndShutdownCleanLifecycleOwners()
        {
            Register<FailingOpenPage>(
                "Stage9/FailingOpen",
                cache: false,
                transition: false);
            var failedOpen = _manager.OpenAsync<FailingOpenPage>().AsTask();
            yield return AwaitCompletion(failedOpen, 30);
            Assert.That(failedOpen.IsFaulted, Is.True);
            Assert.That(
                failedOpen.Exception?.GetBaseException(),
                Is.TypeOf<UILifecycleException>());
            Assert.That(_manager.MessageCenter.ListenerCount, Is.Zero);
            Assert.That(FailingOpenPage.LastViewModel.IsDisposed, Is.True);
            Assert.That(FailingOpenPage.DisplayBindingDisposals, Is.EqualTo(1));

            Register<LifecycleBindingPage>(
                "Stage9/ScopeAndShutdown",
                cache: true,
                transition: false);
            var external = new TestViewModel();
            LifecycleBindingPage.ExpectedViewModel = external;
            var scope = _manager.CreateModuleScope("stage9.active");
            var scopedOpen = _manager.OpenInScopeAsync<LifecycleBindingPage>(
                    scope,
                    external)
                .AsTask();
            yield return Await(scopedOpen, 30);
            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(2));

            yield return Await(_manager.ReleaseScopeAsync(scope).AsTask(), 60);
            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(2));
            Assert.That(external.IsDisposed, Is.False);
            yield return Await(
                _manager.CloseAsync(scopedOpen.Result).AsTask(),
                30);
            Assert.That(_manager.MessageCenter.ListenerCount, Is.Zero);

            var activeOpen = _manager.OpenAsync<LifecycleBindingPage>(external).AsTask();
            yield return Await(activeOpen, 30);
            Assert.That(_manager.MessageCenter.ListenerCount, Is.EqualTo(2));

            var messageCenter = _manager.MessageCenter;
            yield return Await(_manager.ShutdownAsync().AsTask(), 120);
            Assert.That(messageCenter.ListenerCount, Is.Zero);
            Assert.That(external.IsDisposed, Is.False);
            Assert.That(LifecycleBindingPage.DisplayBindingDisposals, Is.EqualTo(2));
            Assert.That(LifecycleBindingPage.LifetimeBindingDisposals, Is.EqualTo(2));
        }

        private UIConfig Register<T>(
            string key,
            bool cache,
            bool transition)
            where T : BaseContext
        {
            var config = new UIConfig
            {
                Id = typeof(T).Name,
                PrefabKey = key,
                Layer = UILayer.Normal,
                CacheOnClose = cache,
                MaxPoolSize = cache ? 1 : 0,
                FullScreen = true,
                UseTransition = transition,
                TransitionType = transition
                    ? UITransitionType.Fade
                    : UITransitionType.None,
                ShowDuration = transition ? 0.05f : 0f,
                HideDuration = transition ? 0.05f : 0f
            };
            _manager.Register<T>(config);
            return config;
        }

        private static T Create<T>(
            string name,
            List<GameObject> objects,
            Transform parent = null)
            where T : Component
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.SetActive(false);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            objects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }

        private static IEnumerator Await(Task task, int frameLimit)
        {
            yield return AwaitCompletion(task, frameLimit);
            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception;
            }

            Assert.That(task.IsCanceled, Is.False);
        }

        private static IEnumerator Await<T>(Task<T> task, int frameLimit)
        {
            yield return AwaitCompletion(task, frameLimit);
            if (task.IsFaulted)
            {
                throw task.Exception?.GetBaseException() ?? task.Exception;
            }

            Assert.That(task.IsCanceled, Is.False);
        }

        private static IEnumerator AwaitCompletion(Task task, int frameLimit)
        {
            for (var frame = 0; frame < frameLimit && !task.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Timed out waiting for task.");
        }

        public sealed class TestViewModel : ViewModelBase
        {
        }

        private sealed class LifecycleBindingPage : BasePageContext
        {
            private bool _lifetimeBindingTracked;

            public static TestViewModel ExpectedViewModel;
            public static int InitCount;
            public static int LifetimeMessages;
            public static int DisplayMessages;
            public static int LifetimeBindingDisposals;
            public static int DisplayBindingDisposals;

            public override UILayer DefaultLayer => UILayer.Normal;
            public TestViewModel CurrentViewModel => GetViewModel<TestViewModel>();

            public static void Reset()
            {
                ExpectedViewModel = null;
                InitCount = 0;
                LifetimeMessages = 0;
                DisplayMessages = 0;
                LifetimeBindingDisposals = 0;
                DisplayBindingDisposals = 0;
            }

            protected override void HandleInit()
            {
                InitCount++;
                SubscribeMessage(LifetimeTopic, _ => LifetimeMessages++);
            }

            protected override void HandleShow(object args)
            {
                var viewModel = args as TestViewModel ?? ExpectedViewModel;
                if (!ReferenceEquals(CurrentViewModel, viewModel))
                {
                    SetViewModel(viewModel, UIViewModelOwnership.External);
                    _lifetimeBindingTracked = false;
                }

                if (!_lifetimeBindingTracked)
                {
                    TrackBinding(
                        new CallbackDisposable(
                            () => LifetimeBindingDisposals++));
                    _lifetimeBindingTracked = true;
                }

                SubscribeDisplayMessage(DisplayTopic, _ => DisplayMessages++);
                TrackDisplayBinding(
                    new CallbackDisposable(
                        () => DisplayBindingDisposals++));
            }
        }

        private sealed class OwnedViewModelPage : BasePageContext
        {
            public static TestViewModel LastViewModel;
            public override UILayer DefaultLayer => UILayer.Normal;

            public static void Reset()
            {
                LastViewModel = null;
            }

            protected override void HandleInit()
            {
                LastViewModel = new TestViewModel();
                SetViewModel(LastViewModel);
            }
        }

        private sealed class FailingOpenPage : BasePageContext
        {
            public static TestViewModel LastViewModel;
            public static int DisplayBindingDisposals;
            public override UILayer DefaultLayer => UILayer.Normal;

            public static void Reset()
            {
                LastViewModel = null;
                DisplayBindingDisposals = 0;
            }

            protected override void HandleInit()
            {
                LastViewModel = new TestViewModel();
                SetViewModel(LastViewModel);
            }

            protected override void HandleShow(object args)
            {
                SubscribeDisplayMessage(DisplayTopic, _ => { });
                TrackDisplayBinding(
                    new CallbackDisposable(
                        () => DisplayBindingDisposals++));
                throw new InvalidOperationException("Expected stage 9 open failure.");
            }
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _callback;

            public CallbackDisposable(Action callback)
            {
                _callback = callback;
            }

            public void Dispose()
            {
                var callback = _callback;
                _callback = null;
                callback?.Invoke();
            }
        }

        private sealed class FailingCanExecuteCommand : IUICommand
        {
            private Action _stateChanged;

            public bool ThrowOnCanExecute { get; set; }
            public int ObserverCount { get; private set; }
            public int ExecuteCount { get; private set; }
            public bool CanExecute
            {
                get
                {
                    if (ThrowOnCanExecute)
                    {
                        throw new InvalidOperationException("CanExecute");
                    }

                    return !IsDisposed;
                }
            }

            public bool IsExecuting => false;
            public bool IsDisposed { get; private set; }
            public Exception LastError => null;

            public event Action StateChanged
            {
                add
                {
                    _stateChanged += value;
                    ObserverCount++;
                }
                remove
                {
                    _stateChanged -= value;
                    ObserverCount--;
                }
            }

            public void NotifyCanExecuteChanged()
            {
                _stateChanged?.Invoke();
            }

            public UniTask ExecuteAsync(
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExecuteCount++;
                return UniTask.CompletedTask;
            }

            public void Dispose()
            {
                IsDisposed = true;
                _stateChanged = null;
                ObserverCount = 0;
            }
        }

        private sealed class CancellationGateTransition : IUITransition
        {
            public bool HideStarted { get; private set; }

            public UniTask PlayShowAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }

            public async UniTask PlayHideAsync(
                RectTransform target,
                UITransitionOptions options,
                CancellationToken cancellationToken = default)
            {
                HideStarted = true;
                await UniTask.WaitUntilCanceled(cancellationToken);
            }
        }

        private sealed class BindingResourceLoader : IResourceLoader, IDisposable
        {
            private readonly Dictionary<string, GameObject> _prefabs =
                new Dictionary<string, GameObject>();

            public UniTask<GameObject> LoadPrefabAsync(
                string key,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_prefabs.TryGetValue(key, out var prefab) || prefab == null)
                {
                    prefab = new GameObject(
                        key,
                        typeof(RectTransform),
                        typeof(CanvasGroup),
                        typeof(UIView));
                    prefab.SetActive(false);
                    prefab.hideFlags = HideFlags.DontSave;
                    _prefabs[key] = prefab;
                }

                return UniTask.FromResult(prefab);
            }

            public void Release(string key, GameObject instance)
            {
                if (instance != null)
                {
                    UnityEngine.Object.Destroy(instance);
                }
            }

            public void Dispose()
            {
                foreach (var prefab in _prefabs.Values)
                {
                    if (prefab != null)
                    {
                        UnityEngine.Object.Destroy(prefab);
                    }
                }

                _prefabs.Clear();
            }
        }
    }
}
