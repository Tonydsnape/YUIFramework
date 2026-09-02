using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 转场接口，分别负责显示与隐藏方向。
    /// </summary>
    public interface IUITransition
    {
        UniTask PlayShowAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default);

        UniTask PlayHideAsync(
            RectTransform target,
            UITransitionOptions options,
            CancellationToken cancellationToken = default);
    }
}
