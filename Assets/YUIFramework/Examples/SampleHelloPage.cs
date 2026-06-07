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
        private Button _virtualListButton;
        private Button _publishButton;
        private UnityAction _closeAction;
        private UnityAction _nextAction;
        private UnityAction _virtualListAction;
        private UnityAction _publishAction;

        protected override void HandleInit()
        {
            Debug.Log("[SampleHelloPage] Init");
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
            _nextAction = async () =>
            {
                await UIManager.Instance.Navigator.PushAsync<SecondSamplePage>("Welcome to SecondSamplePage");
                PublishMessage("sample.hello", "Hello from SampleHelloPage");
            };
            _publishAction = () => PublishMessage("sample.hello", "Hello from SampleHelloPage");
            _closeButton.onClick.AddListener(_closeAction);
            _nextButton.onClick.AddListener(_nextAction);

            var virtualListRect = CreateUIObject("VirtualListButton", panel);
            virtualListRect.sizeDelta = new Vector2(360f, 80f);
            virtualListRect.anchorMin = new Vector2(0.5f, 0f);
            virtualListRect.anchorMax = new Vector2(0.5f, 0f);
            virtualListRect.pivot = new Vector2(0.5f, 0f);
            virtualListRect.anchoredPosition = new Vector2(0f, 380f);

            var virtualListImage = virtualListRect.gameObject.AddComponent<Image>();
            virtualListImage.color = new Color(0.2f, 0.22f, 0.35f, 0.95f);
            _virtualListButton = virtualListRect.gameObject.AddComponent<Button>();
            _virtualListButton.targetGraphic = virtualListImage;

            var virtualListLabelRect = CreateUIObject("Label", virtualListRect);
            StretchFull(virtualListLabelRect);
            var virtualListLabel = virtualListLabelRect.gameObject.AddComponent<Text>();
            virtualListLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            virtualListLabel.alignment = TextAnchor.MiddleCenter;
            virtualListLabel.color = Color.white;
            virtualListLabel.fontSize = 26;
            virtualListLabel.text = "Open Virtual List";

            _virtualListAction = async () => await UIManager.Instance.Navigator.PushAsync<VirtualListSamplePage>();
            _virtualListButton.onClick.AddListener(_virtualListAction);

            var publishRect = CreateUIObject("PublishMessageButton", panel);
            publishRect.sizeDelta = new Vector2(320f, 80f);
            publishRect.anchorMin = new Vector2(0.5f, 0f);
            publishRect.anchorMax = new Vector2(0.5f, 0f);
            publishRect.pivot = new Vector2(0.5f, 0f);
            publishRect.anchoredPosition = new Vector2(0f, 280f);

            var publishImage = publishRect.gameObject.AddComponent<Image>();
            publishImage.color = new Color(0.15f, 0.35f, 0.22f, 0.95f);
            _publishButton = publishRect.gameObject.AddComponent<Button>();
            _publishButton.targetGraphic = publishImage;
            _publishButton.onClick.AddListener(_publishAction);

            var publishLabelRect = CreateUIObject("Label", publishRect);
            StretchFull(publishLabelRect);
            var publishLabel = publishLabelRect.gameObject.AddComponent<Text>();
            publishLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            publishLabel.alignment = TextAnchor.MiddleCenter;
            publishLabel.color = Color.white;
            publishLabel.fontSize = 28;
            publishLabel.text = "Publish Message";
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

            if (_virtualListButton != null && _virtualListAction != null)
            {
                _virtualListButton.onClick.RemoveListener(_virtualListAction);
            }

            if (_publishButton != null && _publishAction != null)
            {
                _publishButton.onClick.RemoveListener(_publishAction);
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
