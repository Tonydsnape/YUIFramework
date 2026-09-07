using UnityEngine;
using UnityEngine.UI;

namespace YUIFramework.Bootstrap
{
    public class BootstrapProgressUI : MonoBehaviour, IBootstrapProgressSink
    {
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Image fillImage;
        [SerializeField] private Text statusText;
        [SerializeField] private Text percentText;
        [SerializeField] private float lerpSpeed = 8f;

        private float _targetProgress;
        private float _displayProgress;

        public void Report(BootstrapProgress progress)
        {
            _targetProgress = Mathf.Clamp01(progress.Normalized);
            if (statusText != null)
            {
                statusText.text = progress.State.ToString();
            }

            if (lerpSpeed <= 0f)
            {
                _displayProgress = _targetProgress;
                ApplyProgress(_displayProgress);
            }
        }

        protected void SetLegacyProgress(float value)
        {
            _targetProgress = Mathf.Clamp01(value);
        }

        protected void SetLegacyStatus(string value)
        {
            if (statusText != null)
            {
                statusText.text = value ?? string.Empty;
            }
        }

        protected virtual void OnEnable()
        {
            ApplyProgress(_displayProgress);
        }

        private void Update()
        {
            if (lerpSpeed <= 0f)
            {
                return;
            }

            if (Mathf.Approximately(_displayProgress, _targetProgress))
            {
                return;
            }

            _displayProgress = Mathf.MoveTowards(
                _displayProgress,
                _targetProgress,
                lerpSpeed * Time.unscaledDeltaTime);
            ApplyProgress(_displayProgress);
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
