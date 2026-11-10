using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using YUIFramework;

public static class LoginCoverAssets
{
    public const string SpriteFolder = "Assets/GameRes/UI/Sprite/Load";
    public const string PrefabPath = "Assets/Resources/UI/Pages/LoginCoverPage.prefab";
    public const string StartFontPath = "Assets/Resources/YUIPresentation/WenKai.asset";

    [MenuItem("Tools/YUIFramework/Game/Create missing login cover")]
    public static void CreateMissingPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing)
        {
            var view = existing.GetComponent<LoginCoverView>();
            if (!view) throw new InvalidOperationException("Existing login cover has no LoginCoverView; it was not overwritten.");
            view.ValidateReferences();
            Selection.activeObject = existing;
            return;
        }

        var sprites = FindRequiredSprites();
        var root = CreateView(sprites.background, sprites.button);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (!prefab) throw new IOException("Could not save login cover prefab.");
            Selection.activeObject = prefab;
            Debug.Log("Created " + PrefabPath + ". Run Assets/Scenes/LoadScene.unity to open the cover.", prefab);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static (Sprite background, Sprite button) FindRequiredSprites()
    {
        if (!AssetDatabase.IsValidFolder(SpriteFolder))
        {
            throw new DirectoryNotFoundException(
                "Missing " + SpriteFolder + " in this worktree (" + Application.dataPath +
                "). Import Background_01 and Btn_IconButton_Circle_359 here, including their existing .meta files.");
        }

        var sprites = AssetDatabase.FindAssets("t:Texture2D t:Sprite", new[] { SpriteFolder })
            .Select(AssetDatabase.GUIDToAssetPath).Distinct()
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Sprite>().ToArray();
        return (FindUnique(sprites, "Background_01"), FindUnique(sprites, "Btn_IconButton_Circle_359"));
    }

    private static Sprite FindUnique(Sprite[] sprites, string name)
    {
        var matches = sprites.Where(sprite => sprite.name == name).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one Sprite named '{name}' in {SpriteFolder}, found {matches.Length}. " +
                "Use Sprite (2D and UI) import mode, or check the sprite sheet's sub-sprite names. No substitute was created.");
        }
        return matches[0];
    }

    public static GameObject CreateView(Sprite background, Sprite button)
    {
        if (!background) throw new ArgumentNullException(nameof(background));
        if (!button) throw new ArgumentNullException(nameof(button));
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(StartFontPath);
        if (!font) throw new InvalidOperationException("Login cover requires the existing WenKai TMP font: " + StartFontPath);

        var root = new GameObject("LoginCoverPage", typeof(RectTransform), typeof(UIView), typeof(LoginCoverView));
        try
        {
            Stretch((RectTransform)root.transform);
            var backgroundRect = Child("Background", root.transform);
            var backgroundImage = backgroundRect.gameObject.AddComponent<Image>();
            backgroundImage.sprite = background;
            backgroundImage.raycastTarget = false;
            var fitter = backgroundRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            // None mode recalculates the ratio from the current rect in EditMode.
            fitter.aspectRatio = background.rect.width / background.rect.height;

            var safeRect = Child("SafeArea", root.transform);
            var safe = safeRect.gameObject.AddComponent<UISafeAreaContent>();
            var start = Child("StartButton", safeRect);
            start.anchorMin = start.anchorMax = new Vector2(.5f, .18f);
            start.sizeDelta = new Vector2(256, 256);
            var hitTarget = start.gameObject.AddComponent<Image>();
            hitTarget.color = Color.clear;
            var startButton = start.gameObject.AddComponent<LoginCoverButton>();
            startButton.navigation = new Navigation { mode = Navigation.Mode.None };

            var icon = Child("Icon", start);
            icon.offsetMin = new Vector2(16, 16);
            icon.offsetMax = new Vector2(-16, -16);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.sprite = button;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            startButton.targetGraphic = iconImage;
            var colors = startButton.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.72f, .72f, .72f, 1);
            startButton.colors = colors;

            var labelRect = Child("Label", icon);
            labelRect.offsetMin = new Vector2(40, 40);
            labelRect.offsetMax = new Vector2(-40, -40);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = "\u5f00\u59cb";
            label.fontSize = 48;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(.025f, .2f, .29f, 1);
            label.enableWordWrapping = false;
            label.raycastTarget = false;

            var focus = Child("FocusIndicator", start);
            Border(focus, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 4));
            Border(focus, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 4));
            Border(focus, new Vector2(0, 0), new Vector2(0, 1), new Vector2(4, 0));
            Border(focus, new Vector2(1, 0), new Vector2(1, 1), new Vector2(4, 0));
            focus.gameObject.SetActive(false);
            SetReference(startButton, "_focusIndicator", focus.gameObject);
            var view = root.GetComponent<LoginCoverView>();
            SetReference(view, "_background", backgroundImage);
            SetReference(view, "_backgroundFitter", fitter);
            SetReference(view, "_safeArea", safe);
            SetReference(view, "_startButton", startButton);
            SetReference(view, "_startLabel", label);
            view.ValidateReferences();
            return root;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(root);
            throw;
        }
    }

    private static RectTransform Child(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        Stretch(rect);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Border(RectTransform parent, Vector2 min, Vector2 max, Vector2 size)
    {
        var edge = Child("Edge", parent);
        edge.anchorMin = min;
        edge.anchorMax = max;
        edge.sizeDelta = size;
        edge.pivot = new Vector2(min.x == max.x ? min.x : .5f, min.y == max.y ? min.y : .5f);
        var image = edge.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
    }

    private static void SetReference(UnityEngine.Object owner, string property, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(owner);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
