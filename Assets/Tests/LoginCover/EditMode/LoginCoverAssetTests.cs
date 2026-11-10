using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YUIFramework;

public sealed class LoginCoverAssetTests
{
    private Texture2D _texture;
    private Sprite _background;
    private Sprite _button;
    private GameObject _root;

    [SetUp]
    public void SetUp()
    {
        _texture = new Texture2D(320, 180);
        _background = Sprite.Create(_texture, new Rect(0, 0, 320, 180), Vector2.one * .5f);
        _button = Sprite.Create(_texture, new Rect(0, 0, 160, 100), Vector2.one * .5f);
    }

    [TearDown]
    public void TearDown()
    {
        if (_root) UnityEngine.Object.DestroyImmediate(_root);
        UnityEngine.Object.DestroyImmediate(_button);
        UnityEngine.Object.DestroyImmediate(_background);
        UnityEngine.Object.DestroyImmediate(_texture);
    }

    [Test]
    public void BuilderUsesRealSpriteAspectAndSeparateSafeContent()
    {
        _root = LoginCoverAssets.CreateView(_background, _button);
        var view = _root.GetComponent<LoginCoverView>();
        Assert.DoesNotThrow(view.ValidateReferences);
        Assert.That(view.Background.sprite, Is.SameAs(_background));
        Assert.That(view.Background.raycastTarget, Is.False);
        Assert.That(view.BackgroundFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));
        Assert.That(view.BackgroundFitter.aspectRatio, Is.EqualTo(320f / 180).Within(.0001f));
        Assert.That(view.Background.transform.parent, Is.SameAs(_root.transform));
        Assert.That(view.SafeArea.transform.parent, Is.SameAs(_root.transform));
        Assert.That(view.StartButton.transform.parent, Is.SameAs(view.SafeArea.transform));
        var buttonRect = (RectTransform)view.StartButton.transform;
        Assert.That(buttonRect.anchorMin, Is.EqualTo(new Vector2(.5f, .18f)));
        Assert.That(buttonRect.anchorMax, Is.EqualTo(buttonRect.anchorMin));
        Assert.That(buttonRect.sizeDelta, Is.EqualTo(new Vector2(256, 256)));
        var icon = view.StartButton.targetGraphic as Image;
        Assert.That(icon.sprite, Is.SameAs(_button));
        Assert.That(icon.preserveAspect, Is.True);
        Assert.That(icon.raycastTarget, Is.False);
        Assert.That(view.StartButton.GetComponent<Image>().raycastTarget, Is.True);
        Assert.That(view.StartButton.FocusIndicator.activeSelf, Is.False);
        Assert.That(view.StartLabel.text, Is.EqualTo("\u5f00\u59cb"));
        Assert.That(view.StartLabel.raycastTarget, Is.False);
        Assert.That(view.StartLabel.font.name, Is.EqualTo("WenKai"));
    }

    [Test]
    public void BackgroundRatioIsNotAssumedToBeSixteenByNine()
    {
        _root = LoginCoverAssets.CreateView(_button, _background);
        Assert.That(_root.GetComponent<LoginCoverView>().BackgroundFitter.aspectRatio,
            Is.EqualTo(1.6f).Within(.0001f));
    }

    [Test]
    public void BuilderRejectsMissingSpritesInsteadOfMakingAPlaceholder()
    {
        Assert.Throws<ArgumentNullException>(() => LoginCoverAssets.CreateView(null, _button));
        Assert.Throws<ArgumentNullException>(() => LoginCoverAssets.CreateView(_background, null));
    }

    [Test]
    public void ValidationRejectsDistortedBackground()
    {
        _root = LoginCoverAssets.CreateView(_background, _button);
        var view = _root.GetComponent<LoginCoverView>();
        view.BackgroundFitter.aspectRatio = 1;
        Assert.Throws<InvalidOperationException>(view.ValidateReferences);
    }

    [Test]
    public void ReferencePolicyIsGameLocalAndMobileRotationIsLandscapeOnly()
    {
        var policy = GameBootstrap.CreateRootOptions().ScalePolicy;
        Assert.That(policy.ReferenceResolution, Is.EqualTo(new Vector2(1920, 1080)));
        Assert.That(policy.Mode, Is.EqualTo(CanvasScaler.ScreenMatchMode.Expand));
        Assert.That(UIScalePolicy.Default.Mode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
        Assert.That(UIScalePolicy.Default.Match, Is.Zero);
        Assert.That(PlayerSettings.defaultInterfaceOrientation, Is.EqualTo(UIOrientation.AutoRotation));
        Assert.That(PlayerSettings.allowedAutorotateToPortrait, Is.False);
        Assert.That(PlayerSettings.allowedAutorotateToPortraitUpsideDown, Is.False);
        Assert.That(PlayerSettings.allowedAutorotateToLandscapeLeft, Is.True);
        Assert.That(PlayerSettings.allowedAutorotateToLandscapeRight, Is.True);
    }

    [Test]
    public void StartupMonoScriptStillResolvesItsSceneReferencedClass()
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Script/GaneBootstrap.cs");
        Assert.That(script, Is.Not.Null);
        Assert.That(script.GetClass(), Is.EqualTo(typeof(GameBootstrap)));
    }

    [Test]
    public void LoadSceneIsTheFirstEnabledBuildScene()
    {
        var first = Array.Find(EditorBuildSettings.scenes, scene => scene.enabled);
        Assert.That(first, Is.Not.Null);
        Assert.That(first.path, Is.EqualTo("Assets/Scenes/LoadScene.unity"));
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(first.path), Is.Not.Null);
        Assert.That(first.guid.ToString(), Is.EqualTo(AssetDatabase.AssetPathToGUID(first.path)));
    }

    [Test]
    public void ShippedPrefabUsesRequiredArtworkAndSurvivesInstantiation()
    {
        var sprites = LoginCoverAssets.FindRequiredSprites();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LoginCoverAssets.PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Generate the real login cover before running asset validation.");
        _root = UnityEngine.Object.Instantiate(prefab);
        var view = _root.GetComponent<LoginCoverView>();
        Assert.DoesNotThrow(view.ValidateReferences);
        Assert.That(view.Background.sprite, Is.SameAs(sprites.background));
        Assert.That(((Image)view.StartButton.targetGraphic).sprite, Is.SameAs(sprites.button));
        Assert.That(view.StartLabel.text, Is.EqualTo("\u5f00\u59cb"));
        Assert.That(view.StartLabel.raycastTarget, Is.False);
        Assert.That(AssetDatabase.GetAssetPath(view.StartLabel.font), Is.EqualTo(LoginCoverAssets.StartFontPath));
        foreach (var sprite in new[] { sprites.background, sprites.button })
        {
            var path = AssetDatabase.GetAssetPath(sprite);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            CollectionAssert.Contains(AssetDatabase.GetDependencies(LoginCoverAssets.PrefabPath), path);
        }
    }
}
