using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using YUIFramework;

public sealed class LoginCoverStartupPlayModeTests
{
    [UnityTearDown]
    public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
    {
        foreach (var bootstrap in UnityEngine.Object.FindObjectsOfType<GameBootstrap>())
            UnityEngine.Object.Destroy(bootstrap.gameObject);
        await UniTask.WaitUntil(() => !UIRoot.Active && !EventSystem.current)
            .Timeout(TimeSpan.FromSeconds(10));
    });

    [UnityTest]
    public IEnumerator LoadSceneStartsRealCoverAndPointerEntersMainMenu() => UniTask.ToCoroutine(async () =>
    {
        await EditorSceneManager.LoadSceneAsyncInPlayMode(
            "Assets/Scenes/LoadScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
        Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo("Assets/Scenes/LoadScene.unity"));
        await UniTask.WaitUntil(() => UnityEngine.Object.FindObjectOfType<LoginCoverView>())
            .Timeout(TimeSpan.FromSeconds(10));
        var view = UnityEngine.Object.FindObjectOfType<LoginCoverView>();
        Assert.That(UnityEngine.Object.FindObjectsOfType<GameBootstrap>().Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsOfType<UIRoot>().Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length, Is.EqualTo(1));
        Assert.That(view.Background.sprite.name, Is.EqualTo("Background_01"));
        await UniTask.WaitUntil(() => EventSystem.current.currentSelectedGameObject == view.StartButton.gameObject)
            .Timeout(TimeSpan.FromSeconds(10));
        Canvas.ForceUpdateCanvases();
        var button = (RectTransform)view.StartButton.transform;
        var hit = RaycastButton(button, button.rect.center);
        // Inside the 256-unit hit target, outside the inset 224-unit icon rect.
        var edgeHit = RaycastButton(button, new Vector2(button.rect.xMax - 4, 0));
        Assert.That(edgeHit, Is.SameAs(hit));
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        ExecuteEvents.Execute(hit, pointer, ExecuteEvents.pointerClickHandler);
        await UniTask.WaitUntil(() => !view).Timeout(TimeSpan.FromSeconds(10));
        Assert.That(GameObject.Find("MainMenuPage(Clone)"), Is.Not.Null);
        Assert.That(UnityEngine.Object.FindObjectsOfType<UIRoot>().Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length, Is.EqualTo(1));
    });

    private static GameObject RaycastButton(RectTransform button, Vector2 localPoint)
    {
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, button.TransformPoint(localPoint))
        };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Assert.That(hits, Is.Not.Empty, "Cover button must be reachable by the actual canvas raycaster.");
        var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        Assert.That(handler, Is.SameAs(button.gameObject));
        return handler;
    }
}
