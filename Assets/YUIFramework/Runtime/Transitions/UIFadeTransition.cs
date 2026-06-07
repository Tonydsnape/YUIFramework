using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 透明度转场：通过 CanvasGroup 实现淡入淡出。
    /// </summary>
    public sealed class UIFadeTransition : UITransitionBase
    {
        public override async Task PlayShowAsync(RectTransform target, UITransitionOptions options)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var canvasGroup = GetOrAddCanvasGroup(target);
            canvasGroup.alpha = 0f;
            await TweenAsync(settings.ShowDuration, settings.IgnoreTimeScale, progress =>
            {
                canvasGroup.alpha = Mathf.LerpUnclamped(0f, 1f, progress);
            });
            canvasGroup.alpha = 1f;
        }

        public override async Task PlayHideAsync(RectTransform target, UITransitionOptions options)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var canvasGroup = GetOrAddCanvasGroup(target);
            canvasGroup.alpha = 1f;
            await TweenAsync(settings.HideDuration, settings.IgnoreTimeScale, progress =>
            {
                canvasGroup.alpha = Mathf.LerpUnclamped(1f, 0f, progress);
            });
            canvasGroup.alpha = 0f;
        }

        private static CanvasGroup GetOrAddCanvasGroup(RectTransform target)
        {
            var group = target.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = target.gameObject.AddComponent<CanvasGroup>();
            }

            return group;
        }
    }
}
