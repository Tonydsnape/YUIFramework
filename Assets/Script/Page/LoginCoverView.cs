using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YUIFramework;

[DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
public sealed class LoginCoverView : MonoBehaviour
{
    [SerializeField] private Image _background;
    [SerializeField] private AspectRatioFitter _backgroundFitter;
    [SerializeField] private UISafeAreaContent _safeArea;
    [SerializeField] private LoginCoverButton _startButton;
    [SerializeField] private TMP_Text _startLabel;

    public Image Background => _background;
    public AspectRatioFitter BackgroundFitter => _backgroundFitter;
    public UISafeAreaContent SafeArea => _safeArea;
    public LoginCoverButton StartButton => _startButton;
    public TMP_Text StartLabel => _startLabel;

    public void ValidateReferences()
    {
        if (!_background || !_background.sprite || !_backgroundFitter ||
            _backgroundFitter.gameObject != _background.gameObject ||
            !_safeArea || !_startButton || !_startLabel || !_startLabel.font ||
            string.IsNullOrWhiteSpace(_startLabel.text) || _startLabel.raycastTarget ||
            !_startLabel.transform.IsChildOf(_startButton.transform) ||
            !(_startButton.targetGraphic is Image icon) || !icon.sprite ||
            !_safeArea.transform.IsChildOf(transform) ||
            !_startButton.transform.IsChildOf(_safeArea.transform))
        {
            throw new InvalidOperationException(
                "Login cover has missing or invalid references. Import the two required sprites and use " +
                "Tools > YUIFramework > Game > Create missing login cover.");
        }

        if (_background.raycastTarget ||
            _backgroundFitter.aspectMode != AspectRatioFitter.AspectMode.EnvelopeParent ||
            !Mathf.Approximately(_backgroundFitter.aspectRatio,
                _background.sprite.rect.width / _background.sprite.rect.height))
        {
            throw new InvalidOperationException("Login cover background must cover its parent at the sprite's original aspect ratio.");
        }

        _startButton.ValidateReferences();
    }
}
