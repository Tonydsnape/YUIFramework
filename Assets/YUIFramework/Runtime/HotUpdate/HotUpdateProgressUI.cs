using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.HotUpdate
{
    /// <summary>
    /// 热更/补丁进度 UI（uGUI 版）。挂到 Loading 节点上，在 Inspector 关联进度条与文本即可。
    /// 订阅 <see cref="HotUpdateLauncher"/> 的进度/状态事件并显示，无需业务代码介入。
    ///
    /// 用法：
    ///   1. 放一个 Slider（或 fillImage）+ 两个 Text（状态/百分比）。
    ///   2. 把本组件挂上去并关联字段。
    /// 编辑器模拟/离线模式下资源无需下载，进度会很快到 100%。
    /// </summary>
    public sealed class HotUpdateProgressUI : MonoBehaviour
    {
        [Header("进度显示（任选其一或都填）")]
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Image fillImage;

        [Header("文本")]
        [SerializeField] private Text statusText;
        [SerializeField] private Text percentText;

        [Header("平滑")]
        [Tooltip("进度条插值速度；0 表示直接跳变")]
        [SerializeField] private float lerpSpeed = 8f;

        private float _targetProgress;
        private float _displayProgress;

        private void OnEnable()
        {
            HotUpdateLauncher.OnProgress += HandleProgress;
            HotUpdateLauncher.OnStatus += HandleStatus;
            ApplyProgress(_displayProgress);
        }

        private void OnDisable()
        {
            HotUpdateLauncher.OnProgress -= HandleProgress;
            HotUpdateLauncher.OnStatus -= HandleStatus;
        }

        private void Update()
        {
            if (lerpSpeed <= 0f)
            {
                _displayProgress = _targetProgress;
            }
            else if (!Mathf.Approximately(_displayProgress, _targetProgress))
            {
                _displayProgress = Mathf.MoveTowards(
                    _displayProgress, _targetProgress, lerpSpeed * Time.unscaledDeltaTime);
            }
            else
            {
                return;
            }

            ApplyProgress(_displayProgress);
        }

        private void HandleProgress(float value) => _targetProgress = Mathf.Clamp01(value);

        private void HandleStatus(string status)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }
        }

        private void ApplyProgress(float value)
        {
            if (progressSlider != null)
            {
                progressSlider.value = value;
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = value;
            }

            if (percentText != null)
            {
                percentText.text = $"{Mathf.RoundToInt(value * 100f)}%";
            }
        }
    }
}
