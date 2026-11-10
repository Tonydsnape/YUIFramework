using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    public sealed class SystemUISamplePage : BasePageContext
    {
        private SystemUISampleSession _session;
        private Text _status;
        private Button _first;
        public override bool IsFullScreen => false;
        public override GameObject DefaultFocus => _first?.gameObject;
        protected override void HandleInit()
        {
            var content = SystemUISampleLayout.Panel(this, TrackBinding, .06f, .94f);
            SystemUISampleLayout.Text(content, "Stage 12 - scene and system UI", 30);
            _status = SystemUISampleLayout.Text(content, "Global controls; operations belong to the active scene.", 24);
            _first = SystemUISampleLayout.Button(content, "Two independent loading operations",
                () => _session.RunLoadingAsync().Forget(Debug.LogException));
            SystemUISampleLayout.Button(content, "100 merged Toast requests", () => _session.ToastBurst());
            SystemUISampleLayout.Button(content, "Scene confirmation", () => _session.ConfirmAsync().Forget(Debug.LogException));
            SystemUISampleLayout.Button(content, "Change active scene", () => _session.ChangeSceneAsync().Forget(Debug.LogException));
            foreach (var notice in new[] { UISystemNotice.Disconnected, UISystemNotice.Maintenance, UISystemNotice.ForceUpdate })
            {
                var selected = notice;
                SystemUISampleLayout.Button(content, "Global: " + notice,
                    () => _session.ShowSystemAsync(selected).Forget(Debug.LogException));
            }
            // The one sample driver advances only the bounded Toast scheduler, not view state.
            ViewObject.AddComponent<SystemUISampleDriver>().Bind(() => _session?.Tick());
        }
        protected override void HandleShow(object args)
        {
            _session = args as SystemUISampleSession ?? throw new ArgumentException("A sample session is required.", nameof(args));
            var session = _session;
            void Status(string text) { _status.text = text; }
            session.StatusChanged += Status;
            TrackDisplayBinding(new BindingToken(() => { session.StatusChanged -= Status; _session = null; }));
        }
    }

    public sealed class SampleLoadingContext : BaseWidgetContext
    {
        private Text _message, _progress;
        private Slider _slider;
        private Button _cancel;
        private UILoadingService _loading;
        public override UILayer DefaultLayer => UILayer.Loading;
        public override GameObject DefaultFocus => _cancel?.gameObject;
        protected override void HandleInit()
        {
            var content = SystemUISampleLayout.Panel(this, TrackBinding);
            _message = SystemUISampleLayout.Text(content, "", 30);
            _progress = SystemUISampleLayout.Text(content, "", 24);
            var sliderObject = new GameObject("Progress", typeof(RectTransform), typeof(LayoutElement), typeof(Slider));
            sliderObject.transform.SetParent(content, false);
            sliderObject.GetComponent<LayoutElement>().preferredHeight = 24;
            _slider = sliderObject.GetComponent<Slider>();
            _slider.interactable = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(sliderObject.transform, false);
            var rect = (RectTransform)fill.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color(.3f, .7f, 1);
            fill.GetComponent<Image>().raycastTarget = false;
            _slider.fillRect = rect;
            _cancel = SystemUISampleLayout.Button(content, "Cancel operations", () => _loading?.CancelCancelable());
        }
        protected override void HandleShow(object args)
        {
            _loading = args as UILoadingService ?? throw new ArgumentException("A loading service is required.", nameof(args));
            var loading = _loading;
            loading.Changed += Refresh;
            TrackDisplayBinding(new BindingToken(() => { loading.Changed -= Refresh; _loading = null; }));
            Refresh(loading.State);
        }
        private void Refresh(UILoadingState state)
        {
            _message.text = state.Message;
            _progress.text = state.Progress.HasValue ? state.Progress.Value.ToString("P0") : "Working...";
            _slider.gameObject.SetActive(state.Progress.HasValue);
            _slider.SetValueWithoutNotify(state.Progress ?? 0);
            _cancel.gameObject.SetActive(state.CanCancel);
        }
    }

    public sealed class SampleToastContext : BaseWidgetContext
    {
        private Text _text;
        public override UILayer DefaultLayer => UILayer.Toast;
        protected override void HandleInit()
        {
            var content = SystemUISampleLayout.Panel(this, TrackBinding, .75f, .95f);
            _text = SystemUISampleLayout.Text(content, "", 28);
            foreach (var graphic in ViewObject.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
        protected override void HandleShow(object args)
        {
            var service = args as UIToastService ?? throw new ArgumentException("A Toast service is required.", nameof(args));
            service.Changed += Refresh;
            TrackDisplayBinding(new BindingToken(() => service.Changed -= Refresh));
            Refresh(service.Current);
        }
        private void Refresh(UIToastItem item) =>
            _text.text = item == null ? "" : item.Request.Text + (item.Count > 1 ? " (x" + item.Count + ")" : "");
    }

    public class SampleConfirmationContext : UIConfirmationContext
    {
        private Text _title, _message, _confirmLabel, _cancelLabel;
        private Button _confirm, _cancel;
        public override GameObject DefaultFocus => _confirm?.gameObject;
        protected override void HandleInit()
        {
            var content = SystemUISampleLayout.Panel(this, TrackBinding);
            _title = SystemUISampleLayout.Text(content, "", 30);
            _message = SystemUISampleLayout.Text(content, "", 24);
            _confirm = SystemUISampleLayout.Button(content, "", () => Session?.Confirm());
            _cancel = SystemUISampleLayout.Button(content, "", () => Session?.Decline());
            _confirmLabel = _confirm.GetComponentInChildren<Text>();
            _cancelLabel = _cancel.GetComponentInChildren<Text>();
        }
        protected override void Present(UIConfirmationSession session)
        {
            _title.text = session.Request.Title;
            _message.text = session.Request.Message;
            _confirmLabel.text = session.Request.ConfirmLabel;
            _cancelLabel.text = session.Request.CancelLabel;
            _cancel.gameObject.SetActive(session.Request.AllowCancel);
        }
    }

    public sealed class SampleSystemContext : SampleConfirmationContext
    {
        public override UILayer DefaultLayer => UILayer.System;
    }

    internal static class SystemUISampleLayout
    {
        internal static RectTransform Panel(BaseContext context, Action<IDisposable> track,
            float bottom = .2f, float top = .8f)
        {
            var root = context.View.RectTransform;
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var safe = new GameObject("SafeContent", typeof(RectTransform), typeof(UISafeAreaContent));
            safe.transform.SetParent(root, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            track(safe.GetComponent<UISafeAreaContent>().Bind(root.GetComponentInParent<UIScreenLayout>()));
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            panel.transform.SetParent(safe.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(.08f, bottom); rect.anchorMax = new Vector2(.92f, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(.08f, .12f, .2f, .97f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(rect, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(16, 16); viewportRect.offsetMax = new Vector2(-16, -16);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(.5f, 1); contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = panel.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect; scroll.content = contentRect; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return contentRect;
        }
        internal static Text Text(Transform parent, string value, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size; text.color = Color.white; text.alignment = TextAnchor.MiddleCenter;
            text.text = value; text.raycastTarget = false;
            go.GetComponent<LayoutElement>().minHeight = size + 12;
            return text;
        }
        internal static Button Button(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = 64;
            var button = go.GetComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            go.GetComponent<Image>().color = new Color(.18f, .32f, .5f);
            var text = Text(go.transform, label, 24);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            button.onClick.AddListener(action);
            return button;
        }
    }
}
