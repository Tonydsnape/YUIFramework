using UnityEngine;
using UnityEngine.UI;
using YUIFramework;

public sealed class MainMenuPageContext : BasePageContext
{
    private Button _startButton;
    private Button _settingButton;

    protected override void HandleInit()
    {
        _startButton = ViewObject.transform
            .Find("Bottom/Buttons_Group/Button_Battle")
            ?.GetComponent<Button>();
        _settingButton = ViewObject.transform.Find("Button_Setting")?.GetComponent<Button>();

        if (_startButton != null)
        {
            _startButton.onClick.AddListener(OnStartClicked);
        }

        if (_settingButton != null)
        {
            _settingButton.onClick.AddListener(OnSettingClicked);
        }
    }

    protected override void HandleShow(object args)
    {
        Debug.Log("[MainMenuPage] Show");
    }

    protected override void HandleHide()
    {
        Debug.Log("[MainMenuPage] Hide");
    }

    protected override void HandleDestroy()
    {
        if (_startButton != null)
        {
            _startButton.onClick.RemoveListener(OnStartClicked);
        }

        if (_settingButton != null)
        {
            _settingButton.onClick.RemoveListener(OnSettingClicked);
        }
    }

    private void OnStartClicked()
    {
        Debug.Log("Start Game");
    }

    private void OnSettingClicked()
    {
        Debug.Log("Open Setting");
    }
}