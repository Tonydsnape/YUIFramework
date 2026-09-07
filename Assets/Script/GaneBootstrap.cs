using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework;

public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartAsync(destroyCancellationToken).Forget(Debug.LogException);
    }

    private async UniTask StartAsync(
        System.Threading.CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!UIManager.Instance.IsInitialized)
        {
            UIManager.Instance.Initialize(new ResourcesLoader());
        }

        RegisterAllUI();

        await UIManager.Instance.OpenAsync<MainMenuPageContext>(
            cancellationToken: cancellationToken);
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
