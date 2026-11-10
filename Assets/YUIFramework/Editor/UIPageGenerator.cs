using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace YUIFramework.Editor
{
    [Serializable]
    public sealed class UIPageGenerationRequest
    {
        public string Name = "Inventory", Namespace = "Game.UI", OutputFolder = "Assets/GeneratedUI/Inventory";
        public string Profile = "game", Id = "Inventory", Address = "Inventory", Package = "";
        public GameObject Prefab;
    }
    [Serializable]
    public sealed class UIControlBinding
    {
        public string Field, Type;
        public int[] Route;
    }
    [Serializable]
    public sealed class UIPageGenerationManifest
    {
        public string Owner = "YUIFramework.Stage14.v1", Name, Namespace, SourceGuid, SourcePath, SourceHash, OutputFolder;
        public string Profile, Id, Address, Package;
        public List<UIControlBinding> Bindings = new List<UIControlBinding>();
        public Dictionary<string, string> GeneratedHashes = new Dictionary<string, string>();
    }
    public static class UIPageGenerator
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(
            ("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event " +
             "explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object " +
             "operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch " +
             "this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while record required file global").Split(' '));
        public static void ValidateIdentifier(string value)
        {
            if (value == null || !Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$") || Keywords.Contains(value))
                throw new ArgumentException("Invalid C# identifier: " + value);
        }
        public static string SafeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !folder.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Output must be an Assets subdirectory.");
            var full = Path.GetFullPath(folder);
            UIBuildGate.RejectLinkedAncestors(full);
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || folder.Split('/', '\\').Any(p => p == "..") ||
                folder.Split('/', '\\').Contains("Editor", StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Output escapes Assets or is inside an Editor-only folder.");
            // Owning a small assembly prevents dependence on Assembly-CSharp or an enclosing custom asmdef.
            for (var parent = Directory.GetParent(full); parent != null && parent.FullName.StartsWith(assets, StringComparison.OrdinalIgnoreCase); parent = parent.Parent)
                if (parent.GetFiles("*.asmdef").Length != 0 || parent.GetFiles("*.asmref").Length != 0)
                    throw new ArgumentException("Choose a standalone output folder outside existing assembly roots.");
            return full;
        }
        public static UIPageGenerationManifest Generate(UIPageGenerationRequest request, Action<int> afterWrite = null, bool importAssets = true)
        {
            ValidateIdentifier(request.Name);
            if (string.IsNullOrEmpty(request.Namespace)) throw new ArgumentException("Namespace is required.");
            foreach (var part in request.Namespace.Split('.')) ValidateIdentifier(part);
            if (!request.Prefab || !EditorUtility.IsPersistent(request.Prefab)) throw new ArgumentException("Select a saved UI prefab.");
            foreach (var value in new[] { request.Profile, request.Id, request.Address })
                if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) throw new ArgumentException("Profile, ID and address are required.");
            var folder = SafeFolder(request.OutputFolder);
            var manifestPath = Path.Combine(folder, "ui-page.generated.json");
            UIPageGenerationManifest previous = File.Exists(manifestPath) ?
                JsonConvert.DeserializeObject<UIPageGenerationManifest>(File.ReadAllText(manifestPath)) : null;
            if (previous != null && (previous.Owner != "YUIFramework.Stage14.v1" || previous.Name != request.Name ||
                previous.Namespace != request.Namespace || previous.Profile != request.Profile || previous.Id != request.Id ||
                previous.Address != request.Address || previous.Package != request.Package))
                throw new InvalidOperationException("Output identity/config key changed; migrate the designer row explicitly rather than orphan it.");
            if (previous == null && Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                throw new InvalidOperationException("Output folder contains hand-owned content; choose an empty folder.");
            if (previous == null &&
                (TypeCache.GetTypesDerivedFrom<BaseContext>().Any(t => t.FullName == request.Namespace + "." + request.Name + "Context") ||
                 TypeCache.GetTypesDerivedFrom<UIView>().Any(t => t.FullName == request.Namespace + "." + request.Name + "View") ||
                 TypeCache.GetTypesDerivedFrom<ViewModelBase>().Any(t => t.FullName == request.Namespace + "." + request.Name + "ViewModel")))
                throw new InvalidOperationException("A Context, View or ViewModel with this qualified name already exists.");
            var assembly = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(request.Namespace + "." + request.Name);
            if (!string.IsNullOrEmpty(assembly) &&
                !Path.GetFullPath(assembly).Equals(Path.Combine(folder, request.Namespace + "." + request.Name + ".asmdef"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An assembly with this generated name already exists: " + assembly);
            if (previous != null)
                foreach (var file in previous.GeneratedHashes)
                    if (Path.GetFileName(file.Key) != file.Key || !File.Exists(Path.Combine(folder, file.Key)) ||
                        Hash(File.ReadAllBytes(Path.Combine(folder, file.Key))) != file.Value)
                        throw new InvalidOperationException("Generated file was modified by hand: " + file.Key);
            var manifest = new UIPageGenerationManifest { Name = request.Name, Namespace = request.Namespace,
                SourcePath = AssetDatabase.GetAssetPath(request.Prefab), SourceGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(request.Prefab)),
                SourceHash = Hash(File.ReadAllBytes(AssetDatabase.GetAssetPath(request.Prefab))),
                OutputFolder = request.OutputFolder, Profile = request.Profile, Id = request.Id, Address = request.Address, Package = request.Package };
            foreach (var transform in request.Prefab.GetComponentsInChildren<Transform>(true))
                foreach (var component in transform.GetComponents<Component>())
                    if (component is Selectable || component is TMP_Text || component is Image)
                    {
                        var type = component.GetType();
                        if (type.Assembly != typeof(Selectable).Assembly && type.Assembly != typeof(TMP_Text).Assembly)
                            throw new ArgumentException("Custom controls require an explicit hand-owned binding: " + type.FullName);
                        var route = new List<int>();
                        for (var node = transform; node != request.Prefab.transform; node = node.parent) route.Add(node.GetSiblingIndex());
                        route.Reverse();
                        manifest.Bindings.Add(new UIControlBinding { Field = "control" + manifest.Bindings.Count,
                            Type = type.FullName, Route = route.ToArray() });
                    }
            var files = BuildCode(request, manifest);
            foreach (var file in files) manifest.GeneratedHashes.Add(file.Key, Hash(Encoding.UTF8.GetBytes(file.Value)));
            files.Add("ui-page.generated.json", JsonConvert.SerializeObject(manifest, Formatting.Indented));
            var writes = files.ToDictionary(p => Path.Combine(folder, p.Key), p => Encoding.UTF8.GetBytes(p.Value));
            var handPath = Path.Combine(folder, request.Name + "Context.cs");
            if (!File.Exists(handPath)) writes.Add(handPath, Encoding.UTF8.GetBytes(
                $"namespace {request.Namespace}\n{{\n    public partial class {request.Name}Context\n    {{\n        partial void OnGeneratedInit() {{ }}\n    }}\n}}\n"));
            var workbook = Path.GetFullPath(Path.Combine("Config", "UISettings.xlsx"));
            var workbookBase = File.ReadAllBytes(workbook);
            writes.Add(workbook, ProposeWorkbook(request, workbook));
            if (!workbookBase.SequenceEqual(File.ReadAllBytes(workbook)))
                throw new IOException("Designer workbook changed during generation; no files were published. Retry from the current workbook.");
            var before = writes.ToDictionary(p => p.Key, p => File.Exists(p.Key) ? File.ReadAllBytes(p.Key) : null);
            Directory.CreateDirectory(folder);
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                var index = 0;
                foreach (var write in writes)
                { File.WriteAllBytes(write.Key, write.Value); afterWrite?.Invoke(++index); }
            }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                foreach (var file in before)
                    try
                    {
                        if (file.Value == null) File.Delete(file.Key); else File.WriteAllBytes(file.Key, file.Value);
                    }
                    catch (Exception rollback) { failures.Add(new IOException("Could not restore " + file.Key, rollback)); }
                if (failures.Count > 1) throw new AggregateException("Generation failed; rollback was incomplete. Inspect every reported path.", failures);
                throw;
            }
            finally { AssetDatabase.AllowAutoRefresh(); }
            if (importAssets) AssetDatabase.Refresh();
            return manifest;
        }
        private static Dictionary<string, string> BuildCode(UIPageGenerationRequest request, UIPageGenerationManifest manifest)
        {
            var n = request.Name; var ns = request.Namespace;
            var header = "// <auto-generated>YUIFramework.Stage14.v1</auto-generated>\n";
            var fields = string.Join("\n", manifest.Bindings.Select(b =>
                $"        [UnityEngine.SerializeField, YUIFramework.UIRequiredReference] private {b.Type} {b.Field};\n" +
                $"        public {b.Type} {char.ToUpperInvariant(b.Field[0]) + b.Field.Substring(1)} => {b.Field};"));
            return new Dictionary<string, string>
            {
                [n + "View.g.cs"] = header + $"namespace {ns}\n{{\n    public sealed partial class {n}View : YUIFramework.UIView\n    {{\n{fields}\n    }}\n}}\n",
                [n + "ViewModel.g.cs"] = header + $"namespace {ns}\n{{\n    public partial class {n}ViewModel : YUIFramework.ViewModelBase {{ }}\n}}\n",
                [n + "Context.g.cs"] = header + $"namespace {ns}\n{{\n    public partial class {n}Context : YUIFramework.BasePageContext\n    {{\n" +
                    $"        public {n}View Controls {{ get; private set; }}\n        protected override void HandleInit()\n        {{\n" +
                    $"            Controls = ViewObject.GetComponent<{n}View>();\n            if (!Controls) throw new System.InvalidOperationException(\"Bind the generated view before opening this prefab.\");\n" +
                    $"            SetViewModel(new {n}ViewModel());\n            OnGeneratedInit();\n        }}\n        partial void OnGeneratedInit();\n    }}\n}}\n",
                [n + "Registration.g.cs"] = header + $"namespace {ns}\n{{\n    public static class {n}Registration\n    {{\n" +
                    $"        public static YUIFramework.UIConfigRegistration Map(YUIFramework.Configuration.ConfigSnapshot snapshot) =>\n" +
                    $"            YUIFramework.UIConfigRegistration.For<{n}Context>(YUIFramework.ConfigGenerated.UISettingsMapping.Convert(\n" +
                    $"                snapshot.Get(YUIFramework.ConfigGenerated.UISettings.Table).Get({JsonConvert.SerializeObject(request.Profile)}, {JsonConvert.SerializeObject(request.Id)})));\n    }}\n}}\n",
                [ns + "." + n + ".asmdef"] = JsonConvert.SerializeObject(new { name = ns + "." + n,
                    references = new[] { "YUIFramework.Runtime", "YUIFramework.Config", "YUIFramework.ConfigGenerated", "Unity.ugui", "Unity.TextMeshPro", "UniTask" } }, Formatting.Indented)
            };
        }
        private static byte[] ProposeWorkbook(UIPageGenerationRequest request, string workbook)
        {
            var start = new ProcessStartInfo("node", "\"tools\\propose-ui-page.js\" \"" + workbook + "\"")
            { WorkingDirectory = Path.GetFullPath("Config"), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Node could not start.");
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardInput.Write(JsonConvert.SerializeObject(new { request.Profile, request.Id, PrefabKey = request.Address,
                PrefabPackage = string.IsNullOrEmpty(request.Package) ? null : request.Package }));
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("Workbook proposal failed: " + stderr.GetAwaiter().GetResult());
            return Convert.FromBase64String(output);
        }
        public static GameObject BindCompiledView(string manifestPath)
        {
            var manifest = JsonConvert.DeserializeObject<UIPageGenerationManifest>(File.ReadAllText(manifestPath));
            if (manifest.Owner != "YUIFramework.Stage14.v1") throw new InvalidOperationException("Not a UI generation manifest.");
            SafeFolder(manifest.OutputFolder);
            var sourcePath = AssetDatabase.GUIDToAssetPath(manifest.SourceGuid);
            if (sourcePath != manifest.SourcePath || Hash(File.ReadAllBytes(sourcePath)) != manifest.SourceHash)
                throw new InvalidOperationException("Source prefab moved or changed; regenerate before binding.");
            var type = Type.GetType($"{manifest.Namespace}.{manifest.Name}View, {manifest.Namespace}.{manifest.Name}", true);
            var output = manifest.OutputFolder.Replace('\\', '/') + "/" + manifest.Name + ".prefab";
            if (File.Exists(output)) throw new InvalidOperationException("Generated prefab already exists. Delete it explicitly before rebinding; user prefab edits are not overwritten.");
            var temporary = manifest.OutputFolder.Replace('\\', '/') + "/__YUIBinding_" + Guid.NewGuid().ToString("N") + ".prefab";
            var clone = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                foreach (var bridge in clone.GetComponents<UIView>())
                    if (bridge.GetType() == typeof(UIView))
                    {
                        foreach (var component in clone.GetComponentsInChildren<Component>(true))
                        {
                            if (!component || component == bridge) continue;
                            using var references = new SerializedObject(component);
                            var property = references.GetIterator();
                            while (property.Next(true))
                                if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                    property.objectReferenceValue == bridge)
                                    throw new InvalidOperationException("The source UIView has serialized users; migrate those references explicitly before replacing it.");
                        }
                        UnityEngine.Object.DestroyImmediate(bridge);
                    }
                    else if (bridge.GetType() != type) throw new InvalidOperationException("Source prefab has a hand-owned typed UIView.");
                var view = clone.GetComponent(type) ?? clone.AddComponent(type);
                using var serialized = new SerializedObject(view);
                foreach (var binding in manifest.Bindings)
                {
                    var target = clone.transform;
                    foreach (var index in binding.Route) target = target.GetChild(index);
                    var component = target.GetComponent(binding.Type);
                    if (!component) throw new InvalidOperationException("Prefab structure changed; regenerate bindings.");
                    serialized.FindProperty(binding.Field).objectReferenceValue = component;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var saved = PrefabUtility.SaveAsPrefabAsset(clone, temporary, out var success);
                if (!success || !saved) throw new IOException("Unity could not save the bound prefab.");
                var moveError = AssetDatabase.MoveAsset(temporary, output);
                if (!string.IsNullOrEmpty(moveError)) throw new IOException(moveError);
                return AssetDatabase.LoadAssetAtPath<GameObject>(output) ??
                    throw new IOException("The saved bound prefab could not be imported: " + output);
            }
            finally
            {
                try { PrefabUtility.UnloadPrefabContents(clone); }
                finally { if (File.Exists(temporary)) AssetDatabase.DeleteAsset(temporary); }
            }
        }
        public static void GenerateBatch()
        {
            var path = UIBuildGate.Argument("-yuiRequest") ?? throw new ArgumentException("-yuiRequest JSON path required.");
            var request = JsonConvert.DeserializeObject<UIPageGenerationRequest>(File.ReadAllText(path));
            request.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UIBuildGate.Argument("-yuiPrefab"));
            Generate(request);
        }
        private static string Hash(byte[] value)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", "");
        }
    }
}
