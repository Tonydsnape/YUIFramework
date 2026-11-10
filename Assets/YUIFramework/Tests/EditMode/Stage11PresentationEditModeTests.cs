using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework.Configuration;
using YUIFramework.Localization;
using YUIFramework.Localization.Editor;

namespace YUIFramework.Tests
{
    public sealed class Stage11PresentationEditModeTests
    {
        [Test]
        public void FontLicenseHashAndStaticAtlasFallbackBudgetAreRealAssets()
        {
            var path="Assets/YUIFramework/ThirdParty/LXGWWenKai/LXGWWenKai-Regular.ttf";
            using var sha=System.Security.Cryptography.SHA256.Create();
            var hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
            Assert.That(hash,Is.EqualTo("39ad71264b588165b469e35e6afb162a378dacd1f95348160240ba9038ac3009"));
            StringAssert.Contains("SIL OPEN FONT LICENSE",File.ReadAllText("Assets/YUIFramework/ThirdParty/LXGWWenKai/OFL.txt"));
            var font=Resources.Load<TMP_FontAsset>("YUIPresentation/WenKai");
            Assert.That(font.atlasPopulationMode,Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(font.atlasTexture.width,Is.EqualTo(2048));Assert.That(font.atlasTextures.Length,Is.EqualTo(1));
            Assert.That(font.fallbackFontAssetTable.Count,Is.EqualTo(1));
            var fallback=font.fallbackFontAssetTable[0];
            Assert.That(fallback.atlasPopulationMode,Is.EqualTo(AtlasPopulationMode.Dynamic));
            Assert.That(fallback.isMultiAtlasTexturesEnabled,Is.False);
            Assert.That(fallback.sourceFontFile,Is.Not.Null);
            foreach(var c in "你好，中文正楷！Bonjour,éèàçœHello")
                Assert.That(font.HasCharacter(c,true,true),Is.True,"Missing "+c);
            Debug.Log($"WenKai primary: {font.characterTable.Count} characters; atlas {font.atlasWidth}x{font.atlasHeight}; fallback max one {fallback.atlasWidth}x{fallback.atlasHeight} atlas.");
        }
        [Test]
        public void PresentationSchemaIsImmutableTypedAndRejectsBadKeysTypesAndColors()
        {
            var json=new ConfigCodec().Decode(File.ReadAllBytes("Assets/YUIFramework/ConfigData/Editor/json/UIPresentationConfig.json"),ConfigFormat.Json);
            var catalog=UIPresentationCatalog.Parse(json);
            Assert.That(catalog.Get("dark","zh-CN",new UIThemeToken<Sprite>("badge")).Value,Is.EqualTo("YUIPresentation/BadgeZh"));
            Assert.That(catalog.Get("light","en",new UIThemeToken<Color>("text")).Color(true),Is.EqualTo(Color.black));
            Assert.Throws<ArgumentException>(()=>catalog.Get("dark","en",new UIThemeToken<Material>("body")));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>catalog.Get("dark","en",new UIThemeToken<Color>("absent")));
            var bad=json.DeepClone();bad["theme.light"]["text"]["Value"]="#bad!";
            Assert.Throws<ConfigDataException>(()=>UIPresentationCatalog.Parse(bad));
            bad=json.DeepClone();((JObject)bad["theme.dark"]).Remove("body");
            Assert.Throws<ConfigDataException>(()=>UIPresentationCatalog.Parse(bad));
        }
        [Test]
        public void ContentCheckerLocatesKeyAndActualLayoutOverflowWithoutStringLengthProxy()
        {
            var canvas=new GameObject("ContentCanvas",typeof(RectTransform),typeof(Canvas));
            var root=new GameObject("ContentCheck",typeof(RectTransform));
            root.transform.SetParent(canvas.transform,false);
            try
            {
                var rect=(RectTransform)root.transform;rect.sizeDelta=new Vector2(20,10);
                var text=root.AddComponent<TextMeshProUGUI>();text.fontSize=48;text.text="中文 Hello";
                var localize=root.AddComponent<ConfigLocalizedText>();localize.TextKey=new LocalizedTextKey("missing");
                var issues=UIContentChecks.Inspect(root,LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath));
                Assert.That(issues.Any(i=>i.Code=="MissingKey"&&i.Target==localize),Is.True);
                Assert.That(issues.Any(i=>i.Code=="Overflow"&&i.Target==text),Is.True);
                rect.sizeDelta=new Vector2(1200,200);
                localize.TextKey=new LocalizedTextKey("sample.title");
                Assert.That(UIContentChecks.Inspect(root,LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath)).Count,Is.Zero);
                text.text="Missing \u0378";
                LogAssert.Expect(LogType.Warning,new System.Text.RegularExpressions.Regex(".*\\\\u0378.*"));
                issues=UIContentChecks.Inspect(root,LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath));
                Assert.That(issues.Any(i=>i.Code=="MissingGlyph"&&i.Target==text&&i.Message.Contains("0378")),Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas); }
        }
        [Test]
        public void ScaleAndScreenContractsRejectInvalidValues()
        {
            Assert.Throws<ArgumentException>(()=>new UIScalePolicy(Vector2.zero));
            Assert.Throws<ArgumentException>(()=>new UIScreenFrame(new Vector2(1080,1920),new Rect(-1,0,1080,1920)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new UIAccessibilityOptions(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new UIAccessibilityOptions(3));
        }
        [Test]
        public void ContentCheckAndDevicePreviewDoNotPopulatePersistentFontAtlasesOrMutateSource()
        {
            var font=Resources.Load<TMP_FontAsset>("YUIPresentation/WenKai");
            var fallback=font.fallbackFontAssetTable[0];
            var settings=TMP_Settings.instance;
            var source=new GameObject("Preview source",typeof(RectTransform),typeof(TextMeshProUGUI));
            var text=source.GetComponent<TMP_Text>();text.font=font;text.text="乁丁中文";
            ((RectTransform)source.transform).sizeDelta=new Vector2(1000,200);
            var before=EditorJsonUtility.ToJson(source);
            var count=fallback.characterTable.Count;var atlasCount=fallback.atlasTextures.Length;
            var dirty=EditorUtility.IsDirty(fallback);
            var window=ScriptableObject.CreateInstance<UIDevicePreview>();
            var scenes=UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
            try
            {
                UIContentChecks.Inspect(source,LocalizedTextEditorCatalog.Load(LocalizedTextEditorCatalog.RelativePath));
                Assert.That(fallback.characterTable.Count,Is.EqualTo(count),"Content checks must not populate the original fallback.");
                typeof(UIDevicePreview).GetField("_prefab",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,source);
                typeof(UIDevicePreview).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(window,null);
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount,Is.EqualTo(scenes+1));
                Assert.That(fallback.characterTable.Count,Is.EqualTo(count));
                Assert.That(fallback.atlasTextures.Length,Is.EqualTo(atlasCount));
                Assert.That(EditorUtility.IsDirty(fallback),Is.EqualTo(dirty));
                Assert.That(EditorJsonUtility.ToJson(source),Is.EqualTo(before));
                Assert.That(TMP_Settings.instance,Is.SameAs(settings));
            }
            finally { UnityEngine.Object.DestroyImmediate(window);UnityEngine.Object.DestroyImmediate(source); }
            Assert.That(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount,Is.EqualTo(scenes));
        }
        [UnityTest]
        public IEnumerator FormatterAndLayoutHooksAreUsed() => UniTask.ToCoroutine(async()=>{
            var configs=SampleUIConfiguration.Create(new EditorJsonConfigSource(),ConfigFormat.Json);
            var formatter=new TestFormatter();
            using var text=new TextLocalizationService(configs,"en",formatter:formatter,layout:new TestLayout());
            var other=SampleUIConfiguration.Create(new EditorJsonConfigSource(),ConfigFormat.Json);
            var resourceOwner=new UIResourceService();
            Assert.Throws<ArgumentException>(()=>new UIPresentationService(other,text,resourceOwner));
            await configs.InitializeAsync();
            Assert.That(text.Get("sample.title"),Is.EqualTo("[Text localization sample]"));
            Assert.That(text.GetPlural("sample.title","sample.switch",2),Is.EqualTo("[Switch language]"));
            Assert.That(text.FormatDate(DateTime.Today),Is.EqualTo("date"));
            Assert.That(text.FormatCurrency(2),Is.EqualTo("currency"));
            Assert.That(text.Layout.Prepare("abc","en").RightToLeft,Is.True);
            Assert.That(formatter.Calls,Is.EqualTo(2));
            await configs.ShutdownAsync();
            await other.ShutdownAsync();await resourceOwner.ShutdownAsync();
        });
        private sealed class TestFormatter : ILocalizedTextFormatter
        {
            internal int Calls;
            public string Format(string template,object[] args,System.Globalization.CultureInfo culture) { Calls++;return "["+string.Format(template,args)+"]"; }
            public string Date(DateTime value,System.Globalization.CultureInfo culture)=>"date";
            public string Currency(decimal value,System.Globalization.CultureInfo culture)=>"currency";
            public string PluralKey(string one,string other,decimal count,System.Globalization.CultureInfo culture)=>other;
        }
        private sealed class TestLayout : ILocalizedTextLayout
        { public LocalizedTextLayout Prepare(string text,string locale)=>new LocalizedTextLayout(text,true); }
    }
}
