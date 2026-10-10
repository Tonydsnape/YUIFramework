using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YUIFramework.Localization;

namespace YUIFramework
{
    public sealed class PresentationSampleView : MonoBehaviour, IUIAccessibilitySink
    {
        private readonly List<ConfigPresentationBinding> _styles = new List<ConfigPresentationBinding>();
        private readonly List<ConfigLocalizedText> _labels = new List<ConfigLocalizedText>();
        private readonly List<Button> _buttons = new List<Button>();
        private UISafeAreaContent _safe;
        private TMP_Text _greeting, _format, _focus;
        private BindingToken _binding;
        public GameObject FirstFocus => _buttons.Count > 0 ? _buttons[0].gameObject : null;
        public IReadOnlyList<ConfigPresentationBinding> Styles => _styles;
        public TMP_Text Greeting => _greeting;
        public RectTransform SafeContent => (RectTransform)_safe.transform;
        private void Awake()
        {
            var backdrop = gameObject.AddComponent<Image>();
            backdrop.raycastTarget = true;
            Style(gameObject, "panel", material: "surface");
            var safe = Rect("SafeContent", transform);
            Stretch(safe); _safe = safe.gameObject.AddComponent<UISafeAreaContent>();
            safe.gameObject.AddComponent<RectMask2D>();
            var scroll = safe.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = safe;
            var content = Rect("Content", safe);
            content.anchorMin = new Vector2(.04f, 1); content.anchorMax = new Vector2(.96f, 1);
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12; layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var title = Label("Title", content, "sample.title", 60);
            title.fontSize = 36;
            _greeting = Label("Greeting", content, "sample.greeting", 140);
            _greeting.fontSize = 48;
            var badge = Rect("Badge", content);
            badge.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;
            var image = badge.gameObject.AddComponent<Image>(); image.preserveAspect = true; image.raycastTarget = false;
            Style(badge.gameObject, sprite: "badge");
            _format = Label("DateCurrency", content, null, 56);
            foreach (var key in new[] { "sample.switch", "sample.theme", "sample.size", "sample.contrast", "sample.motion", "sample.close" })
            {
                var rect = Rect(key, content);
                rect.gameObject.AddComponent<LayoutElement>().minHeight = 72;
                var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(12,12,12,12);
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = true; row.childForceExpandHeight = false;
                var graphic = rect.gameObject.AddComponent<Image>(); graphic.color = new Color(.35f, .4f, .45f);
                var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = graphic; _buttons.Add(button);
                var label = Label("Label", rect, key, 0);
                label.alignment = TextAlignmentOptions.Center;
                Stretch((RectTransform)label.transform);
                var semantic = rect.gameObject.AddComponent<UIAccessibilitySemantics>();
                semantic.SetLabel(new LocalizedTextKey(key), UIAccessibleRole.Button);
            }
            _focus = Label("FocusFeedback", content, null, 64);
            _focus.fontSize = 24;
        }
        public IDisposable Bind(SampleLocalizedHelloArgs args, BaseContext context, IUIService ui)
        {
            _binding?.Dispose();
            var bindings = new BindingToken(); _binding = bindings;
            bindings.Add(_safe.Bind(ui.RootRuntime.ScreenLayout));
            foreach (var style in _styles) bindings.Add(style.Bind(args.Presentation, context));
            foreach (var label in _labels)
            {
                if (label.TextKey.Key == "sample.greeting") label.SetArguments(args.PlayerName);
                bindings.Add(label.Bind(args.Localization, context));
            }
            Action refreshFormat = () => _format.text = args.Localization.FormatDate(new DateTime(2026, 10, 10)) +
                "  |  " + args.Localization.FormatCurrency(1234.56m);
            refreshFormat(); args.Localization.Changed += refreshFormat;
            bindings.Add(() => args.Localization.Changed -= refreshFormat);
            var actions = new UnityEngine.Events.UnityAction[] {
                () => args.Localization.SetLocale(args.Localization.Locale == "en" ? "zh-CN" : args.Localization.Locale == "zh-CN" ? "fr" : "en"),
                () => args.Presentation.SetTheme(args.Presentation.Theme == "light" ? "dark" : "light"),
                () => { var a=args.Presentation.Accessibility; args.Presentation.SetAccessibility(new UIAccessibilityOptions(a.FontScale < 1.4f ? 1.5f : 1, a.HighContrast, a.ReducedMotion)); },
                () => { var a=args.Presentation.Accessibility; args.Presentation.SetAccessibility(new UIAccessibilityOptions(a.FontScale, !a.HighContrast, a.ReducedMotion)); },
                () => { var a=args.Presentation.Accessibility; args.Presentation.SetAccessibility(new UIAccessibilityOptions(a.FontScale, a.HighContrast, !a.ReducedMotion)); },
                () => ui.CloseAsync(context).Forget(Debug.LogException)
            };
            for (var i=0;i<_buttons.Count;i++)
            {
                var button = _buttons[i]; var action = actions[i];
                button.onClick.AddListener(action); bindings.Add(() => { if (button) button.onClick.RemoveListener(action); });
                bindings.Add(button.GetComponent<UIAccessibilitySemantics>().Bind(args.Localization, this));
            }
            bindings.Add(context.DisplayToken.Register(bindings.Dispose));
            return bindings;
        }
        public void Focused(UIAccessibilitySemantics element, string label, UIAccessibleRole role)
        {
            if (_focus) _focus.text = label;
            if (!_safe) return;
            var scroll = _safe.GetComponent<ScrollRect>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, element.transform);
            var rect = scroll.viewport.rect;
            var delta = bounds.min.y < rect.yMin ? rect.yMin - bounds.min.y :
                bounds.max.y > rect.yMax ? rect.yMax - bounds.max.y : 0;
            var position = scroll.content.anchoredPosition;
            position.y = Mathf.Clamp(position.y + delta, 0, Mathf.Max(0, scroll.content.rect.height - rect.height));
            scroll.StopMovement(); scroll.content.anchoredPosition = position;
        }
        private TMP_Text Label(string name, Transform parent, string key, float height)
        {
            var rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = 30; text.color = Color.black; text.raycastTarget = false; text.enableWordWrapping = true;
            if (height > 0) rect.gameObject.AddComponent<LayoutElement>().minHeight = height;
            Style(rect.gameObject, "text", "body");
            if (key != null)
            {
                var localize = rect.gameObject.AddComponent<ConfigLocalizedText>();
                localize.TextKey = new LocalizedTextKey(key); _labels.Add(localize);
            }
            return text;
        }
        private void Style(GameObject target, string color = "", string font = "", string sprite = "", string material = "")
        {
            var style = target.AddComponent<ConfigPresentationBinding>();
            style.SetTokens(color, font, sprite, material); _styles.Add(style);
        }
        private static RectTransform Rect(string name, Transform parent)
        { var rect=(RectTransform)new GameObject(name, typeof(RectTransform)).transform; rect.SetParent(parent,false); return rect; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero; }
        private void OnDestroy() { _binding?.Dispose(); }
    }
}
