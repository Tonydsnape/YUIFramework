using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using YUIFramework;

public sealed class GameBootstrap : MonoBehaviour
{
    public const string LoginCoverPrefabKey = "UI/Pages/LoginCoverPage";
    public const string MainMenuPrefabKey = "UI/Pages/Home/Lobby";
    private UIManager _ui;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartAsync(destroyCancellationToken).Forget(Debug.LogException);
    }

    public static UIRootRuntimeOptions CreateRootOptions()
    {
        return new UIRootRuntimeOptions
        {
            ScalePolicy = new UIScalePolicy(
                new Vector2(1920, 1080), CanvasScaler.ScreenMatchMode.Expand)
        };
    }

    private async UniTask StartAsync(CancellationToken cancellationToken)
    {
        _ui = new UIManager();
        UIRootRuntime root = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            root = UIRootRuntime.CreateOwned(CreateRootOptions());
            await _ui.InitializeAsync(new ResourcesLoader(), root, cancellationToken: cancellationToken);
            RegisterAllUI(_ui);
            await _ui.Navigator.PushAsync<LoginCoverPageContext>(cancellationToken: cancellationToken);
            await UniTask.WaitUntilCanceled(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_ui.IsInitialized) await _ui.ShutdownAsync();
            else root?.Dispose();
        }
    }

    public static void RegisterAllUI(IUIService ui)
    {
        if (ui == null) throw new ArgumentNullException(nameof(ui));
        ui.Register<LoginCoverPageContext>(new UIConfig
        {
            Id = "LoginCoverPage",
            PrefabKey = LoginCoverPrefabKey,
            Layer = UILayer.Normal,
            FullScreen = true
        });
        ui.Register<MainMenuPageContext>(new UIConfig
        {
            Id = "MainMenuPage",
            PrefabKey = MainMenuPrefabKey,
            Layer = UILayer.Normal,
            CacheOnClose = true,
            FullScreen = true,
        });
    }
}
