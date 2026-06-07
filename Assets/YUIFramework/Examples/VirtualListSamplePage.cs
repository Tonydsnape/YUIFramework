using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace YUIFramework
{
    /// <summary>
    /// 虚拟列表示例页面：展示 1000 条数据，滚动时仅复用可见 Item。
    /// </summary>
    public sealed class VirtualListSamplePage : BasePageContext, IUIVirtualListDataSource
    {
        private const int SampleCount = 1000;

        private readonly List<string> _items = new List<string>(SampleCount);

        private UIVirtualList _virtualList;
        private Button _backButton;
        private Text _titleText;
        private UnityAction _backAction;

        public int Count => _items.Count;

        protected override void HandleInit()
        {
            for (var i = 0; i < SampleCount; i++)
            {
                _items.Add($"Item #{i}");
            }

            var root = View.RectTransform;
            StretchFull(root);

            var panel = CreateUIObject("Panel", root);
            panel.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.92f);
            StretchFull(panel);

            var title = CreateUIObject("Title", panel);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.sizeDelta = new Vector2(0f, 80f);
            title.anchoredPosition = new Vector2(0f, 0f);
            _titleText = title.gameObject.AddComponent<Text>();
            _titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _titleText.alignment = TextAnchor.MiddleCenter;
            _titleText.color = Color.white;
            _titleText.fontSize = 30;
            _titleText.text = "Virtual List Sample (1000 Items)";

            var scrollRectTransform = CreateUIObject("VirtualListScrollRect", panel);
            scrollRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            scrollRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            scrollRectTransform.pivot = new Vector2(0.5f, 0.5f);
            scrollRectTransform.sizeDelta = new Vector2(760f, 860f);
            scrollRectTransform.anchoredPosition = new Vector2(0f, -30f);
            scrollRectTransform.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.12f, 0.18f, 0.8f);

            var scrollRect = scrollRectTransform.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 32f;

            var viewport = CreateUIObject("Viewport", scrollRectTransform);
            StretchFull(viewport);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.08f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewport;

            var content = CreateUIObject("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            scrollRect.content = content;

            var itemPrefabRect = CreateUIObject("VirtualListItemPrefab", content);
            itemPrefabRect.gameObject.SetActive(false);
            var itemPrefab = itemPrefabRect.gameObject.AddComponent<VirtualListSampleItem>();

            _virtualList = scrollRectTransform.gameObject.AddComponent<UIVirtualList>();
            _virtualList.SetItemPrefab(itemPrefab);
            _virtualList.Layout.Direction = UIVirtualListDirection.Vertical;
            _virtualList.Layout.ItemSize = 76f;
            _virtualList.Layout.Spacing = 8f;
            _virtualList.Layout.PaddingStart = 12f;
            _virtualList.Layout.PaddingEnd = 12f;
            _virtualList.Layout.ExtraVisibleCount = 3;
            _virtualList.SetDataSource(this);
            _virtualList.ReloadData();

            var backRect = CreateUIObject("BackButton", panel);
            backRect.anchorMin = new Vector2(0.5f, 0f);
            backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
            backRect.sizeDelta = new Vector2(260f, 72f);
            backRect.anchoredPosition = new Vector2(0f, 24f);
            backRect.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.25f, 0.35f, 0.96f);
            _backButton = backRect.gameObject.AddComponent<Button>();

            var backLabelRect = CreateUIObject("Label", backRect);
            StretchFull(backLabelRect);
            var backLabel = backLabelRect.gameObject.AddComponent<Text>();
            backLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            backLabel.alignment = TextAnchor.MiddleCenter;
            backLabel.color = Color.white;
            backLabel.fontSize = 28;
            backLabel.text = "Back";

            _backAction = async () => await UIManager.Instance.Navigator.BackAsync();
            _backButton.onClick.AddListener(_backAction);
        }

        protected override void HandleShow(object args)
        {
            _virtualList?.RefreshVisible();
        }

        protected override void HandleDestroy()
        {
            if (_backButton != null && _backAction != null)
            {
                _backButton.onClick.RemoveListener(_backAction);
            }

            _virtualList?.Clear();
        }

        public void BindItem(UIVirtualListItem item, int index)
        {
            if (item is not VirtualListSampleItem sampleItem)
            {
                return;
            }

            sampleItem.SetText(_items[index]);
            sampleItem.SetBackground(index % 2 == 0
                ? new Color(0.15f, 0.17f, 0.22f, 0.95f)
                : new Color(0.10f, 0.13f, 0.18f, 0.95f));
        }

        private static RectTransform CreateUIObject(string name, RectTransform parent)
        {
            var rect = new GameObject(name).AddComponent<RectTransform>();
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
