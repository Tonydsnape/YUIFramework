using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YUIFramework.Configuration;
using YUIFramework.Editor;

namespace YUIFramework.Tests
{
    public sealed class Stage14AuthoringIntegrationTests
    {
        private const string SourceFolder = "Assets/Stage14AuthoringFixture";
        private const string OutputFolder = "Assets/Stage14AuthoringGenerated";
        // Batch producer intentionally leaves its outputs in a dedicated derived
        // project, so the next Unity process compiles the actual generated code.
        public static void Prepare()
        {
            if (AssetDatabase.IsValidFolder(SourceFolder) || Directory.Exists(OutputFolder))
                throw new InvalidOperationException("Use a fresh isolated authoring project.");
            AssetDatabase.CreateFolder("Assets", "Stage14AuthoringFixture");
            var root = new GameObject("Serialized panel", typeof(RectTransform), typeof(UIView));
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    var child = new GameObject("Same", typeof(RectTransform), typeof(Image), typeof(Button));
                    child.transform.SetParent(root.transform, false);
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, SourceFolder + "/Source.prefab");
                UIPageGenerator.Generate(new UIPageGenerationRequest { Name = "Integration", Namespace = "Stage14.Generated",
                    OutputFolder = OutputFolder, Prefab = prefab, Profile = "stage14-integration", Id = "Integration",
                    Address = "Integration", Package = "Fixture" });
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void PrepareBatchProfile()
        {
            var profile = ScriptableObject.CreateInstance<UIBuildProfile>();
            profile.Policy = UIBuildPolicy.MinimalDemo; profile.CheckTextContent = false;
            profile.Target = EditorUserBuildSettings.activeBuildTarget;
            profile.Extras = new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(SourceFolder + "/Source.prefab") };
            profile.AuditedDemoResourcePaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
                p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            var path = SourceFolder + "/MinimalDemo.asset";
            if (File.Exists(path)) throw new InvalidOperationException("Do not overwrite an existing batch fixture profile.");
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
        }
        public static void Regenerate()
        {
            UIPageGenerator.Generate(new UIPageGenerationRequest { Name = "Integration", Namespace = "Stage14.Generated",
                OutputFolder = OutputFolder, Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceFolder + "/Source.prefab"),
                Profile = "stage14-integration", Id = "Integration", Address = "Integration", Package = "Fixture" });
        }
        [UnityTest, Explicit("Prepare in a dedicated project, export Config, then start a fresh Unity process to compile generated code.")]
        public IEnumerator Stage14_CompiledGeneratedContextViewModelBindingsAndConfigBatchAreRunnable() =>
            VerifyAsync().ToCoroutine();

        private static async UniTask VerifyAsync()
        {
            var previewScenes = EditorSceneManager.previewSceneCount;
            var reloaded = File.Exists(OutputFolder + "/Integration.prefab");
            var prefab = reloaded ? AssetDatabase.LoadAssetAtPath<GameObject>(OutputFolder + "/Integration.prefab") :
                UIPageGenerator.BindCompiledView(OutputFolder + "/ui-page.generated.json");
            Assert.AreEqual(previewScenes, EditorSceneManager.previewSceneCount);
            Assert.That(Directory.GetFiles(OutputFolder, "__YUIBinding_*"), Is.Empty);
            var boundBytes = File.ReadAllBytes(AssetDatabase.GetAssetPath(prefab));
            Assert.Throws<InvalidOperationException>(() => UIPageGenerator.BindCompiledView(OutputFolder + "/ui-page.generated.json"));
            CollectionAssert.AreEqual(boundBytes, File.ReadAllBytes(AssetDatabase.GetAssetPath(prefab)));
            var viewType = Type.GetType("Stage14.Generated.IntegrationView, Stage14.Generated.Integration", true);
            Assert.AreEqual(viewType, AssetDatabase.LoadAssetAtPath<MonoScript>(OutputFolder + "/IntegrationView.g.cs").GetClass());
            Assert.AreEqual(1, prefab.GetComponents<UIView>().Length);
            var view = prefab.GetComponent(viewType);
            using (var serialized = new SerializedObject(view))
                for (var i = 0; i < 4; i++) Assert.IsNotNull(serialized.FindProperty("control" + i).objectReferenceValue);
            var sourceBytes = File.ReadAllBytes(SourceFolder + "/Source.prefab");
            var configs = SampleUIConfiguration.Create(new EditorJsonConfigSource(), ConfigFormat.Json);
            var resources = new UIResourceService();
            var provider = new Provider(prefab);
            resources.Packages.Register(provider, true);
            var manager = new UIManager();
            manager.Initialize(resources);
            try
            {
                var snapshot = await configs.InitializeAsync();
                var map = Type.GetType("Stage14.Generated.IntegrationRegistration, Stage14.Generated.Integration", true)
                    .GetMethod("Map", BindingFlags.Static | BindingFlags.Public);
                var registration = (UIConfigRegistration)map.Invoke(null, new object[] { snapshot });
                manager.RegisterBatch(new[] { registration });
                var task = (UniTask<BaseContext>)typeof(Stage14AuthoringIntegrationTests).GetMethod(nameof(OpenGenerated),
                    BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(registration.ContextType).Invoke(null, new object[] { manager });
                var context = await task;
                Assert.AreEqual("IntegrationContext", context.GetType().Name);
                Assert.AreEqual("IntegrationViewModel", typeof(BaseContext).GetField("_viewModel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(context).GetType().Name);
                var controls = context.GetType().GetProperty("Controls").GetValue(context);
                Assert.IsNotNull(controls); Assert.AreSame(context, ((UIView)controls).Context);
                await manager.CloseAsync(context);
                await manager.ShutdownAsync();
                await resources.ShutdownAsync();
                Assert.AreEqual(1, provider.Releases);
                CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(SourceFolder + "/Source.prefab"));
                var projectName = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).Name;
                File.AppendAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", projectName + "-integration.jsonl")),
                    "{\"compiled\":true,\"serializedControls\":4,\"typedMap\":true,\"registerBatch\":true,\"openClose\":true,\"nativeReleases\":1,\"sourcePrefabUnchanged\":true,\"reloaded\":" +
                    (reloaded ? "true" : "false") + "}" + Environment.NewLine);
            }
            finally
            {
                await manager.ShutdownAsync(); await resources.ShutdownAsync(); await configs.ShutdownAsync();
            }
        }
        private static async UniTask<BaseContext> OpenGenerated<T>(UIManager manager) where T : BaseContext => await manager.OpenAsync<T>();
        private sealed class Provider : IUIResourceProvider
        {
            private readonly GameObject _prefab;
            internal int Releases;
            internal Provider(GameObject prefab) { _prefab = prefab; }
            public string PackageName => "Fixture";
            public UniTask<IUINativeAssetHandle> LoadAssetAsync(UIResourceKey key) => UniTask.FromResult<IUINativeAssetHandle>(new Handle(_prefab, () => Releases++));
            public UniTask ShutdownAsync() => UniTask.CompletedTask;
        }
        private sealed class Handle : IUINativeAssetHandle
        {
            private Action _release;
            public UnityEngine.Object Asset { get; private set; }
            public bool IsValid => Asset;
            internal Handle(GameObject asset, Action release) { Asset = asset; _release = release; }
            public void Release() { if (Asset) { Asset = null; var release = _release; _release = null; release(); } }
        }
    }
}
