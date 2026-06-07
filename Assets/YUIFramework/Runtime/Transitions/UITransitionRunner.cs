using System;
using System.Threading.Tasks;
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

        public Task PlayShowAsync(RectTransform target, UITransitionOptions options)
        {
            return PlayAsync(target, options, isShow: true);
        }

        public Task PlayHideAsync(RectTransform target, UITransitionOptions options)
        {
            return PlayAsync(target, options, isShow: false);
        }

        private async Task PlayAsync(RectTransform target, UITransitionOptions options, bool isShow)
        {
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

            try
            {
                if (isShow)
                {
                    await transition.PlayShowAsync(target, options);
                }
                else
                {
                    await transition.PlayHideAsync(target, options);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
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
