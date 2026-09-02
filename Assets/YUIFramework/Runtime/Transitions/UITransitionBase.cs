using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 转场基类：提供通用 tween 循环与时间缩放支持。
    /// </summary>
    public abstract class UITransitionBase : IUITransition
    {
        public abstract UniTask PlayShowAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default);

        public abstract UniTask PlayHideAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default);

        protected static bool ValidateTarget(RectTransform target)
        {
            if (target != null)
            {
                return true;
            }

            Debug.LogWarning("[YUIFramework] UITransition target 为空，已跳过转场。");
            return false;
        }

        protected async UniTask TweenAsync(
            float duration,
            bool ignoreTimeScale,
            Action<float> onUpdate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (onUpdate == null)
            {
                return;
            }

            if (duration <= 0f)
            {
                onUpdate(1f);
                return;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delta = ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
                if (delta < 0f)
                {
                    delta = 0f;
                }

                elapsed += delta;
                var t = Mathf.Clamp01(elapsed / duration);
                onUpdate(EaseOutCubic(t));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            onUpdate(1f);
        }

        protected virtual float EaseOutCubic(float t)
        {
            var inv = 1f - Mathf.Clamp01(t);
            return 1f - inv * inv * inv;
        }
    }
}
