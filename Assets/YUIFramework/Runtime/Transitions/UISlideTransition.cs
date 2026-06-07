using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 滑动转场：根据方向做 anchoredPosition 偏移动画。
    /// </summary>
    public sealed class UISlideTransition : UITransitionBase
    {
        public override async Task PlayShowAsync(RectTransform target, UITransitionOptions options)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var original = GetOriginalPosition(target);
            var offset = ResolveOffset(settings.Type, settings.SlideDistance);
            var start = original + offset;
            target.anchoredPosition = start;

            await TweenAsync(settings.ShowDuration, settings.IgnoreTimeScale, progress =>
            {
                target.anchoredPosition = Vector2.LerpUnclamped(start, original, progress);
            });

            target.anchoredPosition = original;
        }

        public override async Task PlayHideAsync(RectTransform target, UITransitionOptions options)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var original = GetOriginalPosition(target);
            var offset = ResolveOffset(settings.Type, settings.SlideDistance);
            var end = original + offset;
            target.anchoredPosition = original;

            await TweenAsync(settings.HideDuration, settings.IgnoreTimeScale, progress =>
            {
                target.anchoredPosition = Vector2.LerpUnclamped(original, end, progress);
            });

            // 关闭结束后恢复原始位置，避免对象池复用时位置错乱。
            target.anchoredPosition = original;
        }

        private static Vector2 ResolveOffset(UITransitionType type, float distance)
        {
            switch (type)
            {
                case UITransitionType.SlideLeft:
                    return new Vector2(-distance, 0f);
                case UITransitionType.SlideRight:
                    return new Vector2(distance, 0f);
                case UITransitionType.SlideUp:
                    return new Vector2(0f, distance);
                case UITransitionType.SlideDown:
                    return new Vector2(0f, -distance);
                default:
                    return Vector2.zero;
            }
        }

        private static Vector2 GetOriginalPosition(RectTransform target)
        {
            var state = target.GetComponent<UISlideTransitionState>();
            if (state == null)
            {
                state = target.gameObject.AddComponent<UISlideTransitionState>();
            }

            if (!state.Initialized)
            {
                state.Initialized = true;
                state.OriginalAnchoredPosition = target.anchoredPosition;
            }

            return state.OriginalAnchoredPosition;
        }

        private sealed class UISlideTransitionState : MonoBehaviour
        {
            public bool Initialized;
            public Vector2 OriginalAnchoredPosition;
        }
    }
}
