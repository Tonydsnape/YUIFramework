using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using UnityEngine.UI;
using YUIFramework.Configuration;
using YUIFramework.ConfigGenerated;
using YUIFramework.Localization;

namespace YUIFramework
{
    public sealed class ReleaseCandidateViewModel : ViewModelBase
    {
        public readonly ObservableProperty<int> Count = new ObservableProperty<int>();
        public readonly ObservableCollection<string> Items = new ObservableCollection<string>();
        public UIAsyncCommand Add { get; }
        public ReleaseCandidateViewModel()
        {
            for (var i = 0; i < 3; i++) Items.Add("Item " + i);
            Add = new UIAsyncCommand(async token =>
            {
                await UniTask.Yield(token);
                Count.Value++;
                if (Items.Count == 12) Items.RemoveAt(0);
                Items.Add("Added " + Count.Value);
            });
            TrackDisposable(Add);
        }
    }

    public sealed class ReleaseCandidateFlow : IDisposable
    {
        private readonly Scene _original = SceneManager.GetActiveScene();
        private readonly List<Scene> _scenes = new List<Scene>();
        public UIManager UI { get; }
        public TextLocalizationService Text { get; }
        public ReleaseCandidateViewModel Model { get; } = new ReleaseCandidateViewModel();
        public RcHomePage Home { get; private set; }
        public RcPopup Popup { get; private set; }

        public ReleaseCandidateFlow(UIManager ui, TextLocalizationService text)
        { UI = ui ?? throw new ArgumentNullException(nameof(ui)); Text = text ?? throw new ArgumentNullException(nameof(text)); }

        public async UniTask EnterAsync(ConfigSnapshot snapshot, CancellationToken token)
        {
            var table = snapshot.Get(UISettings.Table);
            var home = UISettingsMapping.Convert(table.Get("hello", "HelloPage"));
            home.PreloadCount = 1;
            UI.RegisterBatch(new[]
            {
                UIConfigRegistration.For<RcHomePage>(home),
                UIConfigRegistration.For<RcDetailPage>(UISettingsMapping.Convert(table.Get("hello", "SecondSamplePage"))),
                UIConfigRegistration.For<RcListPage>(UISettingsMapping.Convert(table.Get("hello", "VirtualListSamplePage"))),
                UIConfigRegistration.For<RcPopup>(UISettingsMapping.Convert(table.Get("system", "SystemConfirmation"))),
                UIConfigRegistration.For<RcSceneContext>(UISettingsMapping.Convert(table.Get("y2", "SampleHelloPage")))
            });
            if (await UI.PrewarmAsync<RcHomePage>(token) != 1)
                throw new InvalidOperationException("RC Type-based prewarm did not construct exactly one Context.");
            Home = await UI.Navigator.PushAsync<RcHomePage>(this, cancellationToken: token);
        }

        public async UniTask ShowPopupAsync(CancellationToken token) =>
            Popup = await UI.OpenAsync<RcPopup>(this, token);

        public async UniTask ChangeSceneAsync(CancellationToken token)
        {
            var previous = UI.Scopes.CurrentScene;
            var transient = await previous.OpenAsync<RcSceneContext>(this, token);
            var view = transient.ViewObject;
            var next = SceneManager.CreateScene("YUI-RC-" + Guid.NewGuid().ToString("N"));
            _scenes.Add(next);
            if (!SceneManager.SetActiveScene(next)) throw new InvalidOperationException("Could not activate RC scene.");
            await previous.ReleaseAsync();
            await UniTask.Yield(token);
            if (!previous.Token.IsCancellationRequested || view != null)
                throw new InvalidOperationException("Previous scene UI did not drain.");
            if (Home.ViewObject == null || !Home.ViewObject.activeInHierarchy)
                throw new InvalidOperationException("Global navigation did not survive the scene change.");
        }

        public async UniTask RestoreScenesAsync()
        {
            if (_original.IsValid() && _original.isLoaded && SceneManager.GetActiveScene().handle != _original.handle &&
                !SceneManager.SetActiveScene(_original))
                throw new InvalidOperationException("Could not restore the original scene.");
            var errors = new List<Exception>();
            foreach (var scene in _scenes)
                if (scene.IsValid() && scene.isLoaded)
                {
                    try { await SceneManager.UnloadSceneAsync(scene); }
                    catch (Exception error) { errors.Add(error); }
                }
            _scenes.Clear();
            if (errors.Count != 0) throw new AggregateException("RC scene cleanup failed.", errors);
        }

        public async UniTask<string[]> SmokeAsync(CancellationToken token)
        {
            var checks = new List<string>();
            checks.Add("Type-based Activator prewarm and pooled open");
            Require(UI.Navigator.Count == 1 && Home.ViewObject.activeInHierarchy, "home navigation", checks);
            Home.AddButton.onClick.Invoke();
            await UniTask.WaitUntil(() => !Model.Add.IsExecuting, cancellationToken: token);
            Require(Model.Count.Value == 1 && Home.Counter.text == "1" && Model.Items.Count == 4, "MVVM button and observable list", checks);
            Home.LanguageButton.onClick.Invoke();
            Require(Text.Locale == "fr" && Home.Title.text == "Exemple de localisation", "French localization", checks);
            Home.PopupButton.onClick.Invoke();
            await UniTask.WaitUntil(() => Popup != null && Popup.State == UIContextState.Opened, cancellationToken: token);
            Require(UI.Modals.Count == 1 && UI.Modals.MaskObject.activeInHierarchy, "popup modal ownership", checks);
            Popup.CloseButton.onClick.Invoke();
            await UniTask.WaitUntil(() => UI.Modals.Count == 0, cancellationToken: token);
            Home.DetailButton.onClick.Invoke();
            await UniTask.WaitUntil(() => UI.Navigator.CurrentPage is RcDetailPage page && page.State == UIContextState.Opened, cancellationToken: token);
            Require(UI.Navigator.Count == 2, "navigation push", checks);
            await UI.Navigator.BackAsync(token);
            var list = await UI.Navigator.PushAsync<RcListPage>(this, cancellationToken: token);
            Require(list.Rows.text.Contains("Added 1"), "bounded list initial render", checks);
            await Model.Add.ExecuteAsync(token);
            Require(list.Rows.text.Contains("Added 2"), "observable list refresh", checks);
            await UI.Navigator.BackAsync(token);
            await ChangeSceneAsync(token);
            Require(UI.Navigator.CurrentPage == Home && UI.Modals.Count == 0, "scene UI drain and global survival", checks);
            return checks.ToArray();
        }

        private static void Require(bool condition, string check, List<string> checks)
        {
            if (!condition) throw new InvalidOperationException("RC smoke failed: " + check);
            checks.Add(check);
        }
        public void Dispose() => Model.Dispose();
    }

    internal static class RcWidgets
    {
        public static RectTransform Panel(RectTransform root)
        {
            Stretch(root);
            var panel = new GameObject("Content", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            panel.SetParent(root, false); Stretch(panel);
            panel.GetComponent<Image>().color = new Color(.05f, .08f, .13f, .98f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 24, 24); layout.spacing = 12;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            panel.gameObject.AddComponent<UISafeAreaContent>();
            return panel;
        }
        public static Text Label(Transform parent, string name, string value, float height = 60)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false; text.text = value;
            text.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }
        public static Button Button(Transform parent, string name, string caption)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(.16f, .28f, .45f);
            go.GetComponent<LayoutElement>().preferredHeight = 76;
            var label = Label(go.transform, "Caption", caption);
            Stretch((RectTransform)label.transform);
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            var colors = button.colors; colors.selectedColor = new Color(.5f, .8f, 1); button.colors = colors;
            return button;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }

    [Preserve]
    public sealed class RcHomePage : BasePageContext
    {
        private RectTransform _panel;
        private UISafeAreaContent _safeArea;
        private ReleaseCandidateFlow _flow;
        public Text Title { get; private set; }
        public Text Counter { get; private set; }
        public Button AddButton { get; private set; }
        public Button LanguageButton { get; private set; }
        public Button PopupButton { get; private set; }
        public Button DetailButton { get; private set; }
        public override GameObject DefaultFocus => AddButton ? AddButton.gameObject : null;
        protected override void HandleInit()
        {
            _panel = RcWidgets.Panel(View.RectTransform);
            _safeArea = _panel.GetComponent<UISafeAreaContent>();
            Title = RcWidgets.Label(_panel, "Title", "");
            Counter = RcWidgets.Label(_panel, "Count", "");
            AddButton = RcWidgets.Button(_panel, "Add", "Add item (MVVM)");
            DetailButton = RcWidgets.Button(_panel, "Navigate", "Navigate / Back");
            var list = RcWidgets.Button(_panel, "List", "Observable list (maximum 12)");
            LanguageButton = RcWidgets.Button(_panel, "Language", "English / Francais");
            PopupButton = RcWidgets.Button(_panel, "Popup", "Open modal");
            var scene = RcWidgets.Button(_panel, "Scene", "Change scene / retain global UI");
            DetailButton.onClick.AddListener(() => _flow.UI.Navigator.PushAsync<RcDetailPage>(_flow, cancellationToken: LifetimeToken).Forget(Debug.LogException));
            list.onClick.AddListener(() => _flow.UI.Navigator.PushAsync<RcListPage>(_flow, cancellationToken: LifetimeToken).Forget(Debug.LogException));
            PopupButton.onClick.AddListener(() => _flow.ShowPopupAsync(LifetimeToken).Forget(Debug.LogException));
            scene.onClick.AddListener(() => _flow.ChangeSceneAsync(LifetimeToken).Forget(Debug.LogException));
            LanguageButton.onClick.AddListener(() => _flow.Text.SetLocale(_flow.Text.Locale == "en" ? "fr" : "en"));
        }
        protected override void HandleShow(object args)
        {
            _flow = args as ReleaseCandidateFlow ?? _flow ??
                throw new ArgumentException("The first show requires a ReleaseCandidateFlow.", nameof(args));
            SetViewModel(_flow.Model, UIViewModelOwnership.External);
            TrackDisplayBinding(UIDataBinding.BindButton(AddButton, _flow.Model.Add));
            TrackDisplayBinding(UIDataBinding.BindText(Counter, _flow.Model.Count, value => value.ToString()));
            void Refresh() => Title.text = _flow.Text.Get("sample.title");
            _flow.Text.Changed += Refresh;
            TrackDisplayBinding(new BindingToken(() => _flow.Text.Changed -= Refresh));
            TrackDisplayBinding(_safeArea.Bind(_flow.UI.RootRuntime.ScreenLayout));
            Refresh();
        }
    }

    [Preserve]
    public sealed class RcDetailPage : BasePageContext
    {
        private Button _back;
        private RectTransform _panel;
        public override GameObject DefaultFocus => _back ? _back.gameObject : null;
        protected override void HandleInit()
        {
            _panel = RcWidgets.Panel(View.RectTransform);
            RcWidgets.Label(_panel, "Title", "Navigation detail");
            RcWidgets.Label(_panel, "Explanation", "Back restores the previous page, bindings and focus.", 100);
            _back = RcWidgets.Button(_panel, "Back", "Back");
            _back.onClick.AddListener(() => Services.Navigator.BackAsync(LifetimeToken).Forget(Debug.LogException));
        }
        protected override void HandleShow(object args) => TrackDisplayBinding(_panel.GetComponent<UISafeAreaContent>()
            .Bind(((ReleaseCandidateFlow)args).UI.RootRuntime.ScreenLayout));
    }

    [Preserve]
    public sealed class RcListPage : BasePageContext
    {
        private Button _back;
        private RectTransform _panel;
        public Text Rows { get; private set; }
        public override GameObject DefaultFocus => _back ? _back.gameObject : null;
        protected override void HandleInit()
        {
            _panel = RcWidgets.Panel(View.RectTransform);
            RcWidgets.Label(_panel, "Title", "Bounded observable list, not a virtual-list benchmark");
            Rows = RcWidgets.Label(_panel, "Rows", "", 420);
            _back = RcWidgets.Button(_panel, "Back", "Back");
            _back.onClick.AddListener(() => Services.Navigator.BackAsync(LifetimeToken).Forget(Debug.LogException));
        }
        protected override void HandleShow(object args)
        {
            var flow = (ReleaseCandidateFlow)args;
            TrackDisplayBinding(_panel.GetComponent<UISafeAreaContent>().Bind(flow.UI.RootRuntime.ScreenLayout));
            SetViewModel(flow.Model, UIViewModelOwnership.External);
            void Refresh() => Rows.text = string.Join("\n", flow.Model.Items);
            TrackDisplayBinding(flow.Model.Items.Subscribe(_ => Refresh()));
            Refresh();
        }
    }

    [Preserve]
    public sealed class RcPopup : BaseDialogContext
    {
        private RectTransform _panel;
        public Button CloseButton { get; private set; }
        public override GameObject DefaultFocus => CloseButton ? CloseButton.gameObject : null;
        protected override void HandleInit()
        {
            _panel = RcWidgets.Panel(View.RectTransform);
            RcWidgets.Label(_panel, "Title", "Modal / focus ownership");
            CloseButton = RcWidgets.Button(_panel, "Close", "Close");
            CloseButton.onClick.AddListener(() => Services.CloseAsync<RcPopup>().Forget(Debug.LogException));
        }
        protected override void HandleShow(object args) => TrackDisplayBinding(_panel.GetComponent<UISafeAreaContent>()
            .Bind(((ReleaseCandidateFlow)args).UI.RootRuntime.ScreenLayout));
    }

    [Preserve]
    public sealed class RcSceneContext : BaseContext
    {
        public override UILayer DefaultLayer => UILayer.Normal;
    }
}
