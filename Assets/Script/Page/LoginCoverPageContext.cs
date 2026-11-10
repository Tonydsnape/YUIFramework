using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YUIFramework;

public sealed class LoginCoverPageContext : BasePageContext
{
    private LoginCoverView _view;
    private bool _entering;
    private int _displayGeneration;

    public override GameObject DefaultFocus => _view ? _view.StartButton.gameObject : null;
    public bool IsEntering => _entering;

    protected override void HandleInit()
    {
        _view = ViewObject.GetComponent<LoginCoverView>();
        if (!_view) throw new InvalidOperationException("LoginCoverPage requires LoginCoverView.");
        _view.ValidateReferences();
        _view.StartButton.onClick.AddListener(OnStartClicked);
    }

    protected override void HandleShow(object args)
    {
        ++_displayGeneration;
        TrackDisplayBinding(_view.SafeArea.Bind(Services.RootRuntime.ScreenLayout));
        _view.StartButton.interactable = !_entering;
    }

    protected override void HandleDestroy()
    {
        if (_view) _view.StartButton.onClick.RemoveListener(OnStartClicked);
    }

    public async UniTask EnterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_entering || !IsInteractable) return;

        var generation = _displayGeneration;
        _entering = true;
        _view.StartButton.interactable = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, LifetimeToken);
        try
        {
            // DisplayToken ends when Replace closes this page; lifetime cancellation
            // still aborts an externally released page without canceling its own hide.
            await Services.Navigator.ReplaceAsync<MainMenuPageContext>(cancellationToken: linked.Token);
        }
        finally
        {
            _entering = false;
            if (_view && generation == _displayGeneration && IsVisible)
            {
                _view.StartButton.interactable = true;
                if (IsInteractable) Services.RootRuntime.Focus.Focus(this);
            }
        }
    }

    private void OnStartClicked()
    {
        EnterFromButtonAsync().Forget(Debug.LogException);
    }

    private async UniTask EnterFromButtonAsync()
    {
        try
        {
            await EnterAsync();
        }
        catch (OperationCanceledException) when (LifetimeToken.IsCancellationRequested)
        {
        }
    }
}
