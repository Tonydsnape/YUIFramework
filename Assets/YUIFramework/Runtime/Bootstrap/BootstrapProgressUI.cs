using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace YUIFramework.Bootstrap
{
    public class BootstrapProgressUI : MonoBehaviour, IBootstrapProgressSink
    {
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Image fillImage;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text percentText;
        [SerializeField] private float lerpSpeed = 8f;

        private float _targetProgress;
        private float _displayProgress;
        private int _completedFiles;
        private int _totalFiles;

        public void Report(BootstrapProgress progress)
        {
            _targetProgress = Mathf.Clamp01(progress.Normalized);
            _completedFiles = progress.CompletedFiles;
            _totalFiles = progress.TotalFiles;
            if (statusText != null)
            {
                statusText.text = GetStatusText(progress);
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
                var percentage = Mathf.RoundToInt(value * 100f);
                percentText.text = _totalFiles > 0
                    ? $"{percentage}%  {_completedFiles}/{_totalFiles} files"
                    : $"{percentage}%";
            }
        }

        private static string GetStatusText(BootstrapProgress progress)
        {
            switch (progress.State)
            {
                case BootstrapState.InitializingPackage:
                    return "Initializing package";
                case BootstrapState.RequestingVersion:
                    return "Checking version";
                case BootstrapState.ActivatingManifest:
                    return "Loading resource manifest";
                case BootstrapState.CalculatingDownload:
                    return "Preparing update";
                case BootstrapState.AwaitingConfirmation:
                    return "Waiting for download confirmation";
                case BootstrapState.CheckingDisk:
                    return "Checking storage";
                case BootstrapState.Downloading:
                    return "Downloading resources";
                case BootstrapState.Verifying:
                    return "Verifying resources";
                case BootstrapState.ResourcesReady:
                    return progress.Degraded
                        ? "Using verified fallback resources"
                        : "Resources ready";
                case BootstrapState.LoadingCodeExtension:
                    return "Loading code extension";
                case BootstrapState.EnteringGame:
                    return "Entering game";
                case BootstrapState.Completed:
                    return "Ready";
                case BootstrapState.Canceled:
                    return "Startup canceled";
                case BootstrapState.Failed:
                    return "Startup failed";
                default:
                    return "Preparing game";
            }
        }
    }
}
