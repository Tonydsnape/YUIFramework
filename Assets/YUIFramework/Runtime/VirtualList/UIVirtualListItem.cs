using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 虚拟列表 Item 基类，负责复用时的索引绑定状态。
    /// </summary>
    public class UIVirtualListItem : MonoBehaviour
    {
        private RectTransform _rectTransform;

        public int Index { get; internal set; } = -1;

        public RectTransform RectTransform
        {
            get
            {
                EnsureRectTransform();
                return _rectTransform;
            }
        }

        public bool IsBound => Index >= 0;

        protected virtual void Awake()
        {
            EnsureRectTransform();
        }

        internal void BindIndex(int index)
        {
            Index = index;
            OnBindIndex(index);
        }

        internal void UnbindIndex()
        {
            if (Index < 0)
            {
                return;
            }

            Index = -1;
            OnUnbindIndex();
        }

        protected virtual void OnBindIndex(int index)
        {
        }

        protected virtual void OnUnbindIndex()
        {
        }

        private void EnsureRectTransform()
        {
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }

            if (_rectTransform == null)
            {
                _rectTransform = gameObject.AddComponent<RectTransform>();
            }
        }
    }
}
