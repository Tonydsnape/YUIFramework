using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// 虚拟列表示例 Item。
    /// </summary>
    public sealed class VirtualListSampleItem : UIVirtualListItem
    {
        private Image _background;
        private Text _label;

        protected override void Awake()
        {
            base.Awake();
            EnsureVisuals();
        }

        public void SetText(string text)
        {
            EnsureVisuals();
            _label.text = text;
        }

        public void SetBackground(Color color)
        {
            EnsureVisuals();
            _background.color = color;
        }

        private void EnsureVisuals()
        {
            if (_background == null)
            {
                _background = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
                _background.color = new Color(0.12f, 0.14f, 0.18f, 0.92f);
            }

            if (_label == null)
            {
                var labelRect = new GameObject("Label").AddComponent<RectTransform>();
                labelRect.SetParent(RectTransform, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(24f, 0f);
                labelRect.offsetMax = new Vector2(-24f, 0f);

                _label = labelRect.gameObject.AddComponent<Text>();
                _label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _label.alignment = TextAnchor.MiddleLeft;
                _label.color = Color.white;
                _label.fontSize = 24;
            }
        }
    }
}
