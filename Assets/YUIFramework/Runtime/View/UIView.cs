 using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 作为 UI 预制体根与 Context 的桥接组件。
    /// </summary>
    public class UIView : MonoBehaviour
    {
        private RectTransform _rectTransform;

        public BaseContext Context { get; private set; }
        public bool IsBound => Context != null;

        public RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null)
                {
                    _rectTransform = GetComponent<RectTransform>();
                    if (_rectTransform == null)
                    {
                        _rectTransform = gameObject.AddComponent<RectTransform>();
                    }
                }

                return _rectTransform;
            }
        }

        internal void Bind(BaseContext context)
        {
            Context = context;
        }
    }
}
