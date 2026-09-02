using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// MVVM 基础绑定示例页面。
    /// </summary>
    public sealed class MvvmSamplePage : BasePageContext
    {
        private Text _titleText;
        private Text _countText;
        private Text _progressText;
        private Button _incrementButton;
        private Button _backButton;
        private Toggle _enabledToggle;
        private Slider _progressSlider;
        private UnityAction _incrementAction;
        private UnityAction _backAction;

        protected override void HandleInit()
        {
            var root = View.RectTransform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var panel = CreateUIObject("Panel", root);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.12f, 0.2f, 0.95f);
            StretchFull(panel);

            _titleText = CreateLabel("Title", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(520f, 60f), new Vector2(0f, -60f), 34);
            _countText = CreateLabel("Count", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(520f, 48f), new Vector2(0f, -120f), 28);
            _progressText = CreateLabel("Progress", panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(520f, 42f), new Vector2(0f, -175f), 24);

            _incrementButton = CreateButton("IncrementButton", "Increment", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(280f, 76f), new Vector2(0f, 100f), new Color(0.22f, 0.35f, 0.55f, 1f));
            _backButton = CreateButton("BackButton", "Back", panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(240f, 76f), new Vector2(0f, 60f), new Color(0.18f, 0.23f, 0.32f, 1f));

            _enabledToggle = CreateToggle("EnabledToggle", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(360f, 48f), new Vector2(0f, 10f), "Enabled");
            _progressSlider = CreateSlider("ProgressSlider", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(420f, 42f), new Vector2(0f, -70f));

            var vm = new MvvmSampleViewModel();
            SetViewModel(vm);

            TrackBinding(UIDataBinding.BindText(_titleText, vm.Title));
            TrackBinding(UIDataBinding.BindText(_countText, vm.ClickCount, value => $"Click Count: {value}"));
            TrackBinding(UIDataBinding.BindText(_progressText, vm.Progress, value => $"Progress: {value:0.00}"));
            TrackBinding(UIDataBinding.BindToggle(_enabledToggle, vm.Enabled, BindingMode.TwoWay));
            TrackBinding(UIDataBinding.BindSlider(_progressSlider, vm.Progress, BindingMode.TwoWay));
            TrackBinding(vm.Enabled.Subscribe(value => _incrementButton.interactable = value));

            _incrementAction = vm.Increment;
            _backAction = async () => await UIManager.Instance.Navigator.BackAsync();
            _incrementButton.onClick.AddListener(_incrementAction);
            _backButton.onClick.AddListener(_backAction);
        }

        protected override void HandleDestroy()
        {
            if (_incrementButton != null && _incrementAction != null)
            {
                _incrementButton.onClick.RemoveListener(_incrementAction);
            }

            if (_backButton != null && _backAction != null)
            {
                _backButton.onClick.RemoveListener(_backAction);
            }
        }

        private static Text CreateLabel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPosition, int fontSize)
        {
            var textRect = CreateUIObject(name, parent);
            textRect.anchorMin = anchorMin;
            textRect.anchorMax = anchorMax;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = size;
            textRect.anchoredPosition = anchoredPosition;

            var text = textRect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = fontSize;
            return text;
        }

        private static Button CreateButton(string name, string labelText, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPosition, Color color)
        {
            var buttonRect = CreateUIObject(name, parent);
            buttonRect.anchorMin = anchorMin;
            buttonRect.anchorMax = anchorMax;
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = size;
            buttonRect.anchoredPosition = anchoredPosition;

            var image = buttonRect.gameObject.AddComponent<Image>();
            image.color = color;
            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var labelRect = CreateUIObject("Label", buttonRect);
            StretchFull(labelRect);
            var label = labelRect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 28;
            label.text = labelText;
            return button;
        }

        private static Toggle CreateToggle(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPosition, string labelText)
        {
            var toggleRect = CreateUIObject(name, parent);
            toggleRect.anchorMin = anchorMin;
            toggleRect.anchorMax = anchorMax;
            toggleRect.pivot = new Vector2(0.5f, 0.5f);
            toggleRect.sizeDelta = size;
            toggleRect.anchoredPosition = anchoredPosition;

            var backgroundRect = CreateUIObject("Background", toggleRect);
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.pivot = new Vector2(0f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(32f, 32f);
            backgroundRect.anchoredPosition = new Vector2(0f, 0f);
            var backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var checkmarkRect = CreateUIObject("Checkmark", backgroundRect);
            StretchFull(checkmarkRect);
            checkmarkRect.offsetMin = new Vector2(6f, 6f);
            checkmarkRect.offsetMax = new Vector2(-6f, -6f);
            var checkmarkImage = checkmarkRect.gameObject.AddComponent<Image>();
            checkmarkImage.color = new Color(0.2f, 0.7f, 0.4f, 1f);

            var labelRect = CreateUIObject("Label", toggleRect);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(44f, 0f);
            labelRect.offsetMax = Vector2.zero;
            var label = labelRect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.fontSize = 24;
            label.text = labelText;

            var toggle = toggleRect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            return toggle;
        }

        private static Slider CreateSlider(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 anchoredPosition)
        {
            var sliderRect = CreateUIObject(name, parent);
            sliderRect.anchorMin = anchorMin;
            sliderRect.anchorMax = anchorMax;
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.sizeDelta = size;
            sliderRect.anchoredPosition = anchoredPosition;

            var backgroundRect = CreateUIObject("Background", sliderRect);
            StretchFull(backgroundRect);
            var backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
            backgroundImage.color = new Color(0.16f, 0.16f, 0.16f, 1f);

            var fillAreaRect = CreateUIObject("Fill Area", sliderRect);
            StretchFull(fillAreaRect);
            fillAreaRect.offsetMin = new Vector2(10f, 10f);
            fillAreaRect.offsetMax = new Vector2(-10f, -10f);

            var fillRect = CreateUIObject("Fill", fillAreaRect);
            StretchFull(fillRect);
            var fillImage = fillRect.gameObject.AddComponent<Image>();
            fillImage.color = new Color(0.2f, 0.58f, 0.9f, 1f);

            var handleAreaRect = CreateUIObject("Handle Slide Area", sliderRect);
            StretchFull(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);

            var handleRect = CreateUIObject("Handle", handleAreaRect);
            handleRect.sizeDelta = new Vector2(26f, 42f);
            var handleImage = handleRect.gameObject.AddComponent<Image>();
            handleImage.color = new Color(0.9f, 0.9f, 0.9f, 1f);

            var slider = sliderRect.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        private static RectTransform CreateUIObject(string name, RectTransform parent)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
