using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[assembly: InternalsVisibleTo("YUIFramework.Tests.EditMode")]
[assembly: InternalsVisibleTo("YUIFramework.PackageTests.Editor")]

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
            options.locationPathName = UIExternalOutputs.NewPath(options.locationPathName);
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
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(directory.FullName); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Choose a literal output path without symlinks or junctions: " + directory.FullName);
            }
        }
    }

    internal static class UIExternalOutputs
    {
        internal static string NewPath(string path)
        {
            var full = ExternalPath(path);
            if (File.Exists(full) || Directory.Exists(full))
                throw new IOException("Output already exists; choose a new path: " + full);
            return full;
        }

        internal static string BundleRoot(string path)
        {
            var full = ExternalPath(path);
            if (File.Exists(full)) throw new IOException("Bundle root is a file: " + full);
            // Existing roots retain YooAsset's append-only version/history policy.
            return full;
        }

        private static string ExternalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("Choose an absolute external output path.");
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetPathRoot(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar),
                Path.GetPathRoot(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Drive-relative output paths are not allowed.");
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (Inside(full, project) || Inside(project, full))
                throw new ArgumentException("Output must be outside and not an ancestor of the project.");
            UIBuildGate.RejectLinkedAncestors(full);
            for (var parent = Directory.GetParent(full); parent != null; parent = parent.Parent)
                if (File.Exists(parent.FullName))
                    throw new IOException("Output parent is a file: " + parent.FullName);
            if (Path.DirectorySeparatorChar == '\\' && full.Substring(Path.GetPathRoot(full).Length)
                .Split('\\').Any(segment => segment.EndsWith(".", StringComparison.Ordinal) ||
                    segment.EndsWith(" ", StringComparison.Ordinal) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new ArgumentException("Output contains an ambiguous or invalid Windows path segment.");
            return full;
        }

        internal static void RequireDisjoint(params string[] paths)
        {
            for (var i = 0; i < paths.Length; ++i)
                for (var j = i + 1; j < paths.Length; ++j)
                    if (Inside(paths[i], paths[j]) || Inside(paths[j], paths[i]))
                        throw new ArgumentException("Player, report and bundle outputs must be distinct and non-nested.");
        }

        private static bool Inside(string path, string parent) =>
            path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        internal static void WriteNewReport(string path, string contents)
        {
            var full = NewPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(contents);
        }
    }
}
