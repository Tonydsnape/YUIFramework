using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// 转场执行器：根据配置选择具体转场实现并兜底异常。
    /// </summary>
    public sealed class UITransitionRunner
    {
        private readonly IUITransition _fade = new UIFadeTransition();
        private readonly IUITransition _scale = new UIScaleTransition();
        private readonly IUITransition _slide = new UISlideTransition();

        public UniTask PlayShowAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default)
        {
            return PlayAsync(
                target,
                options,
                isShow: true,
                cancellationToken: cancellationToken);
        }

        public UniTask PlayHideAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default)
        {
            return PlayAsync(
                target,
                options,
                isShow: false,
                cancellationToken: cancellationToken);
        }

        private async UniTask PlayAsync(
            RectTransform target,
            UITransitionOptions options,
            bool isShow,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (options == null || options.Type == UITransitionType.None)
            {
                return;
            }

            options.Normalize();
            var transition = ResolveTransition(options.Type);
            if (transition == null)
            {
                return;
            }

            if (isShow)
            {
                await transition.PlayShowAsync(target, options, cancellationToken);
            }
            else
            {
                await transition.PlayHideAsync(target, options, cancellationToken);
            }
        }

        private IUITransition ResolveTransition(UITransitionType type)
        {
            switch (type)
            {
                case UITransitionType.Fade:
                    return _fade;
                case UITransitionType.Scale:
                    return _scale;
                case UITransitionType.SlideLeft:
                case UITransitionType.SlideRight:
                case UITransitionType.SlideUp:
                case UITransitionType.SlideDown:
                    return _slide;
                default:
                    return null;
            }
        }
    }
}
