using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using YooAsset.Editor;
using YUIFramework.Configuration;

namespace YUIFramework.Editor
{
    public static class ReleaseCandidateTools
    {
        private const string Root = "Assets/YUIReleaseCandidate";
        private const string Package = "YUIReleaseCandidate";
        private const string ScenePath = Root + "/CompleteUI.unity";
        private const string ProfilePath = Root + "/BuildProfile.asset";
        private static Type SampleType => Type.GetType("YUIFramework.ReleaseCandidateSample, YUIFramework.Examples", false) ??
            throw new InvalidOperationException("Import the Complete UI flow sample in Package Manager first.");

        [MenuItem("YUIFramework/Release candidate/Create new demo workspace")]
        public static void CreateWorkspace()
        {
            var sampleType = SampleType;
            if (Directory.Exists(Root) || File.Exists(Root + ".meta"))
                throw new IOException("Demo workspace already exists; it is never overwritten.");
            if (BundleCollectorSettingData.HasSettingAsset())
                throw new InvalidOperationException("Automatic demo setup requires a clean consumer without a collector. In an existing application, configure its collector explicitly.");
            if (EditorSceneManager.GetSceneManagerSetup().Any(s => s.isLoaded) &&
                Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                    .Any(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Save open scenes before creating the demo workspace.");
            var json = EditorJsonConfigSource.FindSampleRoot();
            var original = EditorSceneManager.GetSceneManagerSetup();
            string collectorPath = null;
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(Root + "/RcMarker.txt", "YUIFramework RC resource ready\n");
                AssetDatabase.Refresh();
                var setting = BundleCollectorSettingData.Setting;
                collectorPath = AssetDatabase.GetAssetPath(setting);
                setting.Packages.Add(new BundleCollectorPackage
                {
                    PackageName = Package, EnableAddressable = true,
                    Groups = new List<BundleCollectorGroup>
                    {
                        new BundleCollectorGroup { GroupName = "RC", Collectors = new List<BundleCollector>
                        {
                            new BundleCollector { CollectPath = Root + "/RcMarker.txt", PackRuleName = nameof(PackSeparately),
                                AddressRuleName = nameof(AddressByFileName) }
                        }}
                    }
                });
                BundleCollectorSettingData.SaveFile();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Complete UI flow", sampleType);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save complete sample scene.");
                var profile = ScriptableObject.CreateInstance<UIBuildProfile>();
                profile.Policy = UIBuildPolicy.MinimalDemo; profile.Target = EditorUserBuildSettings.activeBuildTarget;
                profile.Scenes = new[] { AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) };
                profile.Collector = setting; profile.Packages = new[] { Package };
                profile.DefaultPackage = Package; profile.BuildPackage = Package;
                profile.UISettingsJson = json + "UISettings.json";
                profile.LocalizationJson = json + "LocalizationTextConfig.json";
                profile.PresentationJson = json + "UIPresentationConfig.json";
                profile.AuditedDemoResourcePaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                    p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
                profile.RequiredResources = new[] { new UIResourceEntry { Package = Package, Address = "RcMarker",
                    Asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/RcMarker.txt") } };
                // All shipped table rows remain explicit; the complete flow replaces only its five demo Context mappings.
                var mappings = new Dictionary<string, string>
                {
                    ["bootstrap/HelloPage"] = "SampleHelloPage", ["hello/HelloPage"] = "RcHomePage",
                    ["hello/SecondSamplePage"] = "RcDetailPage", ["hello/VirtualListSamplePage"] = "RcListPage",
                    ["hello/MvvmSamplePage"] = "MvvmSamplePage", ["y2/SampleHelloPage"] = "RcSceneContext",
                    ["system/SystemUISamplePage"] = "SystemUISamplePage", ["system/SystemLoading"] = "SampleLoadingContext",
                    ["system/SystemToast"] = "SampleToastContext", ["system/SystemConfirmation"] = "RcPopup",
                    ["system/SystemNotice"] = "SampleSystemContext"
                };
                profile.Pages = mappings.Select(pair =>
                {
                    var parts = pair.Key.Split('/');
                    return new UIPageEntry { Profile = parts[0], Id = parts[1], CodeBuiltDemo = true,
                        ContextType = "YUIFramework." + pair.Value + ", YUIFramework.Examples" };
                }).ToArray();
                profile.DemoCodePageIds = profile.Pages.Select(p => p.Id).Distinct().ToArray();
                profile.Navigation = new[]
                {
                    new UINavigationEdge { From = "HelloPage", To = "SecondSamplePage", Action = "Push / Back" },
                    new UINavigationEdge { From = "HelloPage", To = "VirtualListSamplePage", Action = "Push / Back" },
                    new UINavigationEdge { From = "HelloPage", To = "SystemConfirmation", Action = "Open / Close modal" }
                };
                AssetDatabase.CreateAsset(profile, ProfilePath); AssetDatabase.SaveAssets();
                UIBuildGate.ValidateForBuild(profile, new[] { ScenePath });
                Selection.activeObject = profile;
                Debug.Log("[YUI-RC] Demo workspace created. Open " + ScenePath + " and Play (EditorSimulate). This is not a Production profile.");
            }
            catch
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
                if (collectorPath != null) AssetDatabase.DeleteAsset(collectorPath);
                throw;
            }
            finally { if (original.Length != 0) EditorSceneManager.RestoreSceneManagerSetup(original); }
        }

        [MenuItem("YUIFramework/Release candidate/Copy Config authoring tools into project")]
        public static void InstallConfigTools()
        {
            var definition = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReleaseCandidateTools).Assembly);
            if (definition == null) throw new InvalidOperationException("Use the existing Config directory in the authoring repository.");
            var source = Path.Combine(definition.resolvedPath, "Tools~", "Config");
            var output = Path.GetFullPath("Config");
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Config already exists; never overwrite hand-owned authoring inputs.");
            var staging = output + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(staging, file.Substring(source.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
                }
                Directory.Move(staging, output);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

        public static void BuildDevelopmentSmoke()
        {
            var output = UIBuildGate.Argument("-yuiPlayerOutput") ?? throw new ArgumentException("-yuiPlayerOutput required.");
            var evidence = UIBuildGate.Argument("-yuiRcReport") ?? throw new ArgumentException("-yuiRcReport required.");
            var bundles = UIBuildGate.Argument("-yuiBundleOutput") ?? throw new ArgumentException("-yuiBundleOutput required.");
            BuildDevelopmentSmoke(output, evidence, bundles);
        }

        public static void BuildDevelopmentSmoke(string output, string evidence, string bundles)
        {
            output = UIExternalOutputs.NewPath(output);
            evidence = UIExternalOutputs.NewPath(evidence);
            bundles = UIExternalOutputs.BundleRoot(bundles);
            UIExternalOutputs.RequireDisjoint(output, evidence, bundles);
            var builtin = Path.Combine("Assets", "StreamingAssets", "yoo", Package);
            UIBuildGate.RejectLinkedAncestors(builtin);
            if (Directory.Exists(builtin) || File.Exists(builtin))
                throw new IOException("Relocate your previous RC-only builtin bundle directory explicitly before rebuilding; it is never overwritten.");
            var sourceProfile = AssetDatabase.LoadAssetAtPath<UIBuildProfile>(ProfilePath);
            if (!sourceProfile) throw new BuildFailedException("Create the explicit RC demo workspace first.");
            var target = EditorUserBuildSettings.activeBuildTarget;
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (target != BuildTarget.Android && target != BuildTarget.iOS && target != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("RC smoke supports Android, iOS and Windows standalone only.");
            var settingsPath = Path.GetFullPath("ProjectSettings/ProjectSettings.asset");
            var settingsBytes = File.ReadAllBytes(settingsPath);
            var originalSettings = EditorJsonUtility.ToJson(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var profile = UnityEngine.Object.Instantiate(sourceProfile);
            // The official bundle pipeline unloads unused assets, including managed-only profile references.
            profile.hideFlags = HideFlags.HideAndDontSave;
            var sceneBytes = File.ReadAllBytes(ScenePath);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var started = DateTime.UtcNow;
            Exception failure = null;
            try
            {
                profile.Target = target; profile.OutputRoot = bundles; profile.PackageVersion = "rc-" + Guid.NewGuid().ToString("N");
                var assetBuild = UIYooAssetBuilder.Build(profile, true);
                if (assetBuild.Status != "Succeeded") throw new BuildFailedException(assetBuild.Error);
                Directory.CreateDirectory(builtin);
                foreach (var file in assetBuild.BuiltinFiles)
                    File.Copy(file, Path.Combine(builtin, Path.GetFileName(file)));
                AssetDatabase.Refresh();
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var sample = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren(SampleType, true)).Single();
                SampleType.GetMethod("ConfigureSmoke").Invoke(sample, new object[] { true });
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                PlayerSettings.SetScriptingBackend(group, target == BuildTarget.StandaloneWindows64 && UIBuildGate.Argument("-yuiBackend") == "Mono"
                    ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
                PlayerSettings.SetManagedStrippingLevel(group, ManagedStrippingLevel.High);
                PlayerSettings.SetApplicationIdentifier(group, "com.yuiframework.rc.stage16");
                PlayerSettings.companyName = "YUIFramework";
                PlayerSettings.productName = "YUI RC Stage16 Smoke";
                if (target == BuildTarget.Android) PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                var report = UIBuildGate.BuildPlayer(new BuildPlayerOptions { target = target, scenes = new[] { ScenePath },
                    locationPathName = output, options = BuildOptions.Development }, profile);
                UIExternalOutputs.WriteNewReport(evidence, JsonConvert.SerializeObject(new
                {
                    target = target.ToString(), backend = PlayerSettings.GetScriptingBackend(group).ToString(),
                    stripping = "High", options = "Development", policy = "MinimalDemo",
                    result = report.summary.result.ToString(), startedUtc = started, endedUtc = DateTime.UtcNow,
                    seconds = report.summary.totalTime.TotalSeconds, size = report.summary.totalSize,
                    errors = report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content),
                    bundle = assetBuild, deviceSmoke = "Requires independent Player execution evidence"
                }, Formatting.Indented));
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
            }

            catch (Exception error)
            {
                failure = error;
                if (!File.Exists(evidence)) UIExternalOutputs.WriteNewReport(evidence, JsonConvert.SerializeObject(new
                { result = "FailedBeforeReport", startedUtc = started, endedUtc = DateTime.UtcNow, target = target.ToString(), error = error.ToString() }, Formatting.Indented));
                throw;
            }
            finally
            {
                var errors = new List<Exception>();
                void Restore(Action action)
                {
                    try { action(); }
                    catch (Exception error) { errors.Add(error); }
                }
                Restore(() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single));
                Restore(() => { File.WriteAllBytes(ScenePath, sceneBytes); AssetDatabase.ImportAsset(ScenePath); });
                Restore(() => EditorJsonUtility.FromJsonOverwrite(originalSettings,
                    Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings")));
                Restore(AssetDatabase.SaveAssets);
                Restore(() => File.WriteAllBytes(settingsPath, settingsBytes));
                Restore(() => UnityEngine.Object.DestroyImmediate(profile));
                Restore(() => { if (setup.Length != 0) EditorSceneManager.RestoreSceneManagerSetup(setup); });
                if (errors.Count != 0)
                {
                    if (failure != null) errors.Insert(0, failure);
                    throw new AggregateException("RC build restoration failed; all restoration steps were attempted.", errors);
                }
            }
        }

        public static void MobilePreflight()
        {
            var value = UIBuildGate.Argument("-yuiMobileTarget") ?? throw new ArgumentException("-yuiMobileTarget Android|iOS required.");
            if (!Enum.TryParse(value, out BuildTarget target) || target != BuildTarget.Android && target != BuildTarget.iOS)
                throw new ArgumentException("Only Android and iOS are mobile RC targets.");
            var path = UIBuildGate.Argument("-yuiRcReport") ?? throw new ArgumentException("-yuiRcReport required.");
            MobilePreflight(target, path);
        }

        public static void MobilePreflight(BuildTarget target, string path)
        {
            path = UIExternalOutputs.NewPath(path);
            if (target != BuildTarget.Android && target != BuildTarget.iOS)
                throw new ArgumentException("Only Android and iOS are mobile RC targets.");
            var source = AssetDatabase.LoadAssetAtPath<UIBuildProfile>(ProfilePath);
            if (!source) throw new BuildFailedException("Create the explicit imported-sample workspace first.");
            var profile = UnityEngine.Object.Instantiate(source);
            try
            {
                profile.Target = target;
                var report = UIAssetValidator.Validate(profile);
                var module = BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target);
                if (!module) report.Add("BuildModuleUnavailable", "Unity build module unavailable: " + target);
                if (target == BuildTarget.iOS && Application.platform != RuntimePlatform.OSXEditor)
                    report.Add("iOSNativeHostUnavailable", "Native iOS development build/signing/device smoke requires a separate macOS/Xcode host; project generation is not a native build.");
                UIExternalOutputs.WriteNewReport(path, JsonConvert.SerializeObject(new { utc = DateTime.UtcNow, target = target.ToString(),
                    unity = Application.unityVersion, editorHost = Application.platform.ToString(), moduleAvailable = module,
                    status = report.HasErrors ? "Blocked" : "InputsValidatedNotBuilt", policy = "MinimalDemo",
                    nativeBuild = "NotExecutedByPreflight", deviceRun = "NotExecutedByPreflight", report }, Formatting.Indented));
                if (report.HasErrors) throw new BuildFailedException("Mobile RC preflight blocked. Inspect " + path);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
    }
}
