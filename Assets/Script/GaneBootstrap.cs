using UnityEngine;
using YUIFramework;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        if (!UIManager.Instance.IsInitialized)
        {
            UIManager.Instance.Initialize(new ResourcesLoader());
        }

        RegisterAllUI();

        await UIManager.Instance.OpenAsync<MainMenuPageContext>();
    }

    private static void RegisterAllUI()
    {
        UIManager.Instance.Register<MainMenuPageContext>(new UIConfig
        {
            Id = "MainMenuPage",
            PrefabKey = "UI/Pages/MainMenuPage",
            Layer = UILayer.Normal,
            CacheOnClose = true,
            FullScreen = true,
        });
    }
}
