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
        public AnimationCurve Curve;
        public string CustomTransitionId;

        /// <summary>
        /// 规范化配置，避免非法参数导致异常动画行为。
        /// </summary>
        public void Normalize()
        {
            ShowDuration = NormalizeNonNegative(ShowDuration);
            HideDuration = NormalizeNonNegative(HideDuration);
            SlideDistance = NormalizeNonNegative(SlideDistance);
            StartScale = float.IsNaN(StartScale) || float.IsInfinity(StartScale)
                ? 0.9f
                : Mathf.Max(0.01f, StartScale);
        }

        public UITransitionOptions Snapshot()
        {
            var snapshot = new UITransitionOptions
            {
                Type = Type,
                ShowDuration = ShowDuration,
                HideDuration = HideDuration,
                IgnoreTimeScale = IgnoreTimeScale,
                SlideDistance = SlideDistance,
                StartScale = StartScale,
                CustomTransitionId = CustomTransitionId,
                Curve = CloneCurve(Curve)
            };
            snapshot.Normalize();
            return snapshot;
        }

        private static float NormalizeNonNegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? 0f
                : Mathf.Max(0f, value);
        }

        private static AnimationCurve CloneCurve(AnimationCurve curve)
        {
            if (curve == null)
            {
                return null;
            }

            return new AnimationCurve(curve.keys)
            {
                preWrapMode = curve.preWrapMode,
                postWrapMode = curve.postWrapMode
            };
        }
    }
}
