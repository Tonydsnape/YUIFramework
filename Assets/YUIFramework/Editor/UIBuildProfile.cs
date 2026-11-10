using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;
using YUIFramework.Configuration;

namespace YUIFramework.Editor
{
    public enum UIBuildPolicy { MinimalDemo, Production }
    [Serializable]
    public sealed class UIPageEntry
    {
        public string Profile, Id, ContextType;
        public GameObject Prefab;
        public bool CodeBuiltDemo;
    }
    [Serializable]
    public sealed class UINavigationEdge
    {
        public string From, To, Action;
    }
    [Serializable]
    public sealed class UIResourceEntry
    {
        public string Package, Address;
        public UnityEngine.Object Asset;
    }
    [CreateAssetMenu(menuName = "YUIFramework/Build and validation profile")]
    public sealed class UIBuildProfile : ScriptableObject
    {
        public UIBuildPolicy Policy = UIBuildPolicy.Production;
        public SceneAsset[] Scenes = Array.Empty<SceneAsset>();
        public UnityEngine.Object[] Extras = Array.Empty<UnityEngine.Object>();
        public UIPageEntry[] Pages = Array.Empty<UIPageEntry>();
        public UINavigationEdge[] Navigation = Array.Empty<UINavigationEdge>();
        public UIResourceEntry[] RequiredResources = Array.Empty<UIResourceEntry>();
        public string[] AuditedDemoResourcePaths = Array.Empty<string>();
        public string[] DemoCodePageIds = Array.Empty<string>();
        public string UISettingsJson = "Assets/YUIFramework/ConfigData/Editor/json/UISettings.json";
        public string LocalizationJson = "Assets/YUIFramework/ConfigData/Editor/json/LocalizationTextConfig.json";
        public string PresentationJson = "Assets/YUIFramework/ConfigData/Editor/json/UIPresentationConfig.json";
        public BundleCollectorSetting Collector;
        public string[] Packages = Array.Empty<string>();
        public string DefaultPackage = "DefaultPackage";
        public string ConfigPackage = "";
        public string ConfigAddressPrefix = "", ConfigAddressSuffix = "";
        public ConfigFormat ProductionConfigFormat = ConfigFormat.MessagePack;
        public bool CheckTextContent = true;
        public int MaximumAtlasSize = 4096;
        public bool FailOnWarnings;
        public BuildTarget Target = BuildTarget.StandaloneWindows64;
        public string BuildPackage = "DefaultPackage";
        public string PackageVersion = "1.0.0";
        public string Pipeline = nameof(LegacyBuildPipeline);
        public string OutputRoot = "";
        [MenuItem("Tools/YUIFramework/Create audited minimal-demo profile")]
        private static void CreateMinimalDemo()
        {
            var path = EditorUtility.SaveFilePanelInProject("Minimal-demo build profile", "MinimalDemoUI", "asset",
                "This explicitly audits the current Resources inputs as demo, never Production.");
            if (string.IsNullOrEmpty(path)) return;
            var profile = CreateInstance<UIBuildProfile>();
            profile.Policy = UIBuildPolicy.MinimalDemo;
            profile.Target = EditorUserBuildSettings.activeBuildTarget;
            profile.Scenes = EditorBuildSettings.scenes.Where(s => s.enabled)
                .Select(s => AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path)).ToArray();
            profile.AuditedDemoResourcePaths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            AssetDatabase.CreateAsset(profile, path); Selection.activeObject = profile;
        }
    }

    [Serializable]
    public sealed class UIEditorIssue
    {
        public string Severity, Code, AssetPath, ObjectPath, Property, Message;
        public int[] SiblingRoute;
        public override string ToString() => $"{Severity} [{Code}] {AssetPath} :: {ObjectPath} :: {Property}: {Message}";
    }
    [Serializable]
    public sealed class UIEditorReport
    {
        public string Policy, Target, CreatedUtc;
        public bool Complete = true, ProductionCertified;
        public List<string> Coverage = new List<string>();
        public List<string> Skipped = new List<string>();
        public List<UIEditorIssue> Issues = new List<UIEditorIssue>();
        public List<string> Graph = new List<string>();
        public bool HasErrors => !Complete || Issues.Exists(e => e.Severity == "Error");
        public void Add(string code, string message, string path = "", UnityEngine.Object target = null,
            string property = "", bool warning = false)
        {
            var component = target as Component;
            var transform = component ? component.transform : (target as GameObject)?.transform;
            Issues.Add(new UIEditorIssue { Severity = warning ? "Warning" : "Error", Code = code,
                Message = message, AssetPath = path, Property = property,
                SiblingRoute = transform ? RouteOf(transform) : Array.Empty<int>(),
                ObjectPath = transform ? ObjectPathOf(transform) : target ? target.name : "" });
        }
        private static int[] RouteOf(Transform transform)
        {
            var route = new List<int>();
            for (; transform; transform = transform.parent) route.Add(transform.GetSiblingIndex());
            route.Reverse(); return route.ToArray();
        }
        public static string ObjectPathOf(Transform transform)
        {
            var parts = new List<string>();
            for (; transform; transform = transform.parent) parts.Add($"{transform.name}[{transform.GetSiblingIndex()}]");
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
