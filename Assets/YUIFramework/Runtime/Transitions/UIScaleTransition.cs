using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 缩放转场：从起始缩放过渡到原始缩放。
    /// </summary>
    public sealed class UIScaleTransition : UITransitionBase
    {
        public override async Task PlayShowAsync(RectTransform target, UITransitionOptions options)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var originalScale = GetOriginalScale(target);
            var startScale = originalScale * settings.StartScale;
            target.localScale = startScale;

            await TweenAsync(settings.ShowDuration, settings.IgnoreTimeScale, progress =>
            {
                target.localScale = Vector3.LerpUnclamped(startScale, originalScale, progress);
            });

            target.localScale = originalScale;
        }

        public override async Task PlayHideAsync(RectTransform target, UITransitionOptions options)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var originalScale = GetOriginalScale(target);
            var endScale = originalScale * settings.StartScale;
            target.localScale = originalScale;

            await TweenAsync(settings.HideDuration, settings.IgnoreTimeScale, progress =>
            {
                target.localScale = Vector3.LerpUnclamped(originalScale, endScale, progress);
            });

            target.localScale = endScale;
        }

        private static Vector3 GetOriginalScale(RectTransform target)
        {
            var state = target.GetComponent<UIScaleTransitionState>();
            if (state == null)
            {
                state = target.gameObject.AddComponent<UIScaleTransitionState>();
            }

            if (!state.Initialized)
            {
                state.Initialized = true;
                state.OriginalScale = target.localScale;
            }

            return state.OriginalScale;
        }

        private sealed class UIScaleTransitionState : MonoBehaviour
        {
            public bool Initialized;
            public Vector3 OriginalScale = Vector3.one;
        }
    }
}
