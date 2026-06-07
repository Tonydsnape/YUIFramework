using System;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 转场参数配置。
    /// </summary>
    [Serializable]
    public sealed class UITransitionOptions
    {
        public UITransitionType Type = UITransitionType.None;
        public float ShowDuration = 0.2f;
        public float HideDuration = 0.15f;
        public bool IgnoreTimeScale = true;
        public float SlideDistance = 800f;
        public float StartScale = 0.9f;

        /// <summary>
        /// 规范化配置，避免非法参数导致异常动画行为。
        /// </summary>
        public void Normalize()
        {
            ShowDuration = Mathf.Max(0f, ShowDuration);
            HideDuration = Mathf.Max(0f, HideDuration);
            SlideDistance = Mathf.Max(0f, SlideDistance);
            StartScale = Mathf.Max(0.01f, StartScale);
        }
    }
}
