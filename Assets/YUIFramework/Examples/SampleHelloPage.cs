using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// 示例页面：代码创建 UI，演示生命周期与关闭流程。
    /// </summary>
    public class SampleHelloPage : BasePageContext
    {
        private Text _messageText;
        private Button _closeButton;
        private Button _nextButton;
        private UnityAction _closeAction;
        private UnityAction _nextAction;

        protected override void HandleInit()
        {
            var root = View.RectTransform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var panel = CreateUIObject("Panel", root);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.65f);
            StretchFull(panel);

            var textRect = CreateUIObject("Message", panel);
            _messageText = textRect.gameObject.AddComponent<Text>();
            _messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.color = Color.white;
            _messageText.fontSize = 42;
            StretchFull(textRect);

            var buttonRect = CreateUIObject("CloseButton", panel);
            buttonRect.sizeDelta = new Vector2(260f, 80f);
            buttonRect.anchorMin = new Vector2(0.5f, 0f);
            buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 80f);

            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            _closeButton = buttonRect.gameObject.AddComponent<Button>();
            _closeButton.targetGraphic = buttonImage;

            var labelRect = CreateUIObject("Label", buttonRect);
            StretchFull(labelRect);
            var label = labelRect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 28;
            label.text = "Close";

            var nextRect = CreateUIObject("NextButton", panel);
            nextRect.sizeDelta = new Vector2(260f, 80f);
            nextRect.anchorMin = new Vector2(0.5f, 0f);
            nextRect.anchorMax = new Vector2(0.5f, 0f);
            nextRect.pivot = new Vector2(0.5f, 0f);
            nextRect.anchoredPosition = new Vector2(0f, 180f);

            var nextImage = nextRect.gameObject.AddComponent<Image>();
            nextImage.color = new Color(0.24f, 0.24f, 0.24f, 0.95f);
            _nextButton = nextRect.gameObject.AddComponent<Button>();
            _nextButton.targetGraphic = nextImage;

            var nextLabelRect = CreateUIObject("Label", nextRect);
            StretchFull(nextLabelRect);
            var nextLabel = nextLabelRect.gameObject.AddComponent<Text>();
            nextLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nextLabel.alignment = TextAnchor.MiddleCenter;
            nextLabel.color = Color.white;
            nextLabel.fontSize = 28;
            nextLabel.text = "Next";

            _closeAction = async () => await UIManager.Instance.CloseAsync(this);
            _nextAction = async () => await UIManager.Instance.Navigator.PushAsync<SecondSamplePage>("Welcome to SecondSamplePage");
            _closeButton.onClick.AddListener(_closeAction);
            _nextButton.onClick.AddListener(_nextAction);
        }

        protected override void HandleShow(object args)
        {
            var message = args as string;
            if (string.IsNullOrEmpty(message))
            {
                message = "Hello from SampleHelloPage";
            }

            _messageText.text = message;
            Debug.Log($"[SampleHelloPage] Show: {message}");
        }

        protected override void HandleDestroy()
        {
            if (_closeButton != null && _closeAction != null)
            {
                _closeButton.onClick.RemoveListener(_closeAction);
            }

            if (_nextButton != null && _nextAction != null)
            {
                _nextButton.onClick.RemoveListener(_nextAction);
            }
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
