using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using System.Xml.Linq;
using YooAsset;
using YUIFramework.Editor;

namespace YUIFramework.PackageTests
{
    public sealed class PackageBoundaryTests
    {
        [Test]
        public void RetainedRuntimeCompatibilityWarningsNameTheRemovalVersion()
        {
            var attributes = typeof(UIManager).Assembly.GetExportedTypes().SelectMany(type =>
                type.GetCustomAttributes(typeof(ObsoleteAttribute), false).Cast<ObsoleteAttribute>().Concat(
                    type.GetMembers(System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
                        .SelectMany(member => member.GetCustomAttributes(typeof(ObsoleteAttribute), false).Cast<ObsoleteAttribute>())));
            var messages = attributes.Select(attribute => attribute.Message).ToArray();
            Assert.That(messages.Length, Is.GreaterThan(20));
            Assert.That(messages.All(message => message.Contains("2.1.0")), Is.True);
        }
        [Test]
        public void EditorAssemblyIsEditorOnlyAndDoesNotRequireImportedSamples()
        {
            var script = AssetDatabase.FindAssets("UIPackageExporter t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).Single(p => p.EndsWith("/UIPackageExporter.cs", StringComparison.Ordinal));
            var definition = JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(script), "YUIFramework.Editor.asmdef")));
            CollectionAssert.AreEqual(new[] { "Editor" }, definition["includePlatforms"].Values<string>().ToArray());
            Assert.That(definition["references"].Values<string>().Any(r => r.Contains("Examples") || r.Contains("Sample")), Is.False);
            Assert.That(typeof(UIManager).Assembly.GetReferencedAssemblies().Any(a =>
                a.Name.Contains("Editor") || a.Name.Contains("Sample") || a.Name.Contains("HybridCLR")), Is.False);
        }

        [Test]
        public void InvalidExportNeverTouchesTheSourceOrAnExistingDirectory()
        {
            Assert.Throws<ArgumentException>(() => UIPackageExporter.Export(ApplicationPath()));
            Assert.Throws<ArgumentException>(() => UIPackageExporter.Export("relative-output"));
        }

        [Test]
        public void CorePackageProvidesTheActualYooAssetReflectiveConstructorRulesWithoutSampleImport()
        {
            var processor = new UIYooAssetLinker();
            var path = processor.GenerateAdditionalLinkXmlFile(null, null);
            var xml = XDocument.Load(path);
            var assembly = xml.Root.Element("assembly");
            Assert.AreEqual("YooAsset", assembly.Attribute("fullname").Value);
            var types = assembly.Elements("type").ToArray();
            CollectionAssert.AreEquivalent(new[] { "YooAsset.BuiltinFileSystem", "YooAsset.SandboxFileSystem" },
                types.Select(t => t.Attribute("fullname").Value));
            foreach (var type in types)
            {
                Assert.AreEqual("nothing", type.Attribute("preserve").Value);
                Assert.AreEqual("System.Void .ctor()", type.Element("method").Attribute("signature").Value);
                var actual = typeof(FileSystemParameters).Assembly.GetType(type.Attribute("fullname").Value, true);
                Assert.NotNull(actual.GetConstructor(Type.EmptyTypes), "YooAsset API changed; review the explicit reflection boundary.");
            }
        }

        [Test]
        public void CoreBuildAndPreflightRejectUnsafeOutputsBeforeRequiringSampleImport()
        {
            Assert.Throws<ArgumentException>(() => UIBuildGate.BuildPlayer(new BuildPlayerOptions { locationPathName = "relative" }, null));
            Assert.Throws<ArgumentException>(() => ReleaseCandidateTools.MobilePreflight(BuildTarget.Android, "relative"));
            Assert.Throws<ArgumentException>(() => ReleaseCandidateTools.BuildDevelopmentSmoke("relative", "relative", "relative"));
        }

        private static string ApplicationPath() => Path.GetFullPath("Assets");
    }
}
