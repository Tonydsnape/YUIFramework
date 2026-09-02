using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 透明度转场：通过 CanvasGroup 实现淡入淡出。
    /// </summary>
    public sealed class UIFadeTransition : UITransitionBase
    {
        public override async UniTask PlayShowAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var canvasGroup = GetOrAddCanvasGroup(target);
            canvasGroup.alpha = 0f;
            try
            {
                await TweenAsync(
                    settings.ShowDuration,
                    settings.IgnoreTimeScale,
                    progress => canvasGroup.alpha = Mathf.LerpUnclamped(0f, 1f, progress),
                    cancellationToken);
            }
            finally
            {
                canvasGroup.alpha = 1f;
            }
        }

        public override async UniTask PlayHideAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default)
        {
            if (!ValidateTarget(target))
            {
                return;
            }

            options?.Normalize();
            var settings = options ?? new UITransitionOptions();
            var canvasGroup = GetOrAddCanvasGroup(target);
            canvasGroup.alpha = 1f;
            try
            {
                await TweenAsync(
                    settings.HideDuration,
                    settings.IgnoreTimeScale,
                    progress => canvasGroup.alpha = Mathf.LerpUnclamped(1f, 0f, progress),
                    cancellationToken);
            }
            finally
            {
                canvasGroup.alpha = 1f;
            }
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
