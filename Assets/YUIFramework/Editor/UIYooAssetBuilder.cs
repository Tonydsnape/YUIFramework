using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace YUIFramework.Editor
{
    [Serializable]
    public sealed class UIYooAssetBuildRecord
    {
        public string Status, Package, Version, Target, Pipeline, Output, StartedUtc, EndedUtc, Error;
        public string[] Files, Sha256;
        public string BuiltinOutput;
        public string[] BuiltinFiles, BuiltinSha256;
        public string ReportPath;
        public ReportSummary Summary;
    }
    public static class UIYooAssetBuilder
    {
        public static UIYooAssetBuildRecord Build(UIBuildProfile profile, CancellationToken token = default) =>
            Build(profile, false, token);

        internal static UIYooAssetBuildRecord Build(UIBuildProfile profile, bool includeBuiltinFiles, CancellationToken token = default)
        {
            if (!profile) throw new ArgumentNullException(nameof(profile));
            var record = new UIYooAssetBuildRecord { Package = profile.BuildPackage, Version = profile.PackageVersion,
                Target = profile.Target.ToString(), Pipeline = profile.Pipeline, StartedUtc = DateTime.UtcNow.ToString("O") };
            string history = null;
            try
            {
                var report = UIAssetValidator.Validate(profile);
                if (report.HasErrors) throw new BuildFailedException(string.Join("\n", report.Issues.Select(e => e.ToString())));
                if (!profile.Collector || !ReferenceEquals(profile.Collector, BundleCollectorSettingData.Setting))
                    throw new BuildFailedException("Official builder uses the project's active BundleCollectorSetting; select that exact asset.");
                if (!profile.Packages.Contains(profile.BuildPackage)) throw new BuildFailedException("Build package is outside validated packages.");
                if (profile.Pipeline != nameof(LegacyBuildPipeline))
                    throw new BuildFailedException("This entry supports YooAsset 3 LegacyBuildPipeline; use official builder for other pipelines.");
                var group = BuildPipeline.GetBuildTargetGroup(profile.Target);
                if (!BuildPipeline.IsBuildTargetSupported(group, profile.Target))
                    throw new BuildFailedException("Unity build support module unavailable: " + profile.Target);
                if (EditorUserBuildSettings.activeBuildTarget != profile.Target)
                    throw new BuildFailedException("Switch the isolated project's active target explicitly before building; the tool never changes targets.");
                ValidateSegment(profile.BuildPackage); ValidateSegment(profile.PackageVersion);
                if (!Path.IsPathRooted(profile.OutputRoot)) throw new BuildFailedException("Choose an absolute external output directory.");
                var output = Path.GetFullPath(profile.OutputRoot);
                UIBuildGate.RejectLinkedAncestors(output);
                var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (output.Equals(project, StringComparison.OrdinalIgnoreCase) ||
                    output.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    project.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new BuildFailedException("Bundle output must be outside and not an ancestor of the project.");
                if (Directory.Exists(Path.Combine(output, profile.Target.ToString(), profile.BuildPackage, profile.PackageVersion)))
                    throw new BuildFailedException("Version output already exists; choose a new version. Existing release/user notes are never overwritten.");
                var builtinRoot = Path.Combine(output, "bundled", profile.Target.ToString(), profile.PackageVersion);
                if (includeBuiltinFiles)
                {
                    record.BuiltinOutput = UIExternalOutputs.NewPath(Path.Combine(builtinRoot, profile.BuildPackage));
                }
                Directory.CreateDirectory(output);
                history = Path.Combine(output, "ui-build-history.jsonl");
                token.ThrowIfCancellationRequested();
                var parameters = new LegacyBuildParameters
                {
                    BuildOutputRoot = output, BundledFileRoot = builtinRoot,
                    BuildTarget = profile.Target, PackageName = profile.BuildPackage,
                    PackageVersion = profile.PackageVersion, BuildPipeline = profile.Pipeline,
                    BuildBundleType = (int)EBundleType.AssetBundle, VerifyBuildingResult = true,
                    CompressOption = ECompressOption.LZ4,
                    BundledCopyOption = includeBuiltinFiles ? EBundledCopyOption.OnlyCopyAll : EBundledCopyOption.None,
                    UseAssetDependencyDB = false, EnableSharePackRule = true
                };
                // YooAsset 3's synchronous builder has no safe mid-task cancellation API.
                var result = new LegacyBuildPipeline().Run(parameters, true);
                record.Output = result.OutputPackageDirectory;
                if (!result.Success) throw new BuildFailedException(result.FailedTask + ": " + result.ErrorInfo + "\n" + result.ErrorStack);
                record.Status = token.IsCancellationRequested ? "SucceededCancellationTooLate" : "Succeeded";
                record.Files = Directory.GetFiles(record.Output, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
                record.Sha256 = record.Files.Select(Hash).ToArray();
                record.ReportPath = record.Files.SingleOrDefault(f => Path.GetExtension(f).Equals(".report", StringComparison.OrdinalIgnoreCase));
                if (record.ReportPath == null)
                    throw new BuildFailedException("Official pipeline returned success without an inspectable build report.");
                record.Summary = YooAsset.Editor.BuildReport.Deserialize(File.ReadAllText(record.ReportPath))?.Summary;
                if (record.Summary == null || record.Summary.BuildTarget != profile.Target ||
                    record.Summary.BuildPackageName != profile.BuildPackage || record.Summary.BuildPackageVersion != profile.PackageVersion ||
                    record.Summary.BuildPipeline != profile.Pipeline)
                    throw new BuildFailedException("Official build report is absent or does not match the requested target/package/version/pipeline.");
                if (includeBuiltinFiles)
                {
                    ValidateBuiltinOutput(record.BuiltinOutput);
                    record.BuiltinFiles = Directory.GetFiles(record.BuiltinOutput).OrderBy(f => f, StringComparer.Ordinal).ToArray();
                    record.BuiltinSha256 = record.BuiltinFiles.Select(Hash).ToArray();
                }
            }
            catch (OperationCanceledException error) { record.Status = "CanceledBeforeBuild"; record.Error = error.ToString(); }
            catch (Exception error) { record.Status = "Failed"; record.Error = error.ToString(); }
            finally
            {
                record.EndedUtc = DateTime.UtcNow.ToString("O");
                if (history != null) File.AppendAllText(history, JsonUtility.ToJson(record) + Environment.NewLine);
            }
            return record;
        }
        internal static void ValidateBuiltinOutput(string path)
        {
            if (!File.Exists(Path.Combine(path, "BuiltinCatalog.bytes")) ||
                new FileInfo(Path.Combine(path, "BuiltinCatalog.bytes")).Length == 0)
                throw new BuildFailedException("YooAsset 3 Offline files require the official nonempty BuiltinCatalog.bytes, not just versioned patch output.");
        }
        private static void ValidateSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') ||
                value == "." || value == "..") throw new BuildFailedException("Unsafe package/version path segment: " + value);
        }
        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
