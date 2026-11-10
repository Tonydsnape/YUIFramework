using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using YUIFramework.CodeLoading;

namespace YUIFramework.Editor
{
    public static class HotfixBoundaryTools
    {
        private static Type SampleType() => Type.GetType(
            "YUIFramework.Hotfix.AotSample.HotfixBoundarySample, YUIFramework.Hotfix.AotSample", false) ??
            throw new InvalidOperationException("Import the Complete UI flow sample before using Hotfix sample tools.");
        [MenuItem("Tools/YUIFramework/Stage 15/Export boundary and metadata draft")]
        public static void ExportDraft()
        {
            var output = UIBuildGate.Argument("-yuiBoundaryReport");
            if (string.IsNullOrEmpty(output) && !Application.isBatchMode)
            {
                output = EditorUtility.SaveFilePanel("Export reviewed Hotfix boundary draft",
                    Path.GetFullPath("Library"), "hotfix-boundary-draft", "json");
                if (string.IsNullOrEmpty(output)) return;
            }
            ExportDraftTo(output ?? Path.GetFullPath("Library/YUIFramework/hotfix-boundary-draft.json"));
        }
        public static void ExportDraftTo(string output)
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var hotfix = assemblies.Single(a => a.name == "YUIFramework.Hotfix.Sample");
            var aotNames = new[] { "YUIFramework.Runtime", "YUIFramework.Bootstrap", "YUIFramework.Bootstrap.YooAsset",
                "YUIFramework.CodeLoading", "YUIFramework.Hotfix.AotSample" };
            foreach (var name in aotNames)
                if (assemblies.Single(a => a.name == name).assemblyReferences.Any(a => a.name == hotfix.name))
                    throw new BuildFailedException("AOT assembly depends on Hotfix business: " + name);
            if (hotfix.assemblyReferences.Any(a => a.flags.HasFlag(AssemblyFlags.EditorAssembly)))
                throw new BuildFailedException("Hotfix assembly cannot depend on Editor code.");
            var definition = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(hotfix.name);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                status = "DraftNotInstalledOrMetadataGenerated",
                unity = Application.unityVersion, target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                abstractions = "Public interfaces remain in YUIFramework.Runtime; IHotfixEntry is in the AOT CodeLoading assembly. No disruptive interface extraction.",
                aotAssemblies = aotNames.Concat(new[] { "YooAsset", "UniTask", typeof(UnityEngine.UI.Button).Assembly.GetName().Name }).ToArray(),
                hotUpdateAssemblyDefinitions = new[] { new { name = hotfix.name, assetPath = definition,
                    guid = AssetDatabase.AssetPathToGUID(definition) } },
                metadataCandidates = new[] {
                    new { assembly = typeof(ObservableProperty<>).Assembly.GetName().Name + ".dll", reason = "ObservableProperty<T>, ObservableCollection<T>, UIManager generic Open/Prewarm definitions" },
                    new { assembly = typeof(Cysharp.Threading.Tasks.UniTask).Assembly.GetName().Name + ".dll", reason = "UniTask builders, awaiters and Hotfix async state-machine generic calls" },
                    new { assembly = typeof(System.Collections.Generic.List<>).Assembly.GetName().Name + ".dll", reason = "List<T>, Dictionary<K,V>, EqualityComparer<T>, Activator and delegate definitions" }
                },
                metadataPolicy = "Save actual post-strip AOT DLLs with base-player version/BuildTarget/hash. Supplement defining assemblies, not the assembly containing a Hotfix generic argument. This is not a drop-in HybridCLR Settings asset or generated metadata.",
                instantiatedWitness = "HotfixAotWitness.ExerciseAsync: property<int/string>, collection<int/string>, UIAsyncCommand/UniTask<int>; not arbitrary future value types.",
                dependencies = hotfix.assemblyReferences.Select(a => a.name).ToArray()
            }, Newtonsoft.Json.Formatting.Indented);
            UIBuildGate.RejectLinkedAncestors(output);
            if (Path.GetFullPath(output).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Draft reports must be outside Assets.");
            if (File.Exists(output)) throw new IOException("Existing reviewed draft is never overwritten; choose another -yuiBoundaryReport path.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, json);
        }

        public static void InspectStripped()
        {
            var directory = Path.GetFullPath(UIBuildGate.Argument("-yuiStrippedDirectory") ??
                throw new ArgumentException("-yuiStrippedDirectory required."));
            var output = Path.GetFullPath(UIBuildGate.Argument("-yuiBoundaryReport") ??
                throw new ArgumentException("-yuiBoundaryReport required."));
            UIBuildGate.RejectLinkedAncestors(output);
            if (output.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
                throw new ArgumentException("Choose a new report outside Assets.");
            System.Reflection.Assembly Resolve(object sender, ResolveEventArgs args)
            {
                var path = Path.Combine(directory, new System.Reflection.AssemblyName(args.Name).Name + ".dll");
                return File.Exists(path) ? System.Reflection.Assembly.ReflectionOnlyLoadFrom(path) :
                    System.Reflection.Assembly.ReflectionOnlyLoad(args.Name);
            }
            ResolveEventHandler resolver = Resolve;
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolver;
            try
            {
                var samplePath = Path.Combine(directory, "YUIFramework.Hotfix.Sample.dll");
                var runtimePath = Path.Combine(directory, "YUIFramework.Runtime.dll");
                var extensionPath = Path.Combine(directory, "YUIFramework.CodeLoading.dll");
                var sample = System.Reflection.Assembly.ReflectionOnlyLoadFrom(samplePath);
                var runtime = System.Reflection.Assembly.ReflectionOnlyLoadFrom(runtimePath);
                var extension = System.Reflection.Assembly.ReflectionOnlyLoadFrom(extensionPath);
                var entry = sample.GetType("YUIFramework.Hotfix.Sample.HotfixSampleEntry", true);
                var page = sample.GetType("YUIFramework.Hotfix.Sample.HotfixSamplePage", true);
                var manager = runtime.GetType("YUIFramework.UIManager", true);
                var methods = manager.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (entry.GetConstructor(Type.EmptyTypes) == null || page.GetConstructor(Type.EmptyTypes) == null ||
                    entry.GetMethod("EnterAsync") == null ||
                    !methods.Any(m => m.Name == "OpenNewAsync") || !methods.Any(m => m.Name == "CreatePrewarmedContextAsync") ||
                    extension.GetType("YUIFramework.CodeLoading.HotfixAotWitness", true).GetMethod("ExerciseAsync") == null)
                    throw new BuildFailedException("Required reflected/public paths were stripped.");
                using var sha = System.Security.Cryptography.SHA256.Create();
                var files = new[] { samplePath, runtimePath, extensionPath }.Select(path => new
                { path, sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() }).ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    result = "PassedStrippedMemberInspection", files,
                    reflectedConstructors = new[] { entry.FullName, page.FullName },
                    managerPaths = new[] { "OpenNewAsync<T>", "CreatePrewarmedContextAsync(Type)" },
                    limitation = "Inspects actual UnityLinker output. Does not execute IL2CPP code or prove unknown generic instantiations."
                }, Newtonsoft.Json.Formatting.Indented));
            }
            finally { AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolver; }
        }

        [MenuItem("Tools/YUIFramework/Stage 15/Create missing boundary sample scene")]
        public static void CreateSampleScene()
        {
            var path = "Assets/HotfixBoundarySample.unity";
            if (File.Exists(path)) throw new InvalidOperationException("Existing sample scene is never overwritten.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("AOT Hotfix boundary sample", SampleType());
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save sample scene.");
        }

        // Only use in an isolated validation project; settings are always restored.
        public static void BuildStrippingProbe()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run the stripping probe in an isolated batchmode project.");
            var output = UIBuildGate.Argument("-yuiPlayerOutput") ?? throw new ArgumentException("-yuiPlayerOutput required.");
            var resultPath = UIBuildGate.Argument("-yuiBuildResult") ?? throw new ArgumentException("-yuiBuildResult required.");
            foreach (var path in new[] { output, resultPath })
            {
                UIBuildGate.RejectLinkedAncestors(path);
                if (Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Player/reports must be outside Assets.");
                if (File.Exists(path)) throw new IOException("Probe output already exists: " + path);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            }
            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(resultPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Player and report outputs must be distinct.");
            var targetName = UIBuildGate.Argument("-yuiTarget") ?? nameof(BuildTarget.StandaloneWindows64);
            if (!Enum.TryParse<BuildTarget>(targetName, out var target) ||
                (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.Android))
                throw new ArgumentException("Probe supports only StandaloneWindows64 and Android.");
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var backend = PlayerSettings.GetScriptingBackend(group);
            var stripping = PlayerSettings.GetManagedStrippingLevel(group);
            var architecture = PlayerSettings.Android.targetArchitectures;
            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var originalSettings = EditorJsonUtility.ToJson(settings);
            var settingsPath = Path.GetFullPath("ProjectSettings/ProjectSettings.asset");
            var originalSettingsBytes = File.ReadAllBytes(settingsPath);
            var selected = UIBuildGate.Argument("-yuiBackend") == "Mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP;
            var scenePath = "Assets/__YUIHotfixBoundaryProbe-" + Guid.NewGuid().ToString("N") + ".unity";
            var scene = default(UnityEngine.SceneManagement.Scene);
            UIBuildProfile profile = null;
            var started = DateTime.UtcNow;
            try
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var host = new GameObject("AOT Hotfix boundary sample", SampleType());
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new IOException("Could not save probe scene.");
                profile = ScriptableObject.CreateInstance<UIBuildProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
                profile.Policy = UIBuildPolicy.MinimalDemo; profile.Target = target;
                var jsonRoot = YUIFramework.Configuration.EditorJsonConfigSource.FindSampleRoot();
                profile.UISettingsJson = jsonRoot + "UISettings.json";
                profile.LocalizationJson = jsonRoot + "LocalizationTextConfig.json";
                profile.PresentationJson = jsonRoot + "UIPresentationConfig.json";
                profile.Scenes = new[] { AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) };
                profile.CheckTextContent = false;
                profile.AuditedDemoResourcePaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
                    p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
                PlayerSettings.SetScriptingBackend(group, selected);
                PlayerSettings.SetManagedStrippingLevel(group, ManagedStrippingLevel.High);
                if (target == BuildTarget.Android) PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                var report = UIBuildGate.BuildPlayer(new BuildPlayerOptions { target = profile.Target,
                    scenes = profile.Scenes.Select(AssetDatabase.GetAssetPath).ToArray(), locationPathName = output,
                    options = BuildOptions.Development }, profile);
                File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    backend = selected.ToString(), target = target.ToString(), stripping = "High", result = report.summary.result.ToString(),
                    startedUtc = started, endedUtc = DateTime.UtcNow,
                    errors = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                        .Select(m => m.content).ToArray(),
                    size = report.summary.totalSize, seconds = report.summary.totalTime.TotalSeconds,
                    options = "Development", dynamicHotfixLoading = "NotSupportedByStockIL2CPP",
                    deviceCertification = "NotValidated"
                }, Newtonsoft.Json.Formatting.Indented));
                if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Stripping probe failed: " + report.summary.result);
            }
            catch (Exception error)
            {
                if (!File.Exists(resultPath))
                    File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new
                    { backend = selected.ToString(), target = target.ToString(), stripping = "High", result = "FailedBeforeReport",
                        startedUtc = started, endedUtc = DateTime.UtcNow, error = error.ToString() },
                        Newtonsoft.Json.Formatting.Indented));
                throw;
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(group, backend);
                PlayerSettings.SetManagedStrippingLevel(group, stripping);
                PlayerSettings.Android.targetArchitectures = architecture;
                // Public setters materialize previously absent per-platform defaults.
                // Restore their serialized absence too, not just equivalent values.
                EditorJsonUtility.FromJsonOverwrite(originalSettings, settings);
                AssetDatabase.SaveAssets();
                if (profile) UnityEngine.Object.DestroyImmediate(profile);
                if (scene.IsValid()) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (File.Exists(scenePath)) AssetDatabase.DeleteAsset(scenePath);
                // Unity serializes LF even when the user's input file used CRLF.
                File.WriteAllBytes(settingsPath, originalSettingsBytes);
            }
        }
    }
}
