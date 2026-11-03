using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YUIFramework.Editor
{
    public sealed class UIBuildGate : IPreprocessBuildWithReport
    {
        private static UIBuildProfile _buildingProfile;
        private static string[] _buildingScenes;
        private static string PreferenceKey => "YUIFramework.BuildProfile." + Application.dataPath;
        public static string ActiveProfilePath
        {
            get => EditorPrefs.GetString(PreferenceKey, "");
            set => EditorPrefs.SetString(PreferenceKey, value);
        }
        public int callbackOrder => -10000;
        public void OnPreprocessBuild(BuildReport report)
        {
            var profile = _buildingProfile ? _buildingProfile : LoadSelected();
            if (profile && profile.Target != report.summary.platform)
                throw new BuildFailedException("UI profile target does not match the actual Player build target.");
            if (profile && profile.Policy == UIBuildPolicy.Production && _buildingScenes == null)
                throw new BuildFailedException("Production builds must use UIBuildGate.BuildPlayer(options, profile) so actual BuildPlayerOptions.scenes are audited.");
            ValidateForBuild(profile, _buildingScenes ?? EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray());
        }
        public static BuildReport BuildPlayer(BuildPlayerOptions options, UIBuildProfile profile)
        {
            if (_buildingScenes != null) throw new InvalidOperationException("A UI Player build is already in progress.");
            if (!profile || options.target != profile.Target) throw new BuildFailedException("Explicit matching target profile is required.");
            ValidateForBuild(profile, options.scenes);
            _buildingProfile = profile; _buildingScenes = (string[])options.scenes.Clone();
            try { return BuildPipeline.BuildPlayer(options); }
            finally { _buildingProfile = null; _buildingScenes = null; }
        }
        public static UIEditorReport ValidateForBuild(UIBuildProfile profile, string[] actualScenes)
        {
            var report = UIAssetValidator.Validate(profile);
            if (profile)
                foreach (var scene in actualScenes)
                    if (!profile.Scenes.Any(s => s && AssetDatabase.GetAssetPath(s) == scene))
                        report.Add("UnscannedBuildScene", "Actual enabled build scene is not declared by this profile.", scene);
            WriteReport(report, Argument("-yuiReport") ?? Path.Combine("Library", "YUIFramework", "last-validation.json"));
            if (report.HasErrors) throw new BuildFailedException(
                "YUI validation blocked build:\n" + string.Join("\n", report.Issues.Select(i => i.ToString())));
            return report;
        }
        public static UIBuildProfile LoadSelected()
        {
            var path = Argument("-yuiProfile") ?? ActiveProfilePath;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<UIBuildProfile>(path);
        }
        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            return index < 0 ? null : index + 1 < args.Length ? args[index + 1] :
                throw new ArgumentException("Missing value for " + name);
        }
        public static void ValidateBatch()
        {
            var profile = LoadSelected();
            ValidateForBuild(profile, profile ? profile.Scenes.Where(s => s).Select(AssetDatabase.GetAssetPath).ToArray() : Array.Empty<string>());
        }
        public static void BuildBundlesBatch()
        {
            var result = UIYooAssetBuilder.Build(LoadSelected());
            if (result.Status != "Succeeded") throw new BuildFailedException(result.Error);
        }
        public static void BuildPlayerBatch()
        {
            var profile = LoadSelected();
            if (!profile) throw new BuildFailedException("Explicit -yuiProfile required.");
            var output = Argument("-yuiPlayerOutput") ?? throw new ArgumentException("-yuiPlayerOutput required.");
            var report = BuildPlayer(new BuildPlayerOptions { target = profile.Target,
                scenes = profile.Scenes.Select(AssetDatabase.GetAssetPath).ToArray(), locationPathName = output }, profile);
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
        }
        public static void WriteReport(UIEditorReport report, string path)
        {
            var full = Path.GetFullPath(path);
            RejectLinkedAncestors(full);
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (full.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reports must be outside Assets, not imported release content.");
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, JsonUtility.ToJson(report, true));
        }
        internal static void RejectLinkedAncestors(string path)
        {
            for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Choose a literal output path without symlinks or junctions: " + directory.FullName);
        }
    }
}
