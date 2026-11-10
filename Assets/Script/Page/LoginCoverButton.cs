using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class LoginCoverButton : Button
{
    [SerializeField] private GameObject _focusIndicator;

    public GameObject FocusIndicator => _focusIndicator;

    public void ValidateReferences()
    {
        if (!_focusIndicator || !_focusIndicator.transform.IsChildOf(transform))
        {
            throw new InvalidOperationException("Login cover requires a child focus indicator.");
        }
    }

    public override void OnSelect(BaseEventData eventData)
    {
        base.OnSelect(eventData);
        if (_focusIndicator) _focusIndicator.SetActive(true);
    }

    public override void OnDeselect(BaseEventData eventData)
    {
        base.OnDeselect(eventData);
        if (_focusIndicator) _focusIndicator.SetActive(false);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_focusIndicator) _focusIndicator.SetActive(false);
    }
}
