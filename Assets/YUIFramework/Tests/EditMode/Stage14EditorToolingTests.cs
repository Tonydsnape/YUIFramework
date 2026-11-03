using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using YUIFramework.Editor;
using YUIFramework.Localization;
using YUIFramework.Localization.Editor;
using YooAsset.Editor;
using Object = UnityEngine.Object;
using UnityEngine.TestTools;

namespace YUIFramework.Tests
{
    public sealed class Stage14EditorToolingTests
    {
        private const string Root = "Assets/YUIStage14TestAssets";
        private UIBuildProfile _profile;
        private readonly List<Object> _owned = new List<Object>();
        private string _book;
        private byte[] _bookBytes;
        [SetUp]
        public void SetUp()
        {
            Assert.IsFalse(AssetDatabase.IsValidFolder(Root), "Do not overwrite existing user fixtures.");
            AssetDatabase.CreateFolder("Assets", "YUIStage14TestAssets");
            _profile = ScriptableObject.CreateInstance<UIBuildProfile>(); _owned.Add(_profile);
            _profile.Policy = UIBuildPolicy.MinimalDemo; _profile.CheckTextContent = false;
            _profile.AuditedDemoResourcePaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
                p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            _book = Path.GetFullPath("Config/UISettings.xlsx");
            if (File.Exists(_book)) _bookBytes = File.ReadAllBytes(_book);
        }
        [TearDown]
        public void TearDown()
        {
            foreach (var item in _owned) if (item) Object.DestroyImmediate(item);
            _owned.Clear();
            if (_bookBytes != null) File.WriteAllBytes(_book, _bookBytes);
            _bookBytes = null;
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.Refresh();
        }
        private GameObject Create(string name = "Panel")
        {
            var root = new GameObject(name, typeof(RectTransform)); _owned.Add(root); return root;
        }
        private GameObject Prefab(GameObject root, string name = "Panel")
        {
            return PrefabUtility.SaveAsPrefabAsset(root, Root + "/" + name + ".prefab");
        }
        private UIEditorReport Inspect(GameObject root)
        {
            var report = new UIEditorReport();
            UIAssetValidator.InspectHierarchy(root, Root + "/Panel.prefab", _profile, null, report);
            return report;
        }
        [Test]
        public void Stage14_MissingProfileAndProductionResourcesCannotPass()
        {
            Assert.IsTrue(UIAssetValidator.Validate(null).HasErrors);
            _profile.Policy = UIBuildPolicy.Production;
            _profile.Extras = new Object[] { Prefab(Create()) };
            var report = UIAssetValidator.Validate(_profile);
            Assert.IsFalse(report.ProductionCertified); Assert.IsFalse(report.Complete);
            Assert.That(report.Issues.Any(i => i.Code == "CollectorUnavailable"));
            Assert.That(report.Issues.Any(i => i.Code == "ProductionResources" && i.AssetPath.Contains("/Resources/")));
        }
        [Test]
        public void Stage14_MinimalDemoIsExplicitAndScanDoesNotIncludeUnrelatedPrefabs()
        {
            var bad = Create("NotInScope"); bad.AddComponent<Stage14RequiredReferenceProbe>(); Prefab(bad, "NotInScope");
            _profile.Extras = new Object[] { Prefab(Create()) };
            var report = UIAssetValidator.Validate(_profile);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Issues));
            Assert.IsFalse(report.ProductionCertified);
            Assert.IsFalse(report.Coverage.Contains(Root + "/NotInScope.prefab"));
            _profile.AuditedDemoResourcePaths = Array.Empty<string>();
            Assert.That(UIAssetValidator.Validate(_profile).Issues.Any(i => i.Code == "UnauditedDemoResources"));
        }
        [Test]
        public void Stage14_RequiredVersusNullableReferencesAreLocated()
        {
            var root = Create(); var probe = root.AddComponent<Stage14RequiredReferenceProbe>();
            var report = Inspect(root);
            var issue = report.Issues.Single(i => i.Code == "RequiredReference");
            Assert.AreEqual("Required", issue.Property); StringAssert.Contains("Panel[", issue.ObjectPath);
            probe.Required = root;
            Assert.IsFalse(Inspect(root).Issues.Any(i => i.Code.EndsWith("Reference")));
        }
        [Test]
        public void Stage14_RequiredReferenceReportActuallySelectsDuplicateNamedPrefabObject()
        {
            var root = Create();
            new GameObject("Duplicate", typeof(RectTransform)).transform.SetParent(root.transform, false);
            var child = new GameObject("Duplicate", typeof(RectTransform), typeof(Stage14RequiredReferenceProbe));
            child.transform.SetParent(root.transform, false);
            var path = AssetDatabase.GetAssetPath(Prefab(root));
            var bytes = File.ReadAllBytes(path);
            var issue = Inspect(AssetDatabase.LoadAssetAtPath<GameObject>(path)).Issues.Single(i => i.Code == "RequiredReference");
            var selection = Selection.activeObject;
            try
            {
                UIEditorToolingWindow.LocateIssue(issue);
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                Assert.IsNotNull(stage); Assert.AreEqual(path, stage.assetPath);
                Assert.AreEqual(stage.prefabContentsRoot.transform.GetChild(1).gameObject, Selection.activeGameObject);
                Assert.AreEqual("Required", issue.Property);
            }
            finally { StageUtility.GoToMainStage(); Selection.activeObject = selection; }
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        [Test]
        public void Stage14_MissingSerializedReferenceAndMissingScriptAreLocated()
        {
            var referenced = new Texture2D(1, 1);
            AssetDatabase.CreateAsset(referenced, Root + "/reference.asset");
            var root = Create(); var probe = root.AddComponent<Stage14RequiredReferenceProbe>();
            probe.Required = root; probe.Optional = referenced;
            var prefab = Prefab(root); var path = AssetDatabase.GetAssetPath(prefab);
            AssetDatabase.DeleteAsset(Root + "/reference.asset");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Assert.That(Inspect(AssetDatabase.LoadAssetAtPath<GameObject>(path)).Issues.Any(i => i.Code == "MissingReference"));
            var text = File.ReadAllText(path);
            var scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(probe)));
            File.WriteAllText(path, text.Replace(scriptGuid, "00000000000000000000000000000001"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Assert.That(Inspect(AssetDatabase.LoadAssetAtPath<GameObject>(path)).Issues.Any(i => i.Code == "MissingScript"));
        }
        [Test]
        public void Stage14_PrefabCanvasRaycastMaskLayoutAndDuplicateNameRisks()
        {
            var root = Create(); root.AddComponent<Canvas>();
            var a = new GameObject("Duplicate", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(Mask),
                typeof(RectMask2D), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            a.transform.SetParent(root.transform, false);
            a.GetComponent<Canvas>().overrideSorting = true;
            var b = new GameObject("Duplicate", typeof(RectTransform)); b.transform.SetParent(root.transform, false);
            var report = Inspect(root);
            foreach (var code in new[] { "NestedSortingCanvas", "RaycastTarget", "MaskRisk", "LayoutRisk", "DuplicateName" })
                Assert.That(report.Issues.Any(i => i.Code == code), code);
            Assert.That(report.Issues.Where(i => i.Code != "NestedSortingCanvas" && i.Code != "CanvasCamera").All(i => i.Severity == "Warning"));
        }
        [Test]
        public void Stage14_RealCollectorDetectsDuplicateAddressAndInvalidRule()
        {
            var setting = Collector();
            var package = setting.GetPackage("TestPackage");
            AssetDatabase.CreateFolder(Root, "Other");
            File.WriteAllText(Root + "/Other/data.txt", "duplicate"); AssetDatabase.Refresh();
            package.Groups[0].Collectors.Add(new BundleCollector { CollectPath = Root + "/Other/data.txt", PackRuleName = nameof(PackSeparately) });
            _profile.Collector = setting; _profile.Packages = new[] { "TestPackage" };
            var report = new UIEditorReport();
            UIAssetValidator.CollectAddresses(_profile, report);
            Assert.That(report.Issues.Any(i => i.Code == "CollectorFailure"));
            package.Groups[0].Collectors.RemoveAt(1);
            package.Groups[0].Collectors[0].FilterRuleName = "NotARealRule";
            report = new UIEditorReport(); UIAssetValidator.CollectAddresses(_profile, report);
            Assert.IsTrue(report.HasErrors); Assert.IsFalse(report.Complete);
        }
        [Test]
        public void Stage14_RealCollectorResolvesPackageAddressAndDependencyGraph()
        {
            _profile.Collector = Collector(); _profile.Packages = new[] { "TestPackage" };
            var report = new UIEditorReport();
            var map = UIAssetValidator.CollectAddresses(_profile, report);
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.Issues));
            Assert.AreEqual(Root + "/data.txt", map["TestPackage:data"]);
            Assert.That(report.Graph.Any(s => s.Contains("package:TestPackage/data")));
            _profile.Collector.GetPackage("TestPackage").LocationToLower = true;
            _profile.DefaultPackage = "TestPackage";
            _profile.Extras = new Object[] { AssetDatabase.LoadMainAssetAtPath(Root + "/data.txt") };
            _profile.RequiredResources = new[] { new UIResourceEntry { Package = "", Address = "DATA" } };
            Assert.IsFalse(UIAssetValidator.Validate(_profile).HasErrors);
            Assert.AreEqual("TestPackage:data", UIAssetValidator.AddressKey(_profile, "TestPackage", "DATA"));
            _profile.RequiredResources = new[] { new UIResourceEntry { Package = "WrongPackage", Address = "data" } };
            _profile.Extras = new Object[] { AssetDatabase.LoadMainAssetAtPath(Root + "/data.txt") };
            Assert.That(UIAssetValidator.Validate(_profile).Issues.Any(i => i.Code == "MissingUIAddress"));
        }
        private BundleCollectorSetting Collector()
        {
            var setting = ScriptableObject.CreateInstance<BundleCollectorSetting>(); _owned.Add(setting);
            File.WriteAllText(Root + "/data.txt", "real asset bundle fixture"); AssetDatabase.Refresh();
            setting.Packages.Add(new BundleCollectorPackage { PackageName = "TestPackage", EnableAddressable = true,
                Groups = new List<BundleCollectorGroup> { new BundleCollectorGroup { GroupName = "UI",
                    Collectors = new List<BundleCollector> { new BundleCollector { CollectPath = Root + "/data.txt", PackRuleName = nameof(PackSeparately) } } } } });
            return setting;
        }
        [Test]
        public void Stage14_ExplicitPageMapMissingConfigAndNavigationEndpointsAreErrors()
        {
            _profile.Pages = new[] { new UIPageEntry { Profile = "bootstrap", Id = "DoesNotExist",
                ContextType = typeof(SampleHelloPage).AssemblyQualifiedName, Prefab = Prefab(Create()) } };
            _profile.Navigation = new[] { new UINavigationEdge { From = "DoesNotExist", To = "Unknown", Action = "push" } };
            var report = UIAssetValidator.Validate(_profile);
            foreach (var code in new[] { "MissingUIRow", "UnmappedUIRow", "NavigationEndpoint" })
                Assert.That(report.Issues.Any(i => i.Code == code), code);
        }
        [Test]
        public void Stage14_ActualPlayerPreprocessContractRejectsUnscannedSceneAndWritesReport()
        {
            _profile.Extras = new Object[] { Prefab(Create()) };
            Assert.Throws<BuildFailedException>(() => UIBuildGate.ValidateForBuild(_profile, new[] { "Assets/Unscanned.unity" }));
            Assert.IsTrue(File.Exists("Library/YUIFramework/last-validation.json"));
            var data = File.ReadAllText("Library/YUIFramework/last-validation.json");
            StringAssert.Contains("UnscannedBuildScene", data);
            Assert.That(new UIBuildGate().callbackOrder, Is.LessThan(0));
        }
        [Test]
        public void Stage14_SerializedSceneIsScannedInPreviewSceneAndClosedWithoutChangingSource()
        {
            var beforeCount = EditorSceneManager.previewSceneCount;
            var root = Create(); root.AddComponent<Stage14RequiredReferenceProbe>();
            var path = Root + "/Target.unity";
            // A scene can contain the same serialized GameObject/Transform documents
            // as a prefab. Avoid changing or saving the test runner's untitled scene.
            File.Copy(AssetDatabase.GetAssetPath(Prefab(root)), path);
            AssetDatabase.ImportAsset(path);
            var bytes = File.ReadAllBytes(path);
            _profile.Scenes = new[] { AssetDatabase.LoadAssetAtPath<SceneAsset>(path) };
            var report = UIAssetValidator.Validate(_profile);
            Assert.IsTrue(report.Complete, string.Join("\n", report.Issues));
            Assert.That(report.Issues.Any(i => i.Code == "RequiredReference" && i.AssetPath == path));
            Assert.AreEqual(beforeCount, EditorSceneManager.previewSceneCount);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        }
        [Test]
        public void Stage14_FontAtlasBudgetAndRealTextKeyGlyphOverflowChecksRemainLocatable()
        {
            var root = Create(); var text = root.AddComponent<TextMeshProUGUI>();
            text.rectTransform.sizeDelta = new Vector2(1, 1);
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/YUIPresentation/WenKai.asset");
            text.fontSize = 100;
            text.text = "A very long overflowing line \U0010FFFF";
            root.AddComponent<ConfigLocalizedText>().TextKey = new LocalizedTextKey("not.a.real.key");
            var source = Prefab(root);
            _profile.CheckTextContent = true; _profile.MaximumAtlasSize = 1;
            _profile.Extras = new Object[] { source };
            var report = UIAssetValidator.Validate(_profile);
            foreach (var code in new[] { "FontAtlas", "MissingKey", "MissingGlyph", "Overflow" })
                Assert.That(report.Issues.Any(i => i.Code == code), code);
            Assert.That(report.Issues.Where(i => i.Code == "Overflow").All(i => i.Severity == "Warning"));
        }
        [Test]
        public void Stage14_SpriteAtlasUsesActualPackablesAndBudgetWithoutPackingOrMutating()
        {
            var atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, Root + "/Empty.spriteatlas");
            _profile.Extras = new Object[] { atlas }; _profile.MaximumAtlasSize = 1;
            var bytes = File.ReadAllBytes(Root + "/Empty.spriteatlas");
            var report = UIAssetValidator.Validate(_profile);
            Assert.That(report.Issues.Any(i => i.Code == "SpriteAtlasSource"));
            Assert.That(report.Issues.Any(i => i.Code == "SpriteAtlasBudget"));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Root + "/Empty.spriteatlas"));
        }
        [Test]
        public void Stage14_ProductionPageNeedsItsActualUIConfigPrefabAddressInTheSamePackage()
        {
            _profile.Policy = UIBuildPolicy.Production; _profile.DefaultPackage = "TestPackage";
            _profile.Collector = Collector(); _profile.Packages = new[] { "TestPackage" };
            _profile.Pages = new[] { new UIPageEntry { Profile = "bootstrap", Id = "HelloPage",
                ContextType = typeof(SampleHelloPage).AssemblyQualifiedName, Prefab = Prefab(Create()) } };
            var report = UIAssetValidator.Validate(_profile);
            Assert.That(report.Issues.Any(i => i.Code == "MissingUIAddress" && i.Message.Contains("TestPackage:SampleHelloPage")));
            Assert.IsFalse(report.ProductionCertified);
        }
        [Test]
        public void Stage14_EnvironmentCapabilitiesAreReportedWithoutClaimingMobileValidation()
        {
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-editor-capabilities-" +
                new DirectoryInfo(Path.Combine(Application.dataPath, "..")).Name + ".json")),
                JsonConvert.SerializeObject(new { unity = Application.unityVersion, activeTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    graphics = SystemInfo.graphicsDeviceType.ToString(), windowsModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                    androidModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                    iosModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS),
                    devicePerformance = "NotValidated", graphicsPreview = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? "Unavailable (Null device)" : "Available, separate explicit render test required" },
                    Formatting.Indented));
        }
        [Test]
        public void Stage14_DeviceWindowUsesExistingSafeAreaCoreAtPortraitAndLandscapeSizes()
        {
            var count = EditorSceneManager.previewSceneCount;
            var root = Create(); root.AddComponent<TextMeshProUGUI>();
            root.AddComponent<ConfigLocalizedText>().TextKey = new LocalizedTextKey("sample.title");
            var window = ScriptableObject.CreateInstance<UIDevicePreview>();
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(UIDevicePreview).GetField("_prefab", flags).SetValue(window, root);
                typeof(UIDevicePreview).GetField("_options", flags).SetValue(window, new UIEditorPreviewOptions { Locale = "fr" });
                typeof(UIDevicePreview).GetField("_useConfig", flags).SetValue(window, true);
                var presets = new[] { 0, 2, 4 }; var sizes = new[] { new Vector2(1080, 1920), new Vector2(1080, 2400), new Vector2(2400, 1080) };
                for (var i = 0; i < presets.Length; i++)
                {
                    typeof(UIDevicePreview).GetMethod("Cleanup", flags).Invoke(window, null);
                    typeof(UIDevicePreview).GetField("_preset", flags).SetValue(window, presets[i]);
                    typeof(UIDevicePreview).GetMethod("Build", flags).Invoke(window, null);
                    var canvasRoot = (GameObject)typeof(UIDevicePreview).GetField("_root", flags).GetValue(window);
                    Assert.AreEqual(sizes[i], ((RectTransform)canvasRoot.transform).sizeDelta);
                    var safe = (RectTransform)canvasRoot.transform.GetChild(0);
                    Assert.That(safe.offsetMin.y, Is.EqualTo(60).Within(.01f));
                    Assert.That(safe.offsetMax.y, Is.EqualTo(sizes[i].y - 90).Within(.01f));
                    Assert.AreEqual("Exemple de localisation", canvasRoot.GetComponentInChildren<TMP_Text>().text);
                }
            }
            finally { Object.DestroyImmediate(window); }
            Assert.AreEqual(count, EditorSceneManager.previewSceneCount);
        }
        [Test]
        public void Stage14_ProductionWorkspaceRejectsExcludingActualTargetDependencies()
        {
            _profile.Policy = UIBuildPolicy.Production;
            var font = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/YUIPresentation/WenKai.asset");
            _profile.Extras = new[] { font };
            _profile.AuditedDemoResourcePaths = new[] { AssetDatabase.GetAssetPath(font) };
            var destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-refused-workspace-" + Guid.NewGuid().ToString("N")));
            Assert.Throws<InvalidOperationException>(() => UIBuildWorkspace.Export(_profile, destination));
            Assert.IsFalse(Directory.Exists(destination));
        }
        [Test]
        public void Stage14_ProductionWorkspaceActuallyExcludesAuditedDemoResourcesWithoutMovingSource()
        {
            _profile.Policy = UIBuildPolicy.Production;
            _profile.Extras = new Object[] { Prefab(Create()) };
            var fontPath = "Assets/Resources/YUIPresentation/WenKaiFallback.asset";
            var original = File.ReadAllBytes(fontPath);
            AssetDatabase.CreateAsset(_profile, Root + "/Production.asset"); _owned.Remove(_profile);
            var destination = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "s14p-" + Guid.NewGuid().ToString("N").Substring(0, 10)));
            Assert.AreEqual(destination, UIBuildWorkspace.Export(_profile, destination));
            Assert.IsFalse(File.Exists(Path.Combine(destination, fontPath)));
            Assert.IsFalse(File.Exists(Path.Combine(destination, fontPath + ".meta")));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "Assets/YUIStage14TestAssets/Panel.prefab")));
            StringAssert.Contains("PreparedNotValidated", File.ReadAllText(Path.Combine(destination, "ui-staging-manifest.json")));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(fontPath));
            Assert.Throws<ArgumentException>(() => UIBuildWorkspace.Export(_profile, destination));
        }
        [Test, Explicit("Negative real Player pipeline producer; run separately so its expected build errors cannot contaminate ordinary suites.")]
        public void Stage14_PlayerBuildPipelineActuallyInvokesPreprocessor()
        {
            var source = Prefab(Create());
            var scenePath = Root + "/BuildTarget.unity";
            File.Copy(AssetDatabase.GetAssetPath(source), scenePath); AssetDatabase.ImportAsset(scenePath);
            var oldProfile = UIBuildGate.ActiveProfilePath;
            var oldIgnore = LogAssert.ignoreFailingMessages;
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-blocked-player", Guid.NewGuid().ToString("N"), "Blocked.exe"));
            try
            {
                UIBuildGate.ActiveProfilePath = Root + "/MissingProfile.asset";
                LogAssert.ignoreFailingMessages = true;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath },
                    target = EditorUserBuildSettings.activeBuildTarget, locationPathName = output });
                Assert.AreEqual(UnityEditor.Build.Reporting.BuildResult.Failed, report.summary.result);
                var messages = report.steps.SelectMany(s => s.messages).Select(m => m.content).ToArray();
                Assert.That(messages.Any(m => m.Contains("MissingProfile") || m.Contains("YUI validation blocked")), string.Join("\n", messages));
                Assert.IsFalse(File.Exists(output));
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-player-preprocess-evidence.json")),
                    JsonConvert.SerializeObject(new { failed = true, errors = report.summary.totalErrors, messages,
                        outputCreated = File.Exists(output) }, Formatting.Indented));
            }
            finally { LogAssert.ignoreFailingMessages = oldIgnore; UIBuildGate.ActiveProfilePath = oldProfile; }
        }
        [Test, Explicit("Start a separate Unity process WITHOUT -nographics to verify and capture the real Editor preview renderer.")]
        public void Stage14_RealDevicePreviewRendererProducesSafeAreaAndLocalizedContent()
        {
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType, "A Null device cannot verify rendered UI.");
            var root = Create(); var text = root.AddComponent<TextMeshProUGUI>();
            text.rectTransform.sizeDelta = new Vector2(700, 500); text.fontSize = 64;
            root.AddComponent<ConfigLocalizedText>().TextKey = new LocalizedTextKey("sample.title");
            root.AddComponent<ConfigPresentationBinding>().SetTokens(color: "text", font: "body");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/YUIPresentation/WenKai.asset");
            var window = ScriptableObject.CreateInstance<UIDevicePreview>();
            Texture2D image = null;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(UIDevicePreview).GetField("_prefab", flags).SetValue(window, root);
                typeof(UIDevicePreview).GetField("_options", flags).SetValue(window, new UIEditorPreviewOptions { Locale = "fr", Theme = "dark" });
                typeof(UIDevicePreview).GetField("_useConfig", flags).SetValue(window, true);
                typeof(UIDevicePreview).GetField("_resolve", flags).SetValue(window,
                    new Func<string, string, Type, Object>((package, address, type) => font));
                typeof(UIDevicePreview).GetMethod("Build", flags).Invoke(window, null);
                var preview = (PreviewRenderUtility)typeof(UIDevicePreview).GetField("_preview", flags).GetValue(window);
                var presentation = (UIEditorPresentationPreview)typeof(UIDevicePreview).GetField("_presentation", flags).GetValue(window);
                preview.BeginStaticPreview(new Rect(0, 0, 320, 568));
                var canvasRoot = (GameObject)typeof(UIDevicePreview).GetField("_root", flags).GetValue(window);
                var mesh = canvasRoot.GetComponent<CanvasRenderer>().GetMesh();
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-preview-render-state.json")),
                    JsonConvert.SerializeObject(new { vertices = mesh ? mesh.vertexCount : -1, canvas = canvasRoot.GetComponent<Canvas>().isActiveAndEnabled,
                        rootScene = canvasRoot.scene.name, cameraScene = preview.camera.gameObject.scene.name,
                        cameraType = preview.camera.cameraType.ToString(), cameraMask = preview.camera.cullingMask,
                        clip = new[] { preview.camera.nearClipPlane, preview.camera.farClipPlane },
                        position = preview.camera.WorldToViewportPoint(canvasRoot.transform.position).ToString(),
                        size = ((RectTransform)canvasRoot.transform).rect.ToString(), scale = canvasRoot.transform.lossyScale.ToString() }, Formatting.Indented));
                using (presentation.UseSettings()) preview.camera.Render();
                image = preview.EndStaticPreview();
                File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-real-device-preview.png")), image.EncodeToPNG());
                var pixels = image.GetPixels32();
                Assert.That(pixels.Count(p => p.r > p.g * 1.5f), Is.GreaterThan(100), "Unsafe region must actually render.");
                Assert.That(pixels.Count(p => p.g > p.r * 1.5f), Is.GreaterThan(100), "Safe content must actually render.");
                Assert.AreEqual("Exemple de localisation", presentation.Clone.GetComponent<TMP_Text>().text);
            }
            finally { if (image) Object.DestroyImmediate(image); Object.DestroyImmediate(window); }
        }
        [TestCase("class")]
        [TestCase("Bad-Name")]
        [TestCase("4Panel")]
        public void Stage14_GeneratorRejectsInvalidIdentifiers(string value) =>
            Assert.Throws<ArgumentException>(() => UIPageGenerator.ValidateIdentifier(value));
        [Test]
        public void Stage14_GeneratorRejectsPathEscapeAndExistingAssemblyDependency()
        {
            Assert.Throws<ArgumentException>(() => UIPageGenerator.SafeFolder("Assets/../Outside"));
            Assert.Throws<ArgumentException>(() => UIPageGenerator.SafeFolder("Assets/Editor/Page"));
            Assert.Throws<ArgumentException>(() => UIPageGenerator.SafeFolder("Assets/YUIFramework/Runtime/GeneratedChild"));
            Assert.Throws<InvalidOperationException>(() => UIPageGenerator.Generate(new UIPageGenerationRequest
            { Name = "Runtime", Namespace = "YUIFramework", Prefab = Prefab(Create()), OutputFolder = Root + "/Collision" }, importAssets: false));
            Assert.IsFalse(Directory.Exists(Root + "/Collision"));
        }
        [Test, Platform("Win")]
        public void Stage14_GeneratorRejectsRealJunctionEscapeAndReportCannotFollowIt()
        {
            var target = Path.GetFullPath("Library/YUIStage14LinkTarget");
            var link = Path.GetFullPath(Root + "/Linked");
            var reportLink = Path.GetFullPath("Library/YUIStage14ReportLink");
            Assert.IsFalse(Directory.Exists(target), "Never overwrite another fixture.");
            Assert.IsFalse(Directory.Exists(reportLink));
            Directory.CreateDirectory(target);
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"New-Item -ItemType Junction -Path '" + link +
                    "' -Value '" + target + "' | Out-Null; New-Item -ItemType Junction -Path '" + reportLink +
                    "' -Value '" + Path.GetFullPath(Root) + "' | Out-Null\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var process = System.Diagnostics.Process.Start(start);
                var error = process.StandardError.ReadToEndAsync();
                process.StandardOutput.ReadToEnd(); process.WaitForExit();
                Assert.AreEqual(0, process.ExitCode, error.GetAwaiter().GetResult());
                Assert.Throws<ArgumentException>(() => UIPageGenerator.SafeFolder(Root + "/Linked/Page"));
                Assert.Throws<ArgumentException>(() => UIBuildGate.WriteReport(new UIEditorReport(), reportLink + "/report.json"));
                Assert.IsFalse(File.Exists(Root + "/report.json"));
                Assert.IsEmpty(Directory.GetFiles(target));
            }
            finally
            {
                if (Directory.Exists(link)) Directory.Delete(link);
                if (Directory.Exists(reportLink)) Directory.Delete(reportLink);
                Directory.Delete(target);
            }
        }
        [Test, Explicit("Generated C# imports must run in an isolated Unity process.")]
        public void Stage14_GeneratorSerializesDuplicateNamedControlsPreservesHandCodeAndRollsBack()
        {
            Assert.IsNotNull(_bookBytes, "Validation project must include Config tooling and existing dependencies.");
            var root = Create();
            for (var i = 0; i < 2; i++)
                new GameObject("Same", typeof(RectTransform), typeof(Image), typeof(Button)).transform.SetParent(root.transform, false);
            var request = new UIPageGenerationRequest { Name = "Stage14Generated", Namespace = "Stage14.Generated",
                OutputFolder = Root + "/Generated", Prefab = Prefab(root), Profile = "stage14", Id = "Stage14Generated", Address = "Stage14Generated" };
            for (var failure = 1; failure <= 8; failure++)
            {
                var failAt = failure;
                Assert.Throws<InvalidOperationException>(() => UIPageGenerator.Generate(request, index =>
                { if (index == failAt) throw new InvalidOperationException("injected write fault"); }, false));
                Assert.That(Directory.GetFiles(Root + "/Generated"), Is.Empty);
                CollectionAssert.AreEqual(_bookBytes, File.ReadAllBytes(_book));
            }
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var blocked = Root + "/Generated/Stage14GeneratedView.g.cs";
                try
                {
                    var error = Assert.Throws<AggregateException>(() => UIPageGenerator.Generate(request, index =>
                    {
                        if (index != 8) return;
                        File.SetAttributes(blocked, FileAttributes.ReadOnly);
                        throw new InvalidOperationException("injected final write failure");
                    }, false));
                    Assert.AreEqual(2, error.InnerExceptions.Count);
                    CollectionAssert.AreEqual(_bookBytes, File.ReadAllBytes(_book));
                    Assert.AreEqual(1, Directory.GetFiles(Root + "/Generated").Length);
                }
                finally { if (File.Exists(blocked)) { File.SetAttributes(blocked, FileAttributes.Normal); File.Delete(blocked); } }
            }
            var result = UIPageGenerator.Generate(request, importAssets: false);
            Assert.That(result.Bindings.Any(b => b.Route.SequenceEqual(new[] { 0 })));
            Assert.That(result.Bindings.Any(b => b.Route.SequenceEqual(new[] { 1 })));
            var handPath = Root + "/Generated/Stage14GeneratedContext.cs";
            File.AppendAllText(handPath, "// preserved hand code\n");
            UIPageGenerator.Generate(request, importAssets: false);
            StringAssert.EndsWith("// preserved hand code\n", File.ReadAllText(handPath));
            StringAssert.Contains("UIConfigRegistration.For<Stage14GeneratedContext>", File.ReadAllText(Root + "/Generated/Stage14GeneratedRegistration.g.cs"));
            File.AppendAllText(Root + "/Generated/Stage14GeneratedView.g.cs", "// conflict");
            Assert.Throws<InvalidOperationException>(() => UIPageGenerator.Generate(request, importAssets: false));
        }
        [Test]
        public void Stage14_ConfigPreviewChangesRealTextThemeScaleImageAndMaterialWithoutSourceMutation()
        {
            var root = Create();
            root.AddComponent<Stage14RequiredReferenceProbe>().Required = root;
            var awakeCalls = Stage14RequiredReferenceProbe.AwakeCalls;
            var input = new GameObject("Inactive input");
            input.SetActive(false); input.transform.SetParent(root.transform, false);
            input.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var text = root.AddComponent<TextMeshProUGUI>(); text.fontSize = 30;
            var label = root.AddComponent<ConfigLocalizedText>(); label.TextKey = new LocalizedTextKey("sample.title");
            root.AddComponent<ConfigPresentationBinding>().SetTokens(color: "text", font: "body");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/YUIPresentation/WenKaiFallback.asset");
            var panel = new GameObject("Badge", typeof(RectTransform), typeof(Image), typeof(ConfigPresentationBinding));
            panel.transform.SetParent(root.transform, false);
            panel.GetComponent<ConfigPresentationBinding>().SetTokens(color: "panel", sprite: "badge", material: "surface");
            var badge = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/YUIPresentation/BadgeFr.png");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/YUIPresentation/Panel.mat");
            var materialBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(material));
            var before = File.ReadAllBytes(AssetDatabase.GetAssetPath(font));
            var settings = TMP_Settings.instance; var sceneCount = EditorSceneManager.previewSceneCount;
            var holder = Create("Holder");
            var options = new UIEditorPreviewOptions { Locale = "fr", Theme = "dark", FontScale = 1.5f };
            using (var preview = new UIEditorPresentationPreview(root, holder.transform, options,
                (package, address, type) => type == typeof(TMP_FontAsset) ? font : type == typeof(Sprite) ? badge : material))
            {
                var copy = preview.Clone.GetComponent<TMP_Text>();
                Assert.AreEqual(45, copy.fontSize); Assert.AreNotSame(font, copy.font);
                Assert.AreEqual(awakeCalls, Stage14RequiredReferenceProbe.AwakeCalls, "Disabled business components must not run Awake on activation.");
                Assert.IsNull(preview.Clone.GetComponent<Stage14RequiredReferenceProbe>());
                Assert.IsEmpty(preview.Clone.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true));
                Assert.AreNotEqual(text.color, copy.color); Assert.AreEqual("Exemple de localisation", copy.text);
                Assert.AreEqual(30, text.fontSize); Assert.AreSame(settings, TMP_Settings.instance);
                var image = preview.Clone.GetComponentInChildren<Image>();
                Assert.AreSame(badge, image.sprite); Assert.AreNotSame(material, image.material);
                Assert.AreEqual(material.shader, image.material.shader);
            }
            Assert.AreEqual(0, holder.transform.childCount); Assert.AreSame(settings, TMP_Settings.instance);
            Assert.AreEqual(sceneCount, EditorSceneManager.previewSceneCount);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(AssetDatabase.GetAssetPath(font)));
            CollectionAssert.AreEqual(materialBytes, File.ReadAllBytes(AssetDatabase.GetAssetPath(material)));
        }
        [Test]
        public void Stage14_ConfigPreviewFaultRestoresSettingsAndTransientObjects()
        {
            var root = Create(); root.AddComponent<TextMeshProUGUI>();
            root.AddComponent<ConfigPresentationBinding>().SetTokens(font: "body");
            var settings = TMP_Settings.instance; var holder = Create("Holder");
            Assert.Throws<InvalidOperationException>(() => new UIEditorPresentationPreview(root, holder.transform,
                new UIEditorPreviewOptions(), (package, address, type) => throw new InvalidOperationException("missing preview resource")));
            Assert.AreSame(settings, TMP_Settings.instance); Assert.AreEqual(0, holder.transform.childCount);
        }
        private sealed class DisposeCallback : IDisposable
        {
            private readonly Action _action;
            internal DisposeCallback(Action action) { _action = action; }
            public void Dispose() => _action();
        }
        [Test]
        public void Stage14_ConfigPreviewCleanupFailureStillReleasesEveryOwnedSurface()
        {
            var root = Create(); root.AddComponent<TextMeshProUGUI>();
            var settings = TMP_Settings.instance; var holder = Create("Holder");
            var preview = new UIEditorPresentationPreview(root, holder.transform, new UIEditorPreviewOptions(), null);
            var callbacks = (List<IDisposable>)typeof(UIEditorPresentationPreview).GetField("_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
            var released = 0;
            callbacks.Add(new DisposeCallback(() => released++));
            callbacks.Add(new DisposeCallback(() => throw new InvalidOperationException("injected preview cleanup fault")));
            var failure = Assert.Throws<AggregateException>(preview.Dispose);
            StringAssert.Contains("injected preview cleanup fault", failure.ToString());
            Assert.AreEqual(1, released); Assert.AreEqual(0, holder.transform.childCount);
            Assert.AreSame(settings, TMP_Settings.instance); Assert.IsNull(preview.Clone);
            Assert.DoesNotThrow(preview.Dispose);
        }
        [Test, Explicit("Official AssetBundle build recompiles Player scripts; run this exact test in its own Unity process.")]
        public void Stage14_OfficialLegacyBuilderCreatesRealReportVersionAndBundleAndPreservesHistory()
        {
            var setting = Collector();
            _profile.Collector = setting; _profile.Packages = new[] { "TestPackage" }; _profile.BuildPackage = "TestPackage";
            _profile.Target = EditorUserBuildSettings.activeBuildTarget;
            _profile.PackageVersion = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            _profile.Extras = new[] { AssetDatabase.LoadMainAssetAtPath(Root + "/data.txt") };
            _profile.OutputRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "stage14-real-bundle-evidence"));
            var field = typeof(BundleCollectorSettingData).GetField("s_setting", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = field.GetValue(null);
            try
            {
                field.SetValue(null, setting);
                var canceled = new CancellationToken(true);
                Assert.AreEqual("CanceledBeforeBuild", UIYooAssetBuilder.Build(_profile, canceled).Status);
                var result = UIYooAssetBuilder.Build(_profile);
                Assert.AreEqual("Succeeded", result.Status, result.Error);
                Assert.IsTrue(result.Files.Any(p => p.EndsWith(".bundle")));
                Assert.IsTrue(result.Files.Any(p => p.EndsWith(".report", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual(result.Files.Length, result.Sha256.Length);
                Assert.AreEqual("TestPackage", result.Summary.BuildPackageName);
                Assert.AreEqual(_profile.PackageVersion, result.Summary.BuildPackageVersion);
                Assert.AreEqual(1, result.Summary.AssetFileTotalCount);
                Assert.AreEqual(1, result.Summary.AllBundleTotalCount);
                Assert.AreEqual(new FileInfo(result.Files.Single(p => p.EndsWith(".bundle"))).Length, result.Summary.AllBundleTotalSize);
                Assert.IsTrue(File.Exists(result.ReportPath));
                Assert.AreEqual(_profile.PackageVersion, File.ReadAllText(result.Files.Single(p => p.EndsWith(".version"))).Trim());
                StringAssert.Contains("TestPackage", File.ReadAllText(result.Files.Single(p => p.EndsWith(".report"))));
                var bundle = AssetBundle.LoadFromFile(result.Files.Single(p => p.EndsWith(".bundle")));
                Assert.IsNotNull(bundle);
                try { Assert.AreEqual("real asset bundle fixture", bundle.LoadAsset<TextAsset>(Root + "/data.txt").text); }
                finally { bundle.Unload(true); }
                File.WriteAllText(Path.Combine(_profile.OutputRoot, "stage14-last-build.json"), JsonUtility.ToJson(result, true));
                var history = File.ReadAllText(Path.Combine(_profile.OutputRoot, "ui-build-history.jsonl"));
                Assert.AreEqual("Failed", UIYooAssetBuilder.Build(_profile).Status);
                StringAssert.StartsWith(history, File.ReadAllText(Path.Combine(_profile.OutputRoot, "ui-build-history.jsonl")));
            }
            finally { field.SetValue(null, previous); }
        }
    }
}
