using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace YUIFramework.Editor
{
    [Serializable]
    public sealed class UIPackageFile
    {
        public string Source, Path, Sha256;
    }

    [Serializable]
    public sealed class UIPackageInventory
    {
        public int Format = 1;
        public UIPackageFile[] Files;
    }

    public static class UIPackageExporter
    {
        public const string InventoryName = "source-manifest.json";
        private static string Project => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Framework => Path.Combine(Project, "Assets", "YUIFramework");

        [MenuItem("YUIFramework/Package/Export private RC package")]
        private static void ExportMenu()
        {
            var parent = EditorUtility.OpenFolderPanel("Choose external parent (creates a new package directory)", "", "");
            if (string.IsNullOrEmpty(parent)) return;
            Export(Path.Combine(parent, "com.tonydsnape.yuiframework"));
        }

        public static void ExportBatch() => Export(UIBuildGate.Argument("-yuiPackageOutput") ??
            throw new ArgumentException("-yuiPackageOutput is required."));

        public static void VerifyBatch() => Verify(UIBuildGate.Argument("-yuiPackageOutput") ??
            throw new ArgumentException("-yuiPackageOutput is required."));

        public static void Export(string destination)
        {
            var output = ValidateDestination(destination);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists: " + output);
            var files = Gather();
            var staging = output + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var file in files)
                {
                    var target = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(Path.Combine(Project, file.Source.Replace('/', Path.DirectorySeparatorChar)), target);
                    if (Hash(target) != file.Sha256) throw new IOException("Source changed during export: " + file.Source);
                }
                File.WriteAllText(Path.Combine(staging, InventoryName),
                    JsonConvert.SerializeObject(new UIPackageInventory { Files = files }, Formatting.Indented));
                Verify(staging);
                Directory.Move(staging, output);
                Debug.Log("[YUIPackage] Exported and verified " + files.Length + " files to " + output);
            }
            finally
            {
                // This exact random directory is created and owned by this transaction.
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        public static void Verify(string directory)
        {
            var root = Path.GetFullPath(directory);
            UIBuildGate.RejectLinkedAncestors(root);
            var inventory = JsonConvert.DeserializeObject<UIPackageInventory>(File.ReadAllText(Path.Combine(root, InventoryName)));
            if (inventory == null || inventory.Format != 1 || inventory.Files == null)
                throw new InvalidDataException("Unsupported package inventory.");
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { InventoryName };
            foreach (var file in inventory.Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path) || file.Path.Contains("\\") ||
                    file.Path.Split('/').Any(s => s == "" || s == "." || s == ".." ||
                        s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                    Path.IsPathRooted(file.Path) || !expected.Add(file.Path))
                    throw new InvalidDataException("Unsafe or duplicate package inventory path.");
                var path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
                UIBuildGate.RejectLinkedAncestors(Path.GetDirectoryName(path));
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                    Hash(path) != file.Sha256)
                    throw new InvalidDataException("Package content differs: " + file.Path);
            }
            var actual = FilesUnder(root)
                .Select(p => p.Substring(root.Length + 1).Replace('\\', '/')).ToArray();
            if (actual.Length != expected.Count || actual.Any(p => !expected.Contains(p)))
                throw new InvalidDataException("Package contains undeclared files.");
        }

        private static string ValidateDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new ArgumentException("Choose an absolute external output.");
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            UIBuildGate.RejectLinkedAncestors(full);
            if (Inside(full, Project) || Inside(Project, full))
                throw new ArgumentException("Output must be outside and not an ancestor of the authoring project.");
            return full;
        }

        private static bool Inside(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static UIPackageFile[] Gather()
        {
            if (!File.Exists(Path.Combine(Project, "Distribution", "package.json")))
                throw new InvalidOperationException("Export requires the authoring repository, not an installed package.");
            var files = new List<UIPackageFile>();
            void Add(string source, string destination)
            {
                var full = Path.Combine(Project, source.Replace('/', Path.DirectorySeparatorChar));
                UIBuildGate.RejectLinkedAncestors(Path.GetDirectoryName(full));
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked source is not distributable: " + source);
                files.Add(new UIPackageFile { Source = source, Path = destination, Sha256 = Hash(full) });
            }
            void Tree(string source, string destination, Func<string, bool> include = null)
            {
                var full = Path.Combine(Project, source.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full + ".meta")) Add(source + ".meta", destination + ".meta");
                void Walk(string directory)
                {
                    UIBuildGate.RejectLinkedAncestors(directory);
                    foreach (var path in Directory.GetFiles(directory).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        var relative = path.Substring(full.Length + 1).Replace('\\', '/');
                        if (include == null || include(relative)) Add(source + "/" + relative, destination + "/" + relative);
                    }
                    foreach (var child in Directory.GetDirectories(directory).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        var relative = child.Substring(full.Length + 1).Replace('\\', '/') + "/";
                        if (include == null || include(relative)) Walk(child);
                    }
                }
                Walk(full);
            }
            const string assetRoot = "Assets/YUIFramework/";
            Tree(assetRoot + "Runtime", "Runtime");
            Tree(assetRoot + "Config", "Runtime/Configuration");
            Tree(assetRoot + "Localization", "Runtime/Localization");
            Tree(assetRoot + "Examples/ConfigGenerated", "Runtime/ConfigGenerated");
            Tree(assetRoot + "Editor", "Editor");
            Tree(assetRoot + "ThirdParty", "ThirdParty");
            Tree(assetRoot + "PackageTests", "Tests");
            Tree(assetRoot + "Examples", "Samples~/Complete/Examples",
                p => p != "ConfigGenerated.meta" && !p.StartsWith("ConfigGenerated/", StringComparison.Ordinal));
            Tree(assetRoot + "Samples", "Samples~/Complete/Samples");
            Tree(assetRoot + "Hotfix.Sample", "Samples~/Complete/Hotfix.Sample");
            Tree("Distribution/SampleTests", "Samples~/Complete/Tests");
            Add(assetRoot + "Tests/PlayMode/Stage16CompleteFlowTests.cs", "Samples~/Complete/Tests/Stage16CompleteFlowTests.cs");
            Tree(assetRoot + "ConfigData", "Samples~/Complete/ConfigData");
            Tree("Assets/Resources/YUIConfig", "Samples~/Complete/Resources/YUIConfig");
            foreach (var name in new[] { "LineBreaking Leading Characters.txt", "LineBreaking Following Characters.txt" })
            {
                Add("Assets/Resources/YUIPresentation/" + name, "Samples~/Complete/Resources/YUIPresentation/" + name);
                Add("Assets/Resources/YUIPresentation/" + name + ".meta", "Samples~/Complete/Resources/YUIPresentation/" + name + ".meta");
            }
            Tree("Documentation/Y2.0", "Documentation~");
            Tree("Config", "Tools~/Config", p => p.StartsWith("src/", StringComparison.Ordinal) ||
                p.StartsWith("tools/", StringComparison.Ordinal) || p.StartsWith("test/", StringComparison.Ordinal) ||
                (!p.Contains("/") && (p.EndsWith(".xlsx", StringComparison.Ordinal) ||
                    p == "package.json" || p == "package-lock.json" || p == "README.md")));
            Add("Distribution/package.json", "package.json");
            Add("Distribution/NOTICE.md", "NOTICE.md");
            Add("Distribution/README.md", "README.md");
            Add("CHANGELOG.md", "CHANGELOG.md");
            foreach (var name in new[] { "package.json", "NOTICE.md", "README.md", "CHANGELOG.md", InventoryName })
                Add("Distribution/" + name + ".meta", name + ".meta");
            var result = files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
            if (result.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
                throw new InvalidDataException("Duplicate package paths.");
            return result;
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static IEnumerable<string> FilesUnder(string root)
        {
            UIBuildGate.RejectLinkedAncestors(root);
            foreach (var file in Directory.GetFiles(root))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked package file: " + file);
                yield return file;
            }
            foreach (var child in Directory.GetDirectories(root))
                foreach (var file in FilesUnder(child)) yield return file;
        }
    }
}
