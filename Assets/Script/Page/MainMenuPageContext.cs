using UnityEngine;
using UnityEngine.UI;
using YUIFramework;

public sealed class MainMenuPageContext : BasePageContext
{
    private Button _startButton;
    private Button _settingButton;
    private Text _titleText;

    protected override void HandleInit()
    {
        // ViewObject 就是 MainMenuPage.prefab 实例
        _startButton = ViewObject.transform.Find("SafeArea/StartButton")?.GetComponent<Button>();
        _settingButton = ViewObject.transform.Find("SafeArea/SettingButton")?.GetComponent<Button>();
        _titleText = ViewObject.transform.Find("SafeArea/Title")?.GetComponent<Text>();

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
        if (_titleText != null)
        {
            _titleText.text = "YUIFramework Demo";
        }

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