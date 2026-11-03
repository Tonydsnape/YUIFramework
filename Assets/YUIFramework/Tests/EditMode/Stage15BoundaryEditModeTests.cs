using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;
using YUIFramework.CodeLoading;
using YUIFramework.Hotfix.Sample;
using System.IO;
using Newtonsoft.Json.Linq;
using YUIFramework.Editor;

namespace YUIFramework.Tests
{
    public sealed class Stage15BoundaryEditModeTests
    {
        private abstract class AbstractPage : BasePageContext { }
        private sealed class PrivateConstructorPage : BasePageContext { private PrivateConstructorPage() { } }
        private sealed class ParameterizedPage : BasePageContext { public ParameterizedPage(int value) { } }
        [Test]
        public void Stage15_PublicConstructorContractRejectsInvalidRegistrationsBeforePublication()
        {
            var config = new UIConfig { Id = "Invalid", PrefabKey = "Invalid", Layer = UILayer.Normal };
            foreach (var type in new[] { typeof(AbstractPage), typeof(PrivateConstructorPage), typeof(ParameterizedPage), typeof(string) })
                Assert.Throws<ArgumentException>(() => UIConfigRegistration.ValidateContextType(type));
            Assert.Throws<ArgumentException>(() => UIConfigRegistration.For<AbstractPage>(config));
            Assert.DoesNotThrow(() => UIConfigRegistration.ValidateContextType(typeof(HotfixSamplePage)));
        }
        [Test]
        public void Stage15_CompiledAssemblyGraphKeepsAotIndependentAndHotfixPublicOnly()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var hotfix = assemblies.Single(a => a.name == "YUIFramework.Hotfix.Sample");
            CollectionAssert.IsSubsetOf(hotfix.assemblyReferences.Select(a => a.name).ToArray(),
                new[] { "YUIFramework.Runtime", "YUIFramework.CodeLoading", "UniTask", "UnityEngine.UI" });
            Assert.IsFalse(typeof(HotfixSampleEntry).Assembly.GetReferencedAssemblies().Any(a => a.Name.Contains("Editor")));
            foreach (var name in new[] { "YUIFramework.Runtime", "YUIFramework.Bootstrap", "YUIFramework.Bootstrap.YooAsset", "YUIFramework.CodeLoading" })
                Assert.IsFalse(assemblies.Single(a => a.name == name).assemblyReferences.Any(a => a.name == hotfix.name), name);
            Assert.AreEqual(typeof(IHotfixEntry).Assembly, typeof(ResourceHotfixCodeLoader).Assembly);
        }
        [Test]
        public void Stage15_TrustedCodeManifestNeedsExactIdentityAndHash()
        {
            Assert.Throws<ArgumentException>(() => new HotfixAssemblySpec("Code", "x", "ShortName", "Entry", new string('a', 64)));
            Assert.Throws<ArgumentException>(() => new HotfixAssemblySpec("Code", "x", typeof(HotfixSampleEntry).Assembly.FullName, "Entry", "bad"));
        }
        [Test]
        public void Stage15_DraftUsesRealAssemblyDefinitionAndMetadataOwnersWithoutOverwritingReviewedOutput()
        {
            var path = Path.GetFullPath("Library/YUIFramework/Stage15-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                HotfixBoundaryTools.ExportDraftTo(path);
                var bytes = File.ReadAllBytes(path);
                var report = JObject.Parse(File.ReadAllText(path));
                Assert.AreEqual("DraftNotInstalledOrMetadataGenerated", (string)report["status"]);
                var definition = report["hotUpdateAssemblyDefinitions"][0];
                Assert.AreEqual("YUIFramework.Hotfix.Sample", (string)definition["name"]);
                Assert.AreEqual(UnityEditor.AssetDatabase.AssetPathToGUID((string)definition["assetPath"]), (string)definition["guid"]);
                CollectionAssert.Contains(report["metadataCandidates"].Select(c => (string)c["assembly"]).ToArray(),
                    typeof(System.Collections.Generic.List<>).Assembly.GetName().Name + ".dll");
                Assert.Throws<IOException>(() => HotfixBoundaryTools.ExportDraftTo(path));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
                Assert.Throws<ArgumentException>(() => HotfixBoundaryTools.ExportDraftTo("Assets/ForbiddenReport.json"));
                Assert.IsFalse(File.Exists("Assets/ForbiddenReport.json"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
