using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class UIRootRuntimeOptions
    {
        public UILayerProfile LayerProfile { get; set; }
        public RenderMode RenderMode { get; set; } = RenderMode.ScreenSpaceOverlay;
        public Camera EventCamera { get; set; }
        public bool DontDestroyOnLoad { get; set; } = true;
    }

    /// <summary>
    /// Explicit ownership boundary for UIRoot, EventSystem, layer services and input.
    /// </summary>
    public sealed class UIRootRuntime : IDisposable
    {
        private readonly UIInputDriver _inputDriver;
        private bool _disposed;

        private UIRootRuntime(
            UIRoot root,
            EventSystem eventSystem,
            bool ownsRoot,
            bool ownsEventSystem,
            UIRootRuntimeOptions options)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            EventSystem = eventSystem ?? throw new ArgumentNullException(nameof(eventSystem));
            OwnsRoot = ownsRoot;
            OwnsEventSystem = ownsEventSystem;
            Options = options ?? new UIRootRuntimeOptions();
            LayerProfile = Options.LayerProfile ?? UILayerProfile.CreateDefault();

            ValidateEventSystem(EventSystem);
            Root.Claim(this);
            try
            {
                Root.Configure(LayerProfile, Options.RenderMode, Options.EventCamera);
                LayerManager = new UILayerManager(Root, LayerProfile);
                Focus = new UIFocusService(EventSystem);
                Interaction = new UIInteractionController(LayerManager, LayerProfile, Focus);
                InputLocks = new UIInputLockService(LayerProfile, Interaction);
                Modals = new UIModalService(Root, LayerManager, Interaction);
                Interaction.Bind(InputLocks, Modals);
                Input = new UIInputRouter();
                _inputDriver = Root.gameObject.AddComponent<UIInputDriver>();
                _inputDriver.Bind(Input, EventSystem);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public UIRoot Root { get; }
        public EventSystem EventSystem { get; }
        public bool OwnsRoot { get; }
        public bool OwnsEventSystem { get; }
        public UIRootRuntimeOptions Options { get; }
        public UILayerProfile LayerProfile { get; }
        public UILayerManager LayerManager { get; }
        public UIInputLockService InputLocks { get; }
        public UIInteractionController Interaction { get; }
        public UIFocusService Focus { get; }
        public UIModalService Modals { get; }
        public UIInputRouter Input { get; }
        public bool IsDisposed => _disposed;

        public static UIRootRuntime CreateOwned(UIRootRuntimeOptions options = null)
        {
            options = options ?? new UIRootRuntimeOptions();
            if (UIRoot.Active != null && UIRoot.Active.IsClaimed)
            {
                throw new InvalidOperationException("An active UIRoot runtime already exists.");
            }

            if (EventSystem.current != null)
            {
                throw new InvalidOperationException(
                    "An EventSystem already exists. Inject it explicitly with CreateExternal.");
            }

            var rootObject = new GameObject(
                "YUIRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(UIRoot));
            var eventObject = new GameObject(
                "YUIEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            if (options.DontDestroyOnLoad && Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(rootObject);
                UnityEngine.Object.DontDestroyOnLoad(eventObject);
            }

            try
            {
                return new UIRootRuntime(
                    rootObject.GetComponent<UIRoot>(),
                    eventObject.GetComponent<EventSystem>(),
                    true,
                    true,
                    options);
            }
            catch
            {
                DestroyObject(rootObject);
                DestroyObject(eventObject);
                throw;
            }
        }

        public static UIRootRuntime CreateExternal(
            UIRoot root,
            EventSystem eventSystem,
            UIRootRuntimeOptions options = null,
            bool ownsEventSystem = false)
        {
            return new UIRootRuntime(
                root,
                eventSystem,
                false,
                ownsEventSystem,
                options ?? new UIRootRuntimeOptions());
        }

        internal static UIRootRuntime CreateCompatible(UIRootRuntimeOptions options = null)
        {
            options = options ?? new UIRootRuntimeOptions();
            var root = UIRoot.Active;
            if (root == null)
            {
                return CreateOwned(options);
            }

            if (root.IsClaimed)
            {
                throw new InvalidOperationException("The active UIRoot is already claimed.");
            }

            var currentEventSystem = EventSystem.current;
            if (currentEventSystem != null)
            {
                return CreateExternal(root, currentEventSystem, options);
            }

            var eventObject = new GameObject(
                "YUIEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            if (options.DontDestroyOnLoad && Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(eventObject);
            }

            return CreateExternal(
                root,
                eventObject.GetComponent<EventSystem>(),
                options,
                true);
        }

        public void BindNavigator(IUINavigator navigator)
        {
            ThrowIfDisposed();
            Input.Bind(navigator, InputLocks);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Input?.Dispose();
            if (_inputDriver != null)
            {
                DestroyObject(_inputDriver);
            }
            Modals?.Dispose();
            Focus?.Clear();
            InputLocks?.Dispose();
            Interaction?.Dispose();
            LayerManager?.Dispose();
            if (Root != null)
            {
                Root.Release(this);
            }

            if (OwnsRoot && Root != null)
            {
                DestroyObject(Root.gameObject);
            }

            if (OwnsEventSystem && EventSystem != null)
            {
                EventSystem.enabled = false;
                DestroyObject(EventSystem.gameObject);
            }
        }

        private static void ValidateEventSystem(EventSystem eventSystem)
        {
            if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                throw new InvalidOperationException(
                    "The injected EventSystem must have a BaseInputModule.");
            }

            if (EventSystem.current != null && EventSystem.current != eventSystem)
            {
                throw new InvalidOperationException(
                    $"A different EventSystem is current: {EventSystem.current.name}.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(UIRootRuntime));
            }
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
