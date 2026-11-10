using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework.Configuration;
using YUIFramework.Localization;

namespace YUIFramework.Tests
{
    public sealed class Stage11PresentationPlayModeTests
    {
        private UIManager _ui;
        private UIResourceService _resources;
        private ConfigService _configs;
        private TextLocalizationService _text;
        private UIPresentationService _presentation;
        private GameObject _extra;
        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async()=>{
            _resources=new UIResourceService();_resources.Packages.Register(new ResourcesResourceProvider(),true);
            _configs=SampleUIConfiguration.Create(new ResourceConfigSource(_resources,prefix:"YUIConfig/"),ConfigFormat.MessagePack);
            _text=new TextLocalizationService(_configs,"en");
            _ui=new UIManager();await _ui.InitializeAsync(new CodeViewLoader());
            _presentation=new UIPresentationService(_configs,_text,_resources,_ui.Transitions);
            await _configs.InitializeAsync();
        });
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async()=>{
            try { await _ui.ShutdownAsync(); }
            finally {
                try { _presentation.Dispose(); }
                finally { try { _text.Dispose();await _configs.ShutdownAsync(); } finally { await _resources.ShutdownAsync(); } }
            }
            if(_extra)UnityEngine.Object.Destroy(_extra);
            await UniTask.NextFrame();
        });
        private static IEnumerator Await(Task task)
        {
            var deadline=Time.realtimeSinceStartup+30;
            while(!task.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(task.IsCompleted,Is.True,"Stage11 gate timed out.");task.GetAwaiter().GetResult();
        }
        private async Task<SampleHelloPage> Open()
        {
            _ui.RegisterBatch(SampleUIConfiguration.Map(_configs.Snapshot,"hello",c=>c.UseTransition=false));
            return await _ui.Navigator.PushAsync<SampleHelloPage>(new SampleLocalizedHelloArgs(_text,"Ada",_presentation));
        }
        private static async UniTask FinishStyles(PresentationSampleView sample)
        { foreach(var style in sample.Styles)await style.LastRefresh;await UniTask.NextFrame();Canvas.ForceUpdateCanvases(); }
        [UnityTest]
        public IEnumerator LiveExampleSwitchesLocaleThemeAssetsScaleContrastAndFocusWithoutReopening() => Await(Live());
        private async Task Live()
        {
            var page=await Open();var sample=page.ViewObject.GetComponentInChildren<PresentationSampleView>();
            await FinishStyles(sample);
            var native=page.ViewObject;
            var baseSize=sample.Greeting.fontSize;
            var badge=sample.transform.Find("SafeContent/Content/Badge").GetComponent<Image>();
            Assert.That(badge.sprite.name,Is.EqualTo("BadgeEn"));
            _text.SetLocale("zh-CN");_presentation.SetTheme("dark");
            _presentation.SetAccessibility(new UIAccessibilityOptions(1.5f,true,true));
            await FinishStyles(sample);
            Assert.That(page.ViewObject,Is.SameAs(native));
            Assert.That(sample.Greeting.text,Is.EqualTo("你好，Ada！"));
            Assert.That(badge.sprite.name,Is.EqualTo("BadgeZh"));
            Assert.That(sample.Greeting.color,Is.EqualTo(Color.white));
            Assert.That(sample.Greeting.fontSize,Is.EqualTo(baseSize*1.5f));
            _presentation.SetAccessibility(new UIAccessibilityOptions(1.5f,true,true));await FinishStyles(sample);
            Assert.That(sample.Greeting.fontSize,Is.EqualTo(baseSize*1.5f),"Scale must not accumulate.");
            _presentation.SetAccessibility(new UIAccessibilityOptions(1,false,false));await FinishStyles(sample);
            Assert.That(sample.Greeting.fontSize,Is.EqualTo(baseSize));
            Assert.That(_ui.Transitions.ReducedMotion,Is.False);
            var button=sample.FirstFocus.GetComponent<Button>();
            _ui.RootRuntime.EventSystem.SetSelectedGameObject(null);
            _ui.RootRuntime.EventSystem.SetSelectedGameObject(button.gameObject);
            Assert.That(sample.transform.Find("SafeContent/Content/FocusFeedback").GetComponent<TMP_Text>().text,Is.EqualTo("切换语言"));
            var count=_presentation.SubscriberCount;
            await _ui.OpenAsync<SampleHelloPage>(new SampleLocalizedHelloArgs(_text,"Ada",_presentation));
            await FinishStyles(sample);Assert.That(_presentation.SubscriberCount,Is.EqualTo(count));
            await _ui.CloseAsync(page);
            Assert.That(_presentation.SubscriberCount,Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
            _presentation.SetTheme("light");
        }
        [UnityTest]
        public IEnumerator ExampleSafeViewportAndFontScaleRemainUsableAtSixDeviceRatios() => Await(ExampleSizes());
        private async Task ExampleSizes()
        {
            var page=await Open();var sample=page.ViewObject.GetComponentInChildren<PresentationSampleView>();
            var canvas=_ui.RootRuntime.Root.GetComponent<Canvas>();
            var root=(RectTransform)canvas.transform;
            canvas.GetComponent<CanvasScaler>().enabled=false;canvas.renderMode=RenderMode.WorldSpace;
            _ui.RootRuntime.ScreenLayout.enabled=false;
            foreach(var size in new[]{new Vector2(1080,1920),new Vector2(1080,2340),new Vector2(1080,2400),
                new Vector2(1536,2048),new Vector2(2400,1080),new Vector2(2048,1536)})
            {
                root.sizeDelta=new Vector2(1920,1920*size.y/size.x);
                var safe=new Rect(48,72,size.x-96,size.y-180);
                _ui.RootRuntime.ScreenLayout.Apply(new UIScreenFrame(size,safe),new Rect(0,0,1,1));
                foreach(var scale in new[]{1f,1.5f,2f})
                {
                    _presentation.SetAccessibility(new UIAccessibilityOptions(scale));
                    _text.SetLocale("fr");await FinishStyles(sample);await UniTask.NextFrame();
                    var scroll=sample.SafeContent.GetComponent<ScrollRect>();
                    LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);Canvas.ForceUpdateCanvases();
                    foreach(var label in sample.GetComponentsInChildren<TMP_Text>())
                    {
                        label.ForceMeshUpdate();
                        Assert.That(label.isTextOverflowing,Is.False,$"{size} scale={scale}: {label.name} {label.rectTransform.rect} preferred={label.preferredHeight}");
                    }
                    scroll.verticalNormalizedPosition=0;
                    Canvas.ForceUpdateCanvases();
                    var close=sample.transform.Find("SafeContent/Content/sample.close").GetComponent<RectTransform>();
                    var corners=new Vector3[4];close.GetWorldCorners(corners);
                    Assert.That(sample.SafeContent.InverseTransformPoint(corners[0]).y,Is.GreaterThanOrEqualTo(sample.SafeContent.rect.yMin-.1f));
                    Assert.That(((RectTransform)sample.transform).rect.size,Is.EqualTo(root.rect.size),"Backdrop must not inherit safe insets.");
                }
            }
        }
        [UnityTest]
        public IEnumerator RealFontChineseLatinFrenchGlyphMeshesAndRasterEvidence() => Await(FontMesh());
        private async Task FontMesh()
        {
            _extra=new GameObject("RasterRoot",typeof(RectTransform),typeof(Canvas));
            var cameraObject=new GameObject("RasterCamera",typeof(Camera));
            var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.orthographic=true;camera.orthographicSize=2;camera.transform.position=new Vector3(0,0,-10);
            var rt=new RenderTexture(1000,360,24);camera.targetTexture=rt;
            var canvas=_extra.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var go=new GameObject("WenKai rendered sample",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(_extra.transform,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(25,15);rect.offsetMax=new Vector2(-25,-15);
            var tmp=go.GetComponent<TextMeshProUGUI>();tmp.font=Resources.Load<TMP_FontAsset>("YUIPresentation/WenKai");tmp.fontSize=52;tmp.color=Color.white;
            tmp.text="你好，Ada！中文正楷\nHello, Ada!\nBonjour, Ada ! éèàçœ";
            try
            {
                await UniTask.NextFrame();Canvas.ForceUpdateCanvases();tmp.ForceMeshUpdate();
                Assert.That(tmp.textInfo.characterCount,Is.GreaterThan(35));
                var visible=0;
                for(var i=0;i<tmp.textInfo.characterCount;i++)
                {
                    var ch=tmp.textInfo.characterInfo[i];
                    if(char.IsWhiteSpace(ch.character))continue;
                    Assert.That(tmp.font.HasCharacter(ch.character,true,true),Is.True,"Missing "+ch.character);
                    Assert.That(ch.textElement.glyph.index,Is.Not.Zero,"Substituted missing glyph.");
                    if(ch.isVisible)visible++;
                }
                Assert.That(visible,Is.GreaterThan(30));
                Assert.That(tmp.textInfo.meshInfo[0].vertexCount,Is.GreaterThan(100));
                Assert.That(tmp.isTextOverflowing,Is.False);
                camera.Render();
                var previous=RenderTexture.active;
                var image=new Texture2D(1000,360,TextureFormat.RGB24,false);
                try {
                    RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1000,360),0,0);image.Apply();
                    var pixels=image.GetPixels32();var ink=0;
                    foreach(var pixel in pixels)if(pixel.r>80)ink++;
                    var folder=Path.GetDirectoryName(Application.dataPath);
                    File.WriteAllBytes(Path.Combine(folder,"stage11-wenkai-raster.png"),image.EncodeToPNG());
                    Debug.Log($"WenKai raster: {visible} visible glyphs, {tmp.textInfo.meshInfo[0].vertexCount} vertices, {ink} ink pixels; device={SystemInfo.graphicsDeviceType}.");
                    Assert.That(ink,Is.GreaterThan(1500),"Real raster must contain glyph ink, not only strings/mesh.");
                } finally { RenderTexture.active=previous;UnityEngine.Object.Destroy(image); }
                var sourceFallback=tmp.font.fallbackFontAssetTable[0];
                var fallback=UnityEngine.Object.Instantiate(sourceFallback);
                fallback.material=UnityEngine.Object.Instantiate(sourceFallback.material);
                fallback.atlasTextures=new[]{UnityEngine.Object.Instantiate(sourceFallback.atlasTexture)};
                fallback.material.mainTexture=fallback.atlasTexture;
                try
                {
                    var sample=new System.Text.StringBuilder();
                    for(var c=0x4E00;c<0x4E00+300;c++)sample.Append((char)c);
                    Assert.That(fallback.TryAddCharacters(sample.ToString(),out var missing),Is.True,missing);
                    Assert.That(fallback.atlasTextures.Length,Is.EqualTo(1));
                    Assert.That(fallback.atlasWidth,Is.EqualTo(2048));Assert.That(fallback.atlasHeight,Is.EqualTo(2048));
                    Debug.Log($"Dynamic fallback clone: requested300 CJK, chars={fallback.characterTable.Count}, atlases={fallback.atlasTextures.Length}, Alpha8 bytes<=4194304.");
                }
                finally { UnityEngine.Object.Destroy(fallback.material);UnityEngine.Object.Destroy(fallback.atlasTexture);UnityEngine.Object.Destroy(fallback); }
            }
            finally { camera.targetTexture=null;rt.Release();UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(cameraObject); }
        }
        [UnityTest]
        public IEnumerator SafeAreaRealRectsAcrossDeviceRatiosNestedTransformsAndViewportRestore() => Await(SafeArea());
        private async Task SafeArea()
        {
            _extra=new GameObject("LayoutCanvas",typeof(RectTransform),typeof(Canvas));
            var canvas=_extra.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            var root=(RectTransform)_extra.transform;
            var layout=_extra.AddComponent<UIScreenLayout>();layout.enabled=false;
            var parent=(RectTransform)new GameObject("Nested parent",typeof(RectTransform)).transform;parent.SetParent(root,false);
            parent.anchorMin=Vector2.zero;parent.anchorMax=Vector2.one;parent.offsetMin=parent.offsetMax=Vector2.zero;
            var content=(RectTransform)new GameObject("Safe",typeof(RectTransform),typeof(UISafeAreaContent)).transform;content.SetParent(parent,false);
            content.anchorMin=new Vector2(.1f,.1f);content.anchorMax=new Vector2(.9f,.9f);content.offsetMin=new Vector2(3,4);content.offsetMax=new Vector2(-3,-4);
            layout.Initialize(canvas);
            using var binding=content.GetComponent<UISafeAreaContent>().Bind(layout);
            var changes=0;layout.Changed+=()=>changes++;
            foreach(var size in new[]{new Vector2(1080,1920),new Vector2(1080,2340),new Vector2(1080,2400),
                new Vector2(1536,2048),new Vector2(2400,1080),new Vector2(2048,1536)})
            {
                root.sizeDelta=size;
                parent.localScale=Vector3.one;
                var safe=new Rect(48,72,size.x-96,size.y-180);
                var frame=new UIScreenFrame(size,safe);
                layout.Apply(frame,new Rect(0,0,1,1));
                var corners=new Vector3[4];content.GetWorldCorners(corners);
                var lo=root.InverseTransformPoint(corners[0]);var hi=root.InverseTransformPoint(corners[2]);
                Assert.That(lo.x,Is.EqualTo(root.rect.xMin+safe.xMin).Within(.05f));
                Assert.That(lo.y,Is.EqualTo(root.rect.yMin+safe.yMin).Within(.05f));
                Assert.That(hi.x,Is.EqualTo(root.rect.xMin+safe.xMax).Within(.05f));
                Assert.That(hi.y,Is.EqualTo(root.rect.yMin+safe.yMax).Within(.05f));
                var before=changes;layout.Apply(frame,new Rect(0,0,1,1));Assert.That(changes,Is.EqualTo(before),"No duplicate updates.");
                parent.localScale=new Vector3(.75f,.75f,1);
                layout.Apply(frame,new Rect(.1f,.1f,.8f,.8f));
                content.GetWorldCorners(corners);
                Assert.That(parent.InverseTransformPoint(corners[0]).x,Is.GreaterThanOrEqualTo(parent.rect.xMin-.05f));
                Assert.That(parent.InverseTransformPoint(corners[2]).x,Is.LessThanOrEqualTo(parent.rect.xMax+.05f));
                parent.localScale=new Vector3(.6f,.6f,1);parent.anchoredPosition=new Vector2(size.x*.3f,0);
                Canvas.ForceUpdateCanvases();
                content.GetWorldCorners(corners);
                var expected=layout.SafeRectIn(parent);
                Assert.That(parent.InverseTransformPoint(corners[0]).x,Is.EqualTo(expected.xMin).Within(.05f));
                Assert.That(parent.InverseTransformPoint(corners[2]).x,Is.EqualTo(expected.xMax).Within(.05f));
                parent.anchoredPosition=Vector2.zero;
            }
            content.gameObject.SetActive(false);
            Assert.That(content.anchorMin,Is.EqualTo(new Vector2(.1f,.1f)));
            Assert.That(content.offsetMin,Is.EqualTo(new Vector2(3,4)));
            binding.Dispose();
            await UniTask.NextFrame();
        }
        [UnityTest]
        public IEnumerator ReducedMotionUsesExistingRunnerAndRestoresBaseline() => Await(Motion());
        private async Task Motion()
        {
            _extra=new GameObject("Motion",typeof(RectTransform),typeof(CanvasGroup));
            var target=(RectTransform)_extra.transform;target.localScale=new Vector3(.8f,.9f,1);
            var group=_extra.GetComponent<CanvasGroup>();group.alpha=.7f;
            var runner=_ui.Transitions;runner.CaptureBaseline(target);
            _presentation.SetAccessibility(new UIAccessibilityOptions(1,false,true));
            await runner.PlayShowAsync(target,new UITransitionOptions{Type=UITransitionType.Scale,ShowDuration=60,StartScale=.1f});
            Assert.That(group.alpha,Is.EqualTo(.7f));Assert.That(target.localScale,Is.EqualTo(new Vector3(.8f,.9f,1)));
            _presentation.SetAccessibility(new UIAccessibilityOptions(1,false,false));
            var playing=runner.PlayHideAsync(target,new UITransitionOptions{Type=UITransitionType.Fade,HideDuration=60}).AsTask();
            await UniTask.NextFrame();
            _presentation.SetAccessibility(new UIAccessibilityOptions(1,false,true));
            await playing;
            Assert.That(group.alpha,Is.Zero);
            runner.NormalizeVisible(target);Assert.That(group.alpha,Is.EqualTo(.7f));
        }
        [UnityTest]
        public IEnumerator PresentationReadyReloadCallerCancelAndOneThousandDisableCyclesCleanOwners() => Await(Owners());
        private async Task Owners()
        {
            _extra=new GameObject("Repeated binding",typeof(RectTransform),typeof(Image),typeof(ConfigPresentationBinding));
            var style=_extra.GetComponent<ConfigPresentationBinding>();style.SetTokens(color:"text");
            using var bound=style.Bind(_presentation);
            var image=_extra.GetComponent<Image>();var original=Color.white;
            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();
                try { await style.RefreshAsync(canceled.Token);Assert.Fail("Caller cancellation was swallowed."); }catch(OperationCanceledException){}
            }
            for(var i=0;i<1000;i++)
            {
                style.enabled=false;Assert.That(_presentation.SubscriberCount,Is.Zero);
                Assert.That(image.color,Is.EqualTo(original));
                style.enabled=true;await style.LastRefresh;Assert.That(_presentation.SubscriberCount,Is.EqualTo(1));
            }
            await _configs.ShutdownAsync();await style.LastRefresh;Assert.That(image.color,Is.EqualTo(original));
            await _configs.InitializeAsync();await style.LastRefresh;Assert.That(image.color,Is.Not.EqualTo(original));
            await _configs.ReloadAsync();await style.LastRefresh;
            var calls=0;Action bad=()=>throw new InvalidOperationException("observer");Action good=()=>calls++;
            _presentation.Changed+=bad;_presentation.Changed+=good;
            try { Assert.Throws<AggregateException>(()=>_presentation.SetTheme("dark"));Assert.That(calls,Is.EqualTo(1)); }
            finally { _presentation.Changed-=bad;_presentation.Changed-=good; }
            bound.Dispose();Assert.That(_presentation.SubscriberCount,Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
            Assert.That(_text.IsDisposed,Is.False);
        }
        public sealed class StylePage : BasePageContext
        {
            internal ConfigPresentationBinding Style;
            internal bool FailShow;
            private UIPresentationService _owner;
            protected override void HandleInit()
            {
                var child=new GameObject("LocalizedBadge",typeof(RectTransform),typeof(Image),typeof(ConfigPresentationBinding));
                child.transform.SetParent(ViewObject.transform,false);
                Style=child.GetComponent<ConfigPresentationBinding>();Style.SetTokens(sprite:"badge");
            }
            protected override void HandleShow(object args)
            {
                if(args is UIPresentationService owner)_owner=owner;
                TrackDisplayBinding(Style.Bind(_owner,this));
                if(FailShow)throw new InvalidOperationException("style show failure");
            }
        }
        public sealed class StyleCover : BasePageContext {}
        [UnityTest]
        public IEnumerator PresentationHidePoolCloseRollbackFailedShowAndShutdownReleaseAssets() => Await(Navigation());
        private async Task Navigation()
        {
            var config=new UIConfig{Id="Style",PrefabKey="Style",FullScreen=true,CacheOnClose=true,MaxPoolSize=1,UseTransition=false};
            _ui.Register<StylePage>(config);_ui.Register<StyleCover>(new UIConfig{Id="Cover",PrefabKey="Cover",FullScreen=true,UseTransition=false});
            var page=await _ui.Navigator.PushAsync<StylePage>(_presentation);await page.Style.LastRefresh;
            Assert.That(_resources.GetDiagnostics().TotalLeases,Is.EqualTo(1));
            await _ui.Navigator.PushAsync<StyleCover>();
            Assert.That(_presentation.SubscriberCount,Is.Zero);Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
            _text.SetLocale("zh-CN");
            await _ui.Navigator.PopAsync();await page.Style.LastRefresh;
            Assert.That(page.Style.GetComponent<Image>().sprite.name,Is.EqualTo("BadgeZh"));
            config.UseTransition=true;config.TransitionType=UITransitionType.Fade;config.HideDuration=10;
            using(var cancel=new CancellationTokenSource())
            {
                var closing=_ui.CloseAsync(page,cancellationToken:cancel.Token).AsTask();await UniTask.NextFrame();cancel.Cancel();
                try{await closing;Assert.Fail("Expected canceled close.");}catch(OperationCanceledException){}
            }
            Assert.That(page.State,Is.EqualTo(UIContextState.Opened));
            Assert.That(_presentation.SubscriberCount,Is.EqualTo(1));
            _text.SetLocale("fr");await page.Style.LastRefresh;
            Assert.That(page.Style.GetComponent<Image>().sprite.name,Is.EqualTo("BadgeFr"));
            config.UseTransition=false;await _ui.CloseAsync(page);
            Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
            page.FailShow=true;
            try{await _ui.OpenAsync<StylePage>(_presentation);Assert.Fail("Expected failure.");}catch(UILifecycleException){}
            await UniTask.NextFrame();
            Assert.That(_presentation.SubscriberCount,Is.Zero);
            Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
            var fresh=await _ui.OpenAsync<StylePage>(_presentation);await fresh.Style.LastRefresh;
            await _ui.ShutdownAsync();Assert.That(_presentation.IsDisposed,Is.False);
            Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
        }
        [UnityTest]
        public IEnumerator ExternalRootScalePolicyIsAppliedAndRestoredWithoutSafeAreaOnLayers() => Await(ScalePolicy());
        private async Task ScalePolicy()
        {
            await _ui.ShutdownAsync();await UniTask.NextFrame();
            _extra=new GameObject("External root",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(UIRoot));
            var eventObject=new GameObject("External event",typeof(EventSystem),typeof(StandaloneInputModule));eventObject.transform.SetParent(_extra.transform);
            var scaler=_extra.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.referenceResolution=new Vector2(800,600);scaler.matchWidthOrHeight=.2f;
            using(var runtime=UIRootRuntime.CreateExternal(_extra.GetComponent<UIRoot>(),eventObject.GetComponent<EventSystem>(),
                new UIRootRuntimeOptions{ScalePolicy=new UIScalePolicy(new Vector2(1080,1920),CanvasScaler.ScreenMatchMode.Expand)}))
            {
                Assert.That(scaler.uiScaleMode,Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
                Assert.That(scaler.referenceResolution,Is.EqualTo(new Vector2(1080,1920)));
                Assert.That(scaler.screenMatchMode,Is.EqualTo(CanvasScaler.ScreenMatchMode.Expand));
                foreach(UILayer layer in Enum.GetValues(typeof(UILayer)))
                {
                    var rect=runtime.Root.GetLayerRoot(layer);
                    Assert.That(rect.anchorMin,Is.EqualTo(Vector2.zero));Assert.That(rect.anchorMax,Is.EqualTo(Vector2.one));
                    Assert.That(rect.GetComponent<UISafeAreaContent>(),Is.Null);
                }
            }
            Assert.That(scaler.uiScaleMode,Is.EqualTo(CanvasScaler.ScaleMode.ConstantPixelSize));
            Assert.That(scaler.referenceResolution,Is.EqualTo(new Vector2(800,600)));
            Assert.That(scaler.matchWidthOrHeight,Is.EqualTo(.2f));
        }
        [UnityTest]
        public IEnumerator PendingLocalizedSpriteCannotOverwriteNewLanguageOrLeakAfterDisable() => Await(Race());
        private async Task Race()
        {
            var provider=new DelayedProvider();
            var resources=new UIResourceService();resources.Packages.Register(provider,true);
            using var presentation=new UIPresentationService(_configs,_text,resources);
            _extra=new GameObject("AsyncSprite",typeof(RectTransform),typeof(Image),typeof(ConfigPresentationBinding));
            var binding=_extra.GetComponent<ConfigPresentationBinding>();binding.SetTokens(sprite:"badge");
            var handle=binding.Bind(presentation);
            try
            {
                Assert.That(provider.Pending.Count,Is.EqualTo(1));
                _text.SetLocale("zh-CN");Assert.That(provider.Pending.Count,Is.EqualTo(2));
                provider.Complete(1);await binding.LastRefresh;
                Assert.That(_extra.GetComponent<Image>().sprite.name,Is.EqualTo("BadgeZh"));
                provider.Complete(0);await UniTask.NextFrame();
                Assert.That(_extra.GetComponent<Image>().sprite.name,Is.EqualTo("BadgeZh"));
                Assert.That(provider.Releases,Is.EqualTo(1));
                _text.SetLocale("fr");var late=binding.LastRefresh;
                binding.enabled=false;provider.Complete(2);await late;await UniTask.NextFrame();
                Assert.That(_extra.GetComponent<Image>().sprite,Is.Null);
                Assert.That(resources.GetDiagnostics().TotalLeases,Is.Zero);
                handle.Dispose();
                binding.enabled=true;using var fresh=binding.Bind(presentation);provider.CompleteAll();await binding.LastRefresh;
                var count=presentation.SubscriberCount;handle.Dispose();Assert.That(presentation.SubscriberCount,Is.EqualTo(count));
                presentation.Dispose();await binding.LastRefresh;
                Assert.That(_extra.GetComponent<Image>().sprite,Is.Null);
                Assert.That(resources.GetDiagnostics().TotalLeases,Is.Zero);
            }
            finally { handle.Dispose();provider.CompleteAll();await resources.ShutdownAsync();provider.Dispose(); }
        }
        [UnityTest]
        public IEnumerator TMPMaterialTokenChangesRenderingAndRestoresOriginalMaterial() => Await(TmpMaterial());
        private async Task TmpMaterial()
        {
            _extra=new GameObject("Material binding",typeof(RectTransform),typeof(TextMeshProUGUI),typeof(ConfigPresentationBinding));
            var text=_extra.GetComponent<TMP_Text>();var baseline=text.fontSharedMaterial;
            var binding=_extra.GetComponent<ConfigPresentationBinding>();binding.SetTokens(material:"surface");
            using var handle=binding.Bind(_presentation);await binding.LastRefresh;
            Assert.That(text.fontSharedMaterial,Is.SameAs(Resources.Load<Material>("YUIPresentation/Panel")));
            binding.SetTokens();await binding.LastRefresh;
            Assert.That(text.fontSharedMaterial,Is.SameAs(baseline));
            handle.Dispose();Assert.That(_resources.GetDiagnostics().TotalLeases,Is.Zero);
        }
        [UnityTest]
        public IEnumerator PartialAssetFailureRetainsPriorVisualAndReleasesOnlyNewLeases() => Await(PartialFailure());
        private async Task PartialFailure()
        {
            var provider=new DelayedProvider();var resources=new UIResourceService();resources.Packages.Register(provider,true);
            using var presentation=new UIPresentationService(_configs,_text,resources);
            _extra=new GameObject("Partial failure",typeof(RectTransform),typeof(Image),typeof(ConfigPresentationBinding));
            var binding=_extra.GetComponent<ConfigPresentationBinding>();binding.SetTokens(sprite:"badge");
            var handle=binding.Bind(presentation);
            try
            {
                provider.Complete(0);await binding.LastRefresh;
                var before=_extra.GetComponent<Image>().sprite;
                binding.SetTokens(sprite:"badge",material:"surface");
                LogAssert.Expect(LogType.Exception,new System.Text.RegularExpressions.Regex(".*injected material failure.*"));
                provider.Pending[1].gate.TrySetException(new InvalidOperationException("injected material failure"));
                try{await binding.LastRefresh;Assert.Fail("Expected load error.");}catch(ResourceLoadException){}
                Assert.That(binding.LastFailure,Is.Not.Null);
                Assert.That(_extra.GetComponent<Image>().sprite,Is.SameAs(before));
                Assert.That(resources.GetDiagnostics().TotalLeases,Is.EqualTo(1));
                binding.SetTokens(sprite:"badge");await binding.LastRefresh;
                Assert.That(binding.LastFailure,Is.Null);
                handle.Dispose();Assert.That(resources.GetDiagnostics().TotalLeases,Is.Zero);
            }
            finally{handle.Dispose();provider.CompleteAll();await resources.ShutdownAsync();provider.Dispose();}
        }
        private sealed class DelayedProvider : IUIResourceProvider,IDisposable
        {
            public string PackageName=>"Delayed";
            internal readonly List<(UIResourceKey key,UniTaskCompletionSource<IUINativeAssetHandle> gate)> Pending=new List<(UIResourceKey,UniTaskCompletionSource<IUINativeAssetHandle>)>();
            private readonly List<UnityEngine.Object> _assets=new List<UnityEngine.Object>();
            internal int Releases;
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key)
            { var gate=new UniTaskCompletionSource<IUINativeAssetHandle>();Pending.Add((key,gate));return gate.Task; }
            internal void Complete(int index)
            {
                var p=Pending[index];if(p.gate.Task.Status!=UniTaskStatus.Pending)return;
                var texture=new Texture2D(4,4);var sprite=Sprite.Create(texture,new Rect(0,0,4,4),Vector2.zero);
                sprite.name=Path.GetFileName(p.key.Location);_assets.Add(sprite);_assets.Add(texture);
                p.gate.TrySetResult(new Handle(this,sprite));
            }
            internal void CompleteAll(){for(var i=0;i<Pending.Count;i++)Complete(i);}
            public UniTask ShutdownAsync()=>UniTask.CompletedTask;
            public void Dispose(){foreach(var asset in _assets)UnityEngine.Object.Destroy(asset);}
            private sealed class Handle : IUINativeAssetHandle
            {
                private readonly DelayedProvider _owner;
                internal Handle(DelayedProvider owner,UnityEngine.Object asset){_owner=owner;Asset=asset;}
                public UnityEngine.Object Asset{get;private set;}
                public bool IsValid=>Asset;
                public void Release(){Assert.That(Asset,Is.Not.Null);Asset=null;_owner.Releases++;}
            }
        }
    }
}
