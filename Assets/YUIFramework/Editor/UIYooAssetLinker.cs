using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace YUIFramework.Editor
{
    public sealed class UIYooAssetLinker : IUnityLinkerProcessor
    {
        public int callbackOrder => 0;
        internal const string Rules = @"<linker>
  <assembly fullname=""YooAsset"" ignoreIfUnreferenced=""1"">
    <type fullname=""YooAsset.BuiltinFileSystem"" preserve=""nothing"">
      <method signature=""System.Void .ctor()"" />
    </type>
    <type fullname=""YooAsset.SandboxFileSystem"" preserve=""nothing"">
      <method signature=""System.Void .ctor()"" />
    </type>
  </assembly>
</linker>
";

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            // Package-internal link.xml is not an application linker root in Unity 2022.3.
            var path = Path.GetFullPath("Library/YUIFramework/Linker/YooAsset.link.xml");
            UIBuildGate.RejectLinkedAncestors(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Rules);
            return path;
        }

        public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data) { }
        public void OnAfterRun(BuildReport report, UnityLinkerBuildPipelineData data) { }
    }
}
