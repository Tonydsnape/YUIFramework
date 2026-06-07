using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// 第二个示例页面：用于演示 Push/Back。
    /// </summary>
    public class SecondSamplePage : BasePageContext
    {
        private Text _messageText;
        private Button _backButton;
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
            panelImage.color = new Color(0.08f, 0.18f, 0.35f, 0.92f);
            StretchFull(panel);

            var textRect = CreateUIObject("Message", panel);
            _messageText = textRect.gameObject.AddComponent<Text>();
            _messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.color = Color.white;
            _messageText.fontSize = 38;
            StretchFull(textRect);

            var backRect = CreateUIObject("BackButton", panel);
            backRect.sizeDelta = new Vector2(260f, 80f);
            backRect.anchorMin = new Vector2(0.5f, 0f);
            backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.anchoredPosition = new Vector2(0f, 80f);

            var backImage = backRect.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.17f, 0.26f, 0.4f, 1f);
            _backButton = backRect.gameObject.AddComponent<Button>();
            _backButton.targetGraphic = backImage;

            var labelRect = CreateUIObject("Label", backRect);
            StretchFull(labelRect);
            var label = labelRect.gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.fontSize = 28;
            label.text = "Back";

            _backAction = async () => await UIManager.Instance.Navigator.BackAsync();
            _backButton.onClick.AddListener(_backAction);
        }

        protected override void HandleShow(object args)
        {
            var message = args as string;
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "This is SecondSamplePage";
            }

            _messageText.text = message;
        }

        protected override void HandleDestroy()
        {
            if (_backButton != null && _backAction != null)
            {
                _backButton.onClick.RemoveListener(_backAction);
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
