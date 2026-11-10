using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YUIFramework.Editor;

namespace YUIFramework.Tests
{
    public sealed class Stage16PackagingTests
    {
        [TestCase("relative")]
        [TestCase("project")]
        [TestCase("ancestor")]
        [TestCase("existing-file")]
        [TestCase("existing-directory")]
        [TestCase("same-report")]
        [TestCase("nested-report")]
        [TestCase("bundle-report")]
        public void Stage16_InvalidOutputsFailBeforeBuildSideEffects(string scenario)
        {
            var parent = Path.Combine(Path.GetTempPath(), "yui-output-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            var report = Path.Combine(parent, "report.json");
            var output = Path.Combine(parent, "player.apk");
            var bundles = Path.Combine(parent, "bundles");
            var marker = Path.Combine(parent, "marker");
            var settingsPath = Path.GetFullPath("ProjectSettings/ProjectSettings.asset");
            var settings = File.ReadAllBytes(settingsPath);
            var streaming = Path.Combine(Application.dataPath, "StreamingAssets");
            var streamingBefore = Directory.Exists(streaming) ?
                Directory.GetFiles(streaming, "*", SearchOption.AllDirectories) : Array.Empty<string>();
            try
            {
                File.WriteAllText(marker, "Do not replace this input.");
                var invalid = output;
                switch (scenario)
                {
                    case "relative": invalid = "relative-player.apk"; break;
                    case "project": invalid = Path.Combine(Application.dataPath, "must-not-create.apk"); break;
                    case "ancestor": invalid = Directory.GetParent(Application.dataPath).Parent.FullName; break;
                    case "existing-file": invalid = marker; break;
                    case "existing-directory": invalid = parent; break;
                    case "same-report": report = output; break;
                    case "nested-report": report = Path.Combine(output, "report.json"); break;
                    case "bundle-report": report = Path.Combine(bundles, "report.json"); break;
                }
                Assert.That(() => ReleaseCandidateTools.BuildDevelopmentSmoke(invalid, report, bundles),
                    Throws.Exception.TypeOf(scenario.StartsWith("existing-", StringComparison.Ordinal) ? typeof(IOException) : typeof(ArgumentException)));
                if (!scenario.Contains("report"))
                {
                    Assert.That(() => ReleaseCandidateTools.MobilePreflight(BuildTarget.Android, invalid),
                        Throws.Exception.TypeOf(scenario.StartsWith("existing-", StringComparison.Ordinal) ? typeof(IOException) : typeof(ArgumentException)));
                    Assert.That(() => UIBuildGate.BuildPlayer(new BuildPlayerOptions { locationPathName = invalid }, null),
                        Throws.Exception.TypeOf(scenario.StartsWith("existing-", StringComparison.Ordinal) ? typeof(IOException) : typeof(ArgumentException)));
                }
                Assert.AreEqual("Do not replace this input.", File.ReadAllText(marker));
                CollectionAssert.AreEqual(settings, File.ReadAllBytes(settingsPath));
                CollectionAssert.AreEquivalent(streamingBefore, Directory.Exists(streaming) ?
                    Directory.GetFiles(streaming, "*", SearchOption.AllDirectories) : Array.Empty<string>());
                Assert.False(Directory.Exists(bundles));
                CollectionAssert.AreEquivalent(new[] { marker }, Directory.GetFiles(parent, "*", SearchOption.AllDirectories));
            }
            finally { Directory.Delete(parent, true); }
        }

        [Test]
        public void Stage16_ExistingReportIsNeverOverwrittenAndNewParentsAreCreated()
        {
            var parent = Path.Combine(Path.GetTempPath(), "yui-report-test-" + Guid.NewGuid().ToString("N"));
            var report = Path.Combine(parent, "new", "report.json");
            try
            {
                UIExternalOutputs.WriteNewReport(report, "{\"owned\":true}");
                Assert.AreEqual("{\"owned\":true}", File.ReadAllText(report));
                Assert.Throws<IOException>(() => ReleaseCandidateTools.MobilePreflight(BuildTarget.Android, report));
                Assert.Throws<IOException>(() => ReleaseCandidateTools.BuildDevelopmentSmoke(
                    Path.Combine(parent, "player.apk"), report, Path.Combine(parent, "bundles")));
                Assert.Throws<IOException>(() => UIExternalOutputs.WriteNewReport(report, "replacement"));
                Assert.AreEqual("{\"owned\":true}", File.ReadAllText(report));
                Assert.False(Directory.Exists(Path.Combine(parent, "bundles")));
                Assert.AreEqual(Path.GetFullPath(parent), UIExternalOutputs.BundleRoot(parent));
            }
            finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
        }

        [Test]
        public void Stage16_LinkedOutputsAreRejectedWithoutTouchingTheTarget()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("This regression exercises a Windows junction.");
            var parent = Path.Combine(Path.GetTempPath(), "yui-junction-test-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(parent, "owned-target");
            var link = Path.Combine(parent, "junction");
            Directory.CreateDirectory(target);
            var marker = Path.Combine(target, "marker");
            File.WriteAllText(marker, "Untouched junction target.");
            try
            {
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Environment.GetEnvironmentVariable("ComSpec"), "/c mklink /J \"" + link + "\" \"" + target + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    Assert.True(process.WaitForExit(5000), "Junction setup did not finish.");
                    Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
                }
                Assert.Throws<ArgumentException>(() => ReleaseCandidateTools.MobilePreflight(BuildTarget.Android, Path.Combine(link, "report.json")));
                Assert.Throws<ArgumentException>(() => ReleaseCandidateTools.BuildDevelopmentSmoke(
                    Path.Combine(parent, "player.apk"), Path.Combine(parent, "report.json"), link));
                Assert.AreEqual("Untouched junction target.", File.ReadAllText(marker));
                CollectionAssert.AreEquivalent(new[] { marker }, Directory.GetFiles(target));
            }
            finally
            {
                if (Directory.Exists(link)) Directory.Delete(link);
                Directory.Delete(parent, true);
            }
        }

        [Test]
        public void Stage16_NonTmpContentDoesNotRequireOrCreateTmpEssentialResources()
        {
            var root = new UnityEngine.GameObject("Code-built bootstrap scene root");
            try { Assert.That(Localization.Editor.UIContentChecks.Inspect(root, null), Is.Empty); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test]
        public void Stage16_OfflineBuiltinOutputWithoutACatalogCannotBecomeAPlayer()
        {
            var parent = Path.Combine(Path.GetTempPath(), "yui-catalog-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            try
            {
                File.WriteAllText(Path.Combine(parent, "YUIReleaseCandidate.version"), "patch-version-is-not-a-catalog");
                Assert.Throws<UnityEditor.Build.BuildFailedException>(() => UIYooAssetBuilder.ValidateBuiltinOutput(parent));
            }
            finally { Directory.Delete(parent, true); }
        }
        [Test]
        public void Stage16_SingleSourceExportsAreReproducibleAndRejectTampering()
        {
            var parent = Path.Combine(Path.GetTempPath(), "yui-package-test-" + Guid.NewGuid().ToString("N"));
            var first = Path.Combine(parent, "first");
            var second = Path.Combine(parent, "second");
            try
            {
                UIPackageExporter.Export(first); UIPackageExporter.Export(second);
                var inventory = File.ReadAllText(Path.Combine(first, UIPackageExporter.InventoryName));
                Assert.AreEqual(inventory, File.ReadAllText(Path.Combine(second, UIPackageExporter.InventoryName)));
                var manifest = JsonConvert.DeserializeObject<UIPackageInventory>(inventory);
                Assert.That(manifest.Files.All(f => !f.Source.Contains("SuperScrollView.Local") &&
                    !f.Source.Contains("WenKaiFallback.asset") && !f.Path.Contains("node_modules")), Is.True);
                Assert.That(manifest.Files.Any(f => f.Path == "Samples~/Complete/Hotfix.Sample/link.xml"), Is.True);
                Assert.That(manifest.Files.Any(f => f.Path == "Runtime/ConfigGenerated/UISettingsMapping.cs"), Is.True);
                foreach (var file in manifest.Files)
                    CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(first, file.Path)), File.ReadAllBytes(Path.Combine(second, file.Path)));
                Assert.Throws<IOException>(() => UIPackageExporter.Export(first));
                var changed = Path.Combine(first, "Runtime", "Core", "UIManager.cs");
                File.AppendAllText(changed, "\n// changed\n");
                Assert.Throws<InvalidDataException>(() => UIPackageExporter.Verify(first));
            }
            finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
        }
    }
}
