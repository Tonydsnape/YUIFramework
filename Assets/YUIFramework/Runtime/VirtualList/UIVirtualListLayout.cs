using System;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 虚拟列表布局参数。
    /// </summary>
    [Serializable]
    public sealed class UIVirtualListLayout
    {
        public UIVirtualListDirection Direction = UIVirtualListDirection.Vertical;
        public float ItemSize = 80f;
        public float Spacing = 0f;
        public float PaddingStart = 0f;
        public float PaddingEnd = 0f;
        public int ExtraVisibleCount = 2;

        /// <summary>
        /// 对配置值做安全收敛，避免出现非法参数导致计算异常。
        /// </summary>
        public void Clamp()
        {
            ItemSize = Mathf.Max(1f, ItemSize);
            Spacing = Mathf.Max(0f, Spacing);
            PaddingStart = Mathf.Max(0f, PaddingStart);
            PaddingEnd = Mathf.Max(0f, PaddingEnd);
            ExtraVisibleCount = Mathf.Max(0, ExtraVisibleCount);
        }
    }
}
