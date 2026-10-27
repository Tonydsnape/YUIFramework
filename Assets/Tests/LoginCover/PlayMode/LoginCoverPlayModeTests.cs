using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework;

public sealed class LoginCoverPlayModeTests
{
    private UIManager _ui;
    private CoverLoader _loader;
    private Texture2D _texture;
    private Sprite _background;
    private Sprite _button;
    private GameObject _source;

    [UnitySetUp]
    public IEnumerator SetUp() => UniTask.ToCoroutine(async () =>
    {
        _texture = new Texture2D(192, 108);
        _background = Sprite.Create(_texture, new Rect(0, 0, 192, 108), Vector2.one * .5f);
        _button = Sprite.Create(_texture, new Rect(0, 0, 96, 96), Vector2.one * .5f);
        _source = LoginCoverAssets.CreateView(_background, _button);
        _source.SetActive(false);
        _loader = new CoverLoader(_source);
        _ui = new UIManager();
        var root = UIRootRuntime.CreateOwned(GameBootstrap.CreateRootOptions());
        await _ui.InitializeAsync(_loader, root);
        GameBootstrap.RegisterAllUI(_ui);
    });

    [UnityTearDown]
    public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
    {
        await _ui.ShutdownAsync();
        if (_source) UnityEngine.Object.Destroy(_source);
        UnityEngine.Object.Destroy(_button);
        UnityEngine.Object.Destroy(_background);
        UnityEngine.Object.Destroy(_texture);
        await UniTask.NextFrame();
    });

    [UnityTest]
    public IEnumerator OverlayCanvasActuallyUsesExpandAtCurrentScreenSize() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        await UniTask.NextFrame();
        Canvas.ForceUpdateCanvases();
        var root = _ui.RootRuntime.Root.GetComponent<RectTransform>();
        var scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        Assert.That(root.rect.width, Is.EqualTo(Screen.width / scale).Within(1));
        Assert.That(root.rect.height, Is.EqualTo(Screen.height / scale).Within(1));
        Assert.That(((RectTransform)page.ViewObject.transform).rect.size, Is.EqualTo(root.rect.size));
    });

    [UnityTest]
    public IEnumerator ActualRectsCoverViewportAndKeepButtonInsideSafeAreaAcrossDevices() =>
        UniTask.ToCoroutine(VerifyDeviceLayoutsAsync);

    [UnityTest]
    public IEnumerator RealArtworkUsesSameSafeLayoutAndEntersExistingMainMenu() => UniTask.ToCoroutine(async () =>
    {
        _loader.UseRealCover = true;
        await VerifyDeviceLayoutsAsync();
        var page = (LoginCoverPageContext)_ui.Navigator.CurrentPage;
        var view = page.ViewObject.GetComponent<LoginCoverView>();
        Assert.That(view.Background.sprite.name, Is.EqualTo("Background_01"));
        Assert.That(((Image)view.StartButton.targetGraphic).sprite.name, Is.EqualTo("Btn_IconButton_Circle_359"));
        Assert.That(_ui.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(view.StartButton.gameObject));
        await page.EnterAsync();
        Assert.That(_ui.Navigator.CurrentPageType, Is.EqualTo(typeof(MainMenuPageContext)));
        Assert.That(_ui.Navigator.Count, Is.EqualTo(1));
        Assert.That(await _ui.Navigator.BackAsync(), Is.False);
    });

    private async UniTask VerifyDeviceLayoutsAsync()
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        var view = page.ViewObject.GetComponent<LoginCoverView>();
        var canvas = _ui.RootRuntime.Root.GetComponent<Canvas>();
        var root = (RectTransform)canvas.transform;
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.renderMode = RenderMode.WorldSpace;
        _ui.RootRuntime.ScreenLayout.enabled = false;
        var sizes = new[]
        {
            new Vector2(1920, 1080), new Vector2(1280, 720), new Vector2(2560, 1440),
            new Vector2(1920, 1200), new Vector2(1440, 1080), new Vector2(2340, 1080),
            new Vector2(2400, 1080), new Vector2(2560, 1080), new Vector2(3440, 1440),
            new Vector2(720, 1280)
        };
        foreach (var size in sizes)
        {
            var scale = Mathf.Min(size.x / 1920f, size.y / 1080f);
            root.sizeDelta = size / scale;
            var safe = new Rect(110, 40, size.x - 150, size.y - 80);
            _ui.RootRuntime.ScreenLayout.Apply(new UIScreenFrame(size, safe), new Rect(0, 0, 1, 1));
            await UniTask.NextFrame();
            Canvas.ForceUpdateCanvases();
            var background = BoundsIn(view.Background.rectTransform, root);
            Assert.That(background.xMin, Is.LessThanOrEqualTo(root.rect.xMin + .1f), size.ToString());
            Assert.That(background.yMin, Is.LessThanOrEqualTo(root.rect.yMin + .1f), size.ToString());
            Assert.That(background.xMax, Is.GreaterThanOrEqualTo(root.rect.xMax - .1f), size.ToString());
            Assert.That(background.yMax, Is.GreaterThanOrEqualTo(root.rect.yMax - .1f), size.ToString());
            var aspect = view.Background.sprite.rect.width / view.Background.sprite.rect.height;
            Assert.That(background.width / background.height, Is.EqualTo(aspect).Within(.001f));
            var content = (RectTransform)view.SafeArea.transform;
            var button = BoundsIn((RectTransform)view.StartButton.transform, content);
            Assert.That(button.xMin, Is.GreaterThanOrEqualTo(content.rect.xMin - .1f));
            Assert.That(button.yMin, Is.GreaterThanOrEqualTo(content.rect.yMin - .1f));
            Assert.That(button.xMax, Is.LessThanOrEqualTo(content.rect.xMax + .1f));
            Assert.That(button.yMax, Is.LessThanOrEqualTo(content.rect.yMax + .1f));
            Assert.That(button.width, Is.EqualTo(button.height).Within(.01f));
            Assert.That(button.center.x, Is.EqualTo(content.rect.center.x).Within(.1f));
            Assert.That(button.center.y, Is.EqualTo(content.rect.yMin + content.rect.height * .18f).Within(.1f));
            view.StartLabel.ForceMeshUpdate();
            Assert.That(view.StartLabel.isTextOverflowing, Is.False);
            Assert.That(view.StartLabel.textInfo.characterCount, Is.EqualTo(2));
            for (var i = 0; i < view.StartLabel.textInfo.characterCount; ++i)
            {
                var character = view.StartLabel.textInfo.characterInfo[i];
                Assert.That(character.isVisible, Is.True);
                Assert.That(character.textElement.glyph.index, Is.Not.Zero);
                Assert.That(view.StartLabel.font.HasCharacter(character.character, true, true), Is.True);
            }
            var expected = _ui.RootRuntime.ScreenLayout.SafeRectIn((RectTransform)page.ViewObject.transform);
            var actual = BoundsIn(content, (RectTransform)page.ViewObject.transform);
            Assert.That(actual.xMin, Is.EqualTo(expected.xMin).Within(.1f));
            Assert.That(actual.yMin, Is.EqualTo(expected.yMin).Within(.1f));
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(.1f));
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(.1f));
        }
    }

    [UnityTest]
    public IEnumerator FocusHasVisibleShapeAndSubmitReplacesCoverWithoutBackEntry() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        var view = page.ViewObject.GetComponent<LoginCoverView>();
        var events = _ui.RootRuntime.EventSystem;
        Assert.That(events.currentSelectedGameObject, Is.SameAs(view.StartButton.gameObject));
        Assert.That(view.StartButton.FocusIndicator.activeSelf, Is.True);
        events.SetSelectedGameObject(null);
        Assert.That(view.StartButton.FocusIndicator.activeSelf, Is.False);
        events.SetSelectedGameObject(view.StartButton.gameObject);
        ExecuteEvents.Execute(view.StartButton.gameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
        await UniTask.WaitUntil(() => _ui.Navigator.CurrentPage is MainMenuPageContext)
            .Timeout(TimeSpan.FromSeconds(10));
        Assert.That(_ui.Navigator.Count, Is.EqualTo(1));
        Assert.That(await _ui.Navigator.BackAsync(), Is.False);
        Assert.That(_ui.IsOpen<LoginCoverPageContext>(), Is.False);
        Assert.That(page.State, Is.EqualTo(UIContextState.Released));
    });

    [UnityTest]
    public IEnumerator RepeatedPointerClicksAndDirectRequestsShareOneNavigation() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        var button = page.ViewObject.GetComponent<LoginCoverView>().StartButton;
        _loader.MainGate = new UniTaskCompletionSource();
        var pointer = new PointerEventData(_ui.RootRuntime.EventSystem) { button = PointerEventData.InputButton.Left };
        button.OnPointerClick(pointer);
        button.OnPointerClick(pointer);
        await page.EnterAsync();
        await UniTask.WaitUntil(() => _loader.MainLoads > 0).Timeout(TimeSpan.FromSeconds(10));
        Assert.That(page.IsEntering, Is.True);
        Assert.That(button.interactable, Is.False);
        Assert.That(_loader.MainLoads, Is.EqualTo(1));
        _loader.MainGate.TrySetResult();
        await UniTask.WaitUntil(() => !page.IsEntering).Timeout(TimeSpan.FromSeconds(10));
        Assert.That(_ui.Navigator.CurrentPageType, Is.EqualTo(typeof(MainMenuPageContext)));
        Assert.That(_loader.MainLoads, Is.EqualTo(1));
    });

    [UnityTest]
    public IEnumerator FailedLoadKeepsCoverAndAllowsRetry() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        var view = page.ViewObject.GetComponent<LoginCoverView>();
        _loader.FailNextMain = true;
        Exception failure = null;
        try { await page.EnterAsync(); }
        catch (Exception error) { failure = error; }
        Assert.That(failure, Is.Not.Null);
        StringAssert.Contains("Directed main-menu load failure", failure.ToString());
        Assert.That(_ui.Navigator.CurrentPage, Is.SameAs(page));
        Assert.That(page.IsVisible, Is.True);
        Assert.That(page.IsEntering, Is.False);
        Assert.That(view.StartButton.interactable, Is.True);
        Assert.That(_ui.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(view.StartButton.gameObject));
        await page.EnterAsync();
        Assert.That(_ui.Navigator.CurrentPageType, Is.EqualTo(typeof(MainMenuPageContext)));
    });

    [UnityTest]
    public IEnumerator CallerCancellationRestoresButtonAndDoesNotAdvance() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        var view = page.ViewObject.GetComponent<LoginCoverView>();
        _loader.MainGate = new UniTaskCompletionSource();
        using var cancellation = new CancellationTokenSource();
        var entering = page.EnterAsync(cancellation.Token).AsTask();
        await UniTask.WaitUntil(() => _loader.MainLoads > 0).Timeout(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var canceled = false;
        try { await entering; }
        catch (OperationCanceledException) { canceled = true; }
        Assert.That(canceled, Is.True);
        Assert.That(_ui.Navigator.CurrentPage, Is.SameAs(page));
        Assert.That(page.IsEntering, Is.False);
        Assert.That(view.StartButton.interactable, Is.True);
        Assert.That(_ui.RootRuntime.EventSystem.currentSelectedGameObject, Is.SameAs(view.StartButton.gameObject));
        _loader.MainGate.TrySetResult();
        await page.EnterAsync();
        Assert.That(_ui.Navigator.CurrentPageType, Is.EqualTo(typeof(MainMenuPageContext)));
    });

    [UnityTest]
    public IEnumerator ExternalCloseCancelsPendingEntryAndReleasesDisplayBinding() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        var view = page.ViewObject.GetComponent<LoginCoverView>();
        var safe = (RectTransform)view.SafeArea.transform;
        var restored = false;
        page.StateChanged += (previous, current) =>
        {
            if (current == UIContextState.Hidden)
            {
                restored = safe.anchorMin == Vector2.zero && safe.anchorMax == Vector2.one;
            }
        };
        _loader.MainGate = new UniTaskCompletionSource();
        var entering = page.EnterAsync().AsTask();
        await UniTask.WaitUntil(() => _loader.MainLoads > 0).Timeout(TimeSpan.FromSeconds(10));
        await _ui.CloseAsync(page);
        var canceled = false;
        try { await entering; }
        catch (OperationCanceledException) { canceled = true; }
        Assert.That(canceled, Is.True);
        Assert.That(page.State, Is.EqualTo(UIContextState.Released));
        Assert.That(_ui.IsOpen<MainMenuPageContext>(), Is.False);
        Assert.That(restored, Is.True, "Display binding must restore the original anchors before releasing the view.");
        _loader.MainGate.TrySetResult();
        await UniTask.NextFrame();
        Assert.That(_ui.IsOpen<MainMenuPageContext>(), Is.False);
    });

    [UnityTest]
    public IEnumerator ShutdownCancelsInFlightEntryWithoutLeavingRootOrEventSystem() => UniTask.ToCoroutine(async () =>
    {
        var page = await _ui.Navigator.PushAsync<LoginCoverPageContext>();
        _loader.MainGate = new UniTaskCompletionSource();
        var entering = page.EnterAsync().AsTask();
        await UniTask.WaitUntil(() => _loader.MainLoads > 0).Timeout(TimeSpan.FromSeconds(10));
        await _ui.ShutdownAsync();
        var canceled = false;
        try { await entering; }
        catch (OperationCanceledException) { canceled = true; }
        Assert.That(canceled, Is.True);
        await UniTask.NextFrame();
        Assert.That(UIRoot.Active, Is.Null);
        Assert.That(EventSystem.current, Is.Null);
    });

    private static Rect BoundsIn(RectTransform rect, RectTransform parent)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var min = parent.InverseTransformPoint(corners[0]);
        var max = parent.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private sealed class CoverLoader : IResourceLoader
    {
        private readonly GameObject _cover;
        public int MainLoads;
        public bool FailNextMain;
        public UniTaskCompletionSource MainGate;
        public bool UseRealCover;
        public CoverLoader(GameObject cover) { _cover = cover; }

        public async UniTask<GameObject> LoadPrefabAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (key == GameBootstrap.LoginCoverPrefabKey)
            {
                if (!UseRealCover) return _cover;
                var cover = Resources.Load<GameObject>(key);
                if (!cover) throw new InvalidOperationException("Real LoginCoverPage prefab is missing.");
                return cover;
            }
            if (key != "UI/Pages/MainMenuPage") throw new InvalidOperationException("Unexpected test key: " + key);
            ++MainLoads;
            if (MainGate != null) await MainGate.Task.AttachExternalCancellation(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextMain)
            {
                FailNextMain = false;
                throw new IOException("Directed main-menu load failure");
            }
            var prefab = Resources.Load<GameObject>(key);
            if (!prefab) throw new InvalidOperationException("Existing MainMenuPage prefab is missing.");
            return prefab;
        }

        public void Release(string key, GameObject instance)
        {
            if (instance) UnityEngine.Object.Destroy(instance);
        }
    }
}
