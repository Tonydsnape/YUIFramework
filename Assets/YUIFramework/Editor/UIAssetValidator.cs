using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;
using UnityEditor.U2D;
using YUIFramework.Configuration;
using YUIFramework.ConfigGenerated;
using YUIFramework.Localization;
using YUIFramework.Localization.Editor;
using YooAsset.Editor;

namespace YUIFramework.Editor
{
    public static class UIAssetValidator
    {
        public static string AddressKey(UIBuildProfile profile, string package, string address)
        {
            var settings = profile.Collector ? profile.Collector.Packages.FirstOrDefault(p => p.PackageName == package) : null;
            return package + ":" + (settings != null && settings.LocationToLower ? address?.ToLowerInvariant() : address);
        }
        public static UIEditorReport Validate(UIBuildProfile profile)
        {
            var report = new UIEditorReport { CreatedUtc = DateTime.UtcNow.ToString("O") };
            if (!profile) { report.Complete = false; report.Add("MissingProfile", "Explicit demo/production profile is required."); return report; }
            report.Policy = profile.Policy.ToString(); report.Target = profile.Target.ToString();
            if (profile.Scenes.Length == 0 && profile.Extras.Length == 0 && profile.Pages.Length == 0)
                report.Add("EmptyScope", "Declare actual target scenes, pages or explicit extras.");
            if (profile.Scenes.Any(s => !s) || profile.Extras.Any(s => !s))
                report.Add("MissingScanRoot", "A declared scene/extra is missing; coverage cannot silently skip it.");
            if (profile.MaximumAtlasSize < 1) report.Add("AtlasBudget", "Atlas size budget must be positive.");
            if (profile.Pages.Length == 0)
            {
                if (profile.Policy == UIBuildPolicy.Production) report.Add("MissingPageDeclarations", "Production requires explicit typed UI profile/page declarations.");
                else report.Skipped.Add("No typed Config page profile selected; scene/extras-only minimal-demo coverage.");
            }
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in profile.Scenes.Cast<UnityEngine.Object>().Concat(profile.Extras).Concat(profile.Pages.Select(p => p.Prefab)))
            {
                if (!asset) continue;
                var path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) report.Add("NonAssetRoot", "Scan roots must be persistent assets.", target: asset);
                else roots.Add(path);
            }
            foreach (var entry in profile.RequiredResources)
                if (entry.Asset) roots.Add(AssetDatabase.GetAssetPath(entry.Asset));
            var addresses = CollectAddresses(profile, report);
            InspectConfig(profile, report, roots, addresses);
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                foreach (var path in AssetDatabase.GetDependencies(root, true)) paths.Add(path);
                foreach (var dependency in AssetDatabase.GetDependencies(root, false))
                    if (dependency != root) report.Graph.Add($"resource|{root}|{dependency}");
            }
            // All Resources are implicit Player inputs, even when no scene references them.
            foreach (var resource in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) &&
                         p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p) &&
                         !p.Split('/').Contains("Editor", StringComparer.OrdinalIgnoreCase)))
            {
                if (profile.Policy == UIBuildPolicy.Production)
                    report.Add("ProductionResources", "Implicit built-in Resources input is forbidden by Production policy.", resource);
                else if (!profile.AuditedDemoResourcePaths.Contains(resource, StringComparer.Ordinal))
                    report.Add("UnauditedDemoResources", "MinimalDemo requires an exact audited path; no folder wildcard exemption.", resource);
                else report.Skipped.Add("Audited minimal-demo Resources: " + resource);
            }
            LocalizationTextCatalog catalog = null;
            try { catalog = LocalizedTextEditorCatalog.Load(profile.LocalizationJson); }
            catch (Exception error) { report.Add("LocalizationSource", error.ToString(), profile.LocalizationJson); report.Complete = false; }
            foreach (var path in paths)
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    try { InspectHierarchy(AssetDatabase.LoadAssetAtPath<GameObject>(path), path, profile, catalog, report); }
                    catch (Exception error) { report.Add("PrefabScanFailure", error.ToString(), path); report.Complete = false; }
                }
                else if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    var scene = default(UnityEngine.SceneManagement.Scene);
                    try
                    {
                        // Unity 2022.3 exposes Close/NewPreviewScene publicly but keeps
                        // opening a serialized preview scene internal. Never fall back to
                        // opening/saving the user's live scene when that API is unavailable.
                        var open = typeof(EditorSceneManager).GetMethod("OpenPreviewScene", BindingFlags.Static | BindingFlags.NonPublic,
                            null, new[] { typeof(string) }, null);
                        if (open == null) throw new NotSupportedException("Unity preview-scene loader unavailable; scene coverage is incomplete.");
                        scene = (UnityEngine.SceneManagement.Scene)open.Invoke(null, new object[] { path });
                        foreach (var root in scene.GetRootGameObjects()) InspectHierarchy(root, path, profile, catalog, report);
                    }
                    catch (Exception error) { report.Add("SceneScanFailure", error.ToString(), path); report.Complete = false; }
                    finally { if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); }
                }
                else if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) is TMP_FontAsset font)
                {
                    if (font.atlasTextures == null || font.atlasTextures.Length == 0)
                        report.Add("FontAtlas", "Font has no atlas textures.", path, font);
                    foreach (var atlas in font.atlasTextures)
                        if (!atlas || atlas.width > profile.MaximumAtlasSize || atlas.height > profile.MaximumAtlasSize)
                            report.Add("FontAtlas", "Missing atlas or configured atlas budget exceeded.", path, font);
                    if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic && !font.sourceFontFile)
                        report.Add("DynamicFontSource", "Dynamic TMP font has no source Font.", path, font);
                }
                else if (AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path) is SpriteAtlas spriteAtlas)
                {
                    var packables = spriteAtlas.GetPackables();
                    if (packables.Length == 0 || packables.Any(p => !p))
                        report.Add("SpriteAtlasSource", "Sprite atlas has empty or missing packable sources.", path, spriteAtlas);
                    if (spriteAtlas.GetPlatformSettings("DefaultTexturePlatform").maxTextureSize > profile.MaximumAtlasSize)
                        report.Add("SpriteAtlasBudget", "Sprite atlas maximum texture size exceeds the configured budget.", path, spriteAtlas);
                }
                else if (AssetDatabase.LoadAssetAtPath<Material>(path) is Material material && !material.shader)
                    report.Add("MissingShader", "Material shader is missing.", path, material);
                report.Coverage.Add(path);
            }
            var ids = new HashSet<string>(profile.Pages.Select(p => p.Id), StringComparer.Ordinal);
            foreach (var edge in profile.Navigation)
            {
                if (!ids.Contains(edge.From) || !ids.Contains(edge.To))
                    report.Add("NavigationEndpoint", "Explicit edge references undeclared page: " + edge.From + " -> " + edge.To);
                report.Graph.Add($"navigation:{edge.Action}|{edge.From}|{edge.To}");
            }
            report.Skipped.Add("Unregistered navigation/third-party dynamic loads cannot be inferred. No whole-project vendor scan.");
            if (profile.FailOnWarnings)
                foreach (var issue in report.Issues) issue.Severity = "Error";
            report.ProductionCertified = profile.Policy == UIBuildPolicy.Production && !report.HasErrors;
            return report;
        }

        public static Dictionary<string, string> CollectAddresses(UIBuildProfile profile, UIEditorReport report)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!profile.Collector || profile.Packages.Length == 0)
            {
                if (profile.Policy == UIBuildPolicy.Production)
                { report.Add("CollectorUnavailable", "Production requires explicit real YooAsset collector and packages."); report.Complete = false; }
                else report.Skipped.Add("MinimalDemo has no production collector certification.");
                return result;
            }
            if (profile.Policy == UIBuildPolicy.Production)
            {
                var settings = AssetDatabase.FindAssets("t:BundleCollectorSetting");
                if (settings.Length != 1 || AssetDatabase.GUIDToAssetPath(settings[0]) != AssetDatabase.GetAssetPath(profile.Collector))
                {
                    report.Add("CollectorSourceAmbiguous", "Production requires exactly one saved collector, matching the official builder's source.");
                    report.Complete = false;
                }
            }
            foreach (var package in profile.Packages)
            {
                try
                {
                    var collected = profile.Collector.BeginCollect(package, false, false);
                    if (!collected.Command.EnableAddressable) report.Add("AddressingDisabled", "UI packages require addressable locations: " + package);
                    foreach (var item in collected.CollectAssets.Where(a => a.CollectorType == ECollectorType.MainAssetCollector))
                    {
                        var address = collected.Command.LocationToLower ? item.Address.ToLowerInvariant() : item.Address;
                        var key = AddressKey(profile, package, address);
                        if (!result.TryAdd(key, item.AssetInfo.AssetPath)) report.Add("DuplicateAddress", key);
                        report.Graph.Add($"package:{package}/{address}|{item.AssetInfo.AssetPath}|{item.BundleName}");
                    }
                    report.Coverage.Add("Collector:" + package + " (" + collected.CollectAssets.Count + " assets)");
                }
                catch (Exception error) { report.Add("CollectorFailure", error.ToString(), AssetDatabase.GetAssetPath(profile.Collector)); report.Complete = false; }
            }
            return result;
        }

        private static void InspectConfig(UIBuildProfile profile, UIEditorReport report, HashSet<string> roots,
            Dictionary<string, string> addresses)
        {
            try
            {
                var configs = new ConfigService(new UIEditorFileConfigSource(
                    new Dictionary<string, string> { ["UISettings"] = profile.UISettingsJson }),
                    new ConfigCodec(), ConfigFormat.Json, new[] { UISettings.Table });
                UISettings table;
                try { table = configs.InitializeAsync().GetAwaiter().GetResult().Get(UISettings.Table); }
                finally { configs.ShutdownAsync().GetAwaiter().GetResult(); }
                foreach (var row in table.Values) UIConfigRegistration.Validate(SampleUIConfiguration.Convert(row));
                foreach (var group in profile.Pages.GroupBy(p => p.Profile, StringComparer.Ordinal))
                {
                    var rows = table.Values.Where(r => r.Profile == group.Key).ToArray();
                    if (rows.Length == 0) report.Add("MissingUIProfile", group.Key, profile.UISettingsJson);
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var page in group)
                    {
                        if (!ids.Add(page.Id)) report.Add("DuplicatePage", page.Id);
                        var type = Type.GetType(page.ContextType ?? "", false);
                        if (type == null || type.IsAbstract || !typeof(BaseContext).IsAssignableFrom(type) ||
                            type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
                            report.Add("ContextMapping", "Explicit concrete, closed Context class with a public parameterless constructor required: " + page.ContextType);
                        var row = rows.SingleOrDefault(r => r.Id == page.Id);
                        if (row == null) { report.Add("MissingUIRow", group.Key + "/" + page.Id, profile.UISettingsJson); continue; }
                        UIConfigRegistration.Validate(SampleUIConfiguration.Convert(row));
                        if (page.CodeBuiltDemo)
                        {
                            if (profile.Policy != UIBuildPolicy.MinimalDemo || !profile.DemoCodePageIds.Contains(page.Id))
                                report.Add("IllegalCodeDemo", "Explicit audited MinimalDemo ID required: " + page.Id);
                            else report.Skipped.Add("Code-built minimal-demo page: " + page.Id);
                            continue;
                        }
                        var package = row.PrefabPackage ?? profile.DefaultPackage;
                        if (!page.Prefab) report.Add("MissingPagePrefab", "A non-code-built page must declare its actual prefab: " + page.Id);
                        Resolve(package, row.PrefabKey, page.Prefab, profile, report, roots, addresses, typeof(GameObject));
                    }
                    foreach (var row in rows.Where(r => !ids.Contains(r.Id)))
                        report.Add("UnmappedUIRow", group.Key + "/" + row.Id, profile.UISettingsJson);
                }
                var presentation = new ConfigCodec().Decode(File.ReadAllBytes(profile.PresentationJson), ConfigFormat.Json);
                UIPresentationCatalog.Parse(presentation);
                foreach (var node in ConfigValue.EnumerateRows(presentation, 2, "UIPresentationConfig"))
                {
                    var row = node.Row;
                    if (row.Value<string>("Kind") == "Color") continue;
                    var package = row.Value<string>("Package") ?? profile.DefaultPackage;
                    if (profile.Policy == UIBuildPolicy.Production)
                        Resolve(package, row.Value<string>("Value"), null, profile, report, roots, addresses,
                            row.Value<string>("Kind") == "Font" ? typeof(TMP_FontAsset) :
                            row.Value<string>("Kind") == "Sprite" ? typeof(Sprite) : typeof(Material));
                }
                foreach (var entry in profile.RequiredResources)
                    Resolve(string.IsNullOrEmpty(entry.Package) ? profile.DefaultPackage : entry.Package,
                        entry.Address, entry.Asset, profile, report, roots, addresses);
                if (profile.Policy == UIBuildPolicy.Production)
                    foreach (var tableName in new[] { "UISettings", "LocalizationTextConfig", "UIPresentationConfig" })
                    {
                        var package = string.IsNullOrEmpty(profile.ConfigPackage) ? profile.DefaultPackage : profile.ConfigPackage;
                        var address = profile.ConfigAddressPrefix + tableName + profile.ConfigAddressSuffix;
                        Resolve(package, address, null, profile, report, roots, addresses, typeof(TextAsset));
                        if (!addresses.TryGetValue(AddressKey(profile, package, address), out var packagedPath)) continue;
                        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(packagedPath);
                        if (!asset) continue;
                        var actual = new ConfigCodec().Decode(asset.bytes, profile.ProductionConfigFormat);
                        var sourcePath = tableName == "UISettings" ? profile.UISettingsJson :
                            tableName == "LocalizationTextConfig" ? profile.LocalizationJson : profile.PresentationJson;
                        var expected = new ConfigCodec().Decode(File.ReadAllBytes(sourcePath), ConfigFormat.Json);
                        if (!JToken.DeepEquals(actual, expected))
                            report.Add("PackagedConfigMismatch", "Packaged table bytes differ from the inspected authoring snapshot: " + tableName, packagedPath);
                    }
            }
            catch (Exception error) { report.Add("ConfigFailure", error.ToString(), profile.UISettingsJson); report.Complete = false; }
        }
        private static void Resolve(string package, string address, UnityEngine.Object expected, UIBuildProfile profile,
            UIEditorReport report, HashSet<string> roots, Dictionary<string, string> addresses, Type assetType = null)
        {
            var key = AddressKey(profile, package, address);
            if (addresses.TryGetValue(key, out var path))
            {
                roots.Add(path);
                if (assetType != null && !AssetDatabase.LoadAssetAtPath(path, assetType))
                    report.Add("ResourceTypeMismatch", key + " must load as " + assetType.Name, path);
                if (expected && AssetDatabase.GetAssetPath(expected) != path)
                    report.Add("AddressMismatch", key + " resolves to a different asset.", path, expected);
            }
            else if (profile.Policy == UIBuildPolicy.Production || !expected)
                report.Add("MissingUIAddress", "Real collector has no main-asset address: " + key);
            else roots.Add(AssetDatabase.GetAssetPath(expected));
        }

        public static void InspectHierarchy(GameObject root, string path, UIBuildProfile profile,
            LocalizationTextCatalog catalog, UIEditorReport report)
        {
            if (!root) { report.Add("MissingAsset", "Hierarchy could not be loaded.", path); return; }
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var gameObject = transform.gameObject;
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) != 0)
                    report.Add("MissingScript", "Missing MonoBehaviour script.", path, gameObject);
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < transform.childCount; i++)
                    if (!names.Add(transform.GetChild(i).name)) report.Add("DuplicateName",
                        "Sibling names are ambiguous for path-based references; generated bindings use sibling indices.", path, gameObject, warning: true);
                foreach (var component in gameObject.GetComponents<Component>())
                {
                    if (!component) continue;
                    using var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var missing = property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0;
                        var field = FindField(component.GetType(), property.name);
                        var required = field != null && field.IsDefined(typeof(UIRequiredReferenceAttribute));
                        if (missing || required && !property.objectReferenceValue)
                            report.Add(missing ? "MissingReference" : "RequiredReference", "Unresolved serialized object reference.",
                                path, component, property.propertyPath);
                    }
                }
                if (gameObject.TryGetComponent<Canvas>(out var canvas))
                {
                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay && !canvas.worldCamera && !canvas.isRootCanvas)
                        report.Add("CanvasCamera", "Nested non-overlay Canvas has no camera.", path, canvas);
                    if (canvas.overrideSorting && transform != root.transform)
                        report.Add("NestedSortingCanvas", "Nested overrideSorting bypasses manager sorting leases.", path, canvas);
                }
                if (gameObject.TryGetComponent<Graphic>(out var graphic) && graphic.raycastTarget &&
                    !gameObject.GetComponent<Selectable>() && !(graphic is Image && gameObject.GetComponent<ScrollRect>()))
                    report.Add("RaycastTarget", "Decorative/non-selectable Graphic intercepts input; audit intentional blockers.",
                        path, graphic, "m_RaycastTarget", true);
                if (gameObject.TryGetComponent<Mask>(out var mask) && !gameObject.GetComponent<Graphic>())
                    report.Add("MaskGraphic", "Mask requires a Graphic.", path, mask);
                if (gameObject.GetComponents<Mask>().Length + gameObject.GetComponents<RectMask2D>().Length > 1)
                    report.Add("MaskRisk", "Overlapping stencil/rect masks can add clipping cost.", path, gameObject, warning: true);
                if (gameObject.GetComponent<ContentSizeFitter>() && gameObject.GetComponent<LayoutGroup>())
                    report.Add("LayoutRisk", "ContentSizeFitter and LayoutGroup on one object require axis/cycle review.", path, gameObject, warning: true);
            }
            if (profile.CheckTextContent)
                foreach (var issue in UIContentChecks.Inspect(root, catalog))
                    report.Add(issue.Code, issue.Message, path, issue.Target, warning: issue.Code == "Overflow");
        }
        private static FieldInfo FindField(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
            }
            return null;
        }
    }
}
