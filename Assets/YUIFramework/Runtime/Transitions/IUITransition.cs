using System.Threading.Tasks;
using UnityEngine;

namespace YUIFramework
{
    /// <summary>
    /// UI 转场接口，分别负责显示与隐藏方向。
    /// </summary>
    public interface IUITransition
    {
        Task PlayShowAsync(RectTransform target, UITransitionOptions options);
        Task PlayHideAsync(RectTransform target, UITransitionOptions options);
    }
}
