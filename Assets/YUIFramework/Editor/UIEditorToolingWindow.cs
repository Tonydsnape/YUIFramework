using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using YUIFramework.Localization.Editor;

namespace YUIFramework.Editor
{
    public sealed class UIEditorToolingWindow : EditorWindow
    {
        private UIBuildProfile _profile;
        private UIEditorReport _report;
        private readonly UIPageGenerationRequest _request = new UIPageGenerationRequest();
        private Vector2 _scroll;
        private string _message = "", _filter = "";
        private int _tab;
        [MenuItem("Tools/YUIFramework/Editor tooling")]
        public static void Open() { GetWindow<UIEditorToolingWindow>("YUI Editor tools"); }
        private void OnGUI()
        {
            _profile = (UIBuildProfile)EditorGUILayout.ObjectField("Explicit build profile", _profile, typeof(UIBuildProfile), false);
            if (!_profile) EditorGUILayout.HelpBox("Create > YUIFramework > Build and validation profile. Missing profile blocks Player builds.", MessageType.Warning);
            _tab = GUILayout.Toolbar(_tab, new[] { "Checks / graph", "Generate page", "Preview / bundles" });
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
                if (_tab == 0) DrawChecks();
                else if (_tab == 1) DrawGenerator();
                else DrawPreviewAndBuild();
            }
            catch (Exception error) { _message = error.ToString(); UnityEngine.Debug.LogException(error); }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
        }
        private void DrawChecks()
        {
            if (GUILayout.Button("Validate declared dependency closure")) _report = UIAssetValidator.Validate(_profile);
            if (_profile && GUILayout.Button("Use this profile for Player build gate"))
            { UIBuildGate.ActiveProfilePath = AssetDatabase.GetAssetPath(_profile); _message = "Active explicit policy: " + _profile.Policy; }
            if (_report == null) return;
            EditorGUILayout.LabelField($"Policy {_report.Policy}; complete {_report.Complete}; production certified {_report.ProductionCertified}");
            EditorGUILayout.LabelField($"Coverage {_report.Coverage.Count}; skips {_report.Skipped.Count}; issues {_report.Issues.Count}");
            _filter = EditorGUILayout.TextField("Filter", _filter);
            foreach (var issue in _report.Issues.Where(i => i.ToString().IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0))
                if (GUILayout.Button(issue.ToString(), EditorStyles.wordWrappedMiniLabel)) LocateIssue(issue);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Explicit navigation / real dependency / collector edges", EditorStyles.boldLabel);
            foreach (var edge in _report.Graph.Where(g => g.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).Take(100))
            {
                var parts = edge.Split('|');
                if (parts.Length != 3) continue;
                var row = GUILayoutUtility.GetRect(200, 10000, 50, 50);
                var width = (row.width - 80) / 2;
                var left = new Rect(row.x, row.y + 18, width, 30);
                var right = new Rect(row.xMax - width, row.y + 18, width, 30);
                GUI.Label(new Rect(row.x, row.y, row.width, 18), parts[0], EditorStyles.miniLabel);
                Handles.BeginGUI();
                var from = new Vector3(left.xMax, left.center.y); var to = new Vector3(right.x, right.center.y);
                Handles.DrawAAPolyLine(2, from, to);
                Handles.DrawAAPolyLine(2, to + new Vector3(-6, -4), to, to + new Vector3(-6, 4));
                Handles.EndGUI();
                if (GUI.Button(left, parts[1], EditorStyles.miniButton)) LocateNode(parts[1]);
                if (GUI.Button(right, parts[2], EditorStyles.miniButton)) LocateNode(parts[2]);
            }
            EditorGUILayout.HelpBox("Directed edges are explicit navigation and actual AssetDatabase/collector dependencies, not inferred business flow. Display is capped at 100 filtered edges; JSON retains all edges and coverage.", MessageType.Info);
            foreach (var skip in _report.Skipped) EditorGUILayout.LabelField(skip, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Export full locatable JSON report"))
            {
                var path = EditorUtility.SaveFilePanel("UI validation report", Path.GetFullPath("Library"), "ui-validation", "json");
                if (!string.IsNullOrEmpty(path)) UIBuildGate.WriteReport(_report, path);
            }
        }
        private void DrawGenerator()
        {
            _request.Name = EditorGUILayout.TextField("Page C# name", _request.Name);
            _request.Namespace = EditorGUILayout.TextField("Namespace", _request.Namespace);
            _request.OutputFolder = EditorGUILayout.TextField("Owned output folder", _request.OutputFolder);
            _request.Prefab = (GameObject)EditorGUILayout.ObjectField("Source prefab (not modified)", _request.Prefab, typeof(GameObject), false);
            _request.Profile = EditorGUILayout.TextField("Config profile", _request.Profile);
            _request.Id = EditorGUILayout.TextField("Config ID", _request.Id);
            _request.Address = EditorGUILayout.TextField("YooAsset prefab address", _request.Address);
            _request.Package = EditorGUILayout.TextField("Package (blank = default)", _request.Package);
            EditorGUILayout.HelpBox("1. Generate owned code + additive workbook row. 2. Run existing transactional Config exporter. 3. After compilation bind a NEW prefab. 4. Register its collector address, add its explicit page declaration to the build profile, and pass Registration.Map(snapshot) to RegisterBatch. Hand-owned Context.cs is never overwritten.", MessageType.Info);
            if (GUILayout.Button("1. Generate code and propose/commit workbook row"))
            {
                var manifest = UIPageGenerator.Generate(_request);
                _message = $"Generated {manifest.Bindings.Count} serialized controls. Wait for compilation, then export config and bind.";
            }
            if (GUILayout.Button("2. Run existing Config client exporter"))
            {
                var start = new ProcessStartInfo("node", "src\\cli.js --target client")
                { WorkingDirectory = Path.GetFullPath("Config"), UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Node unavailable.");
                var stderr = process.StandardError.ReadToEndAsync();
                var stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
                _message = stdout; AssetDatabase.Refresh();
            }
            if (GUILayout.Button("3. Bind compiled view to new generated prefab"))
            {
                Selection.activeObject = UIPageGenerator.BindCompiledView(Path.Combine(_request.OutputFolder, "ui-page.generated.json"));
                _message = "New prefab saved with serialized bindings. Source prefab unchanged.";
            }
        }
        private void DrawPreviewAndBuild()
        {
            if (!_profile) return;
            if (_profile.Policy == UIBuildPolicy.Production && GUILayout.Button("Prepare isolated production workspace (exclude audited demo Resources only)"))
            {
                var parent = EditorUtility.OpenFolderPanel("Choose external parent for NEW production workspace", "", "");
                if (!string.IsNullOrEmpty(parent))
                    _message = UIBuildWorkspace.Export(_profile, Path.Combine(parent, "YUIProduction-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
            }
            foreach (var page in _profile.Pages.Where(p => p.Prefab))
                if (GUILayout.Button("Preview " + page.Id))
                {
                    var report = new UIEditorReport();
                    var map = UIAssetValidator.CollectAddresses(_profile, report);
                    if (report.HasErrors) throw new InvalidOperationException(string.Join("\n", report.Issues.Select(i => i.ToString())));
                    UIDevicePreview.OpenConfigured(page.Prefab, new UIEditorPreviewOptions
                    { LocalizationJson = _profile.LocalizationJson, PresentationJson = _profile.PresentationJson, DefaultPackage = _profile.DefaultPackage },
                        (package, address, type) =>
                        {
                            if (map.TryGetValue(UIAssetValidator.AddressKey(_profile, package, address), out var path))
                                return AssetDatabase.LoadAssetAtPath(path, type);
                            var entry = _profile.RequiredResources.SingleOrDefault(r =>
                                (string.IsNullOrEmpty(r.Package) ? _profile.DefaultPackage : r.Package) == package && r.Address == address);
                            return entry?.Asset;
                        });
                }
            EditorGUILayout.HelpBox("Official YooAsset 3 Legacy pipeline. External output only; no StreamingAssets copy or implicit target switch. Cancel is supported BEFORE the synchronous build, not midway through YooAsset tasks. Version outputs and edited history are never overwritten.", MessageType.Info);
            if (GUILayout.Button("Open official collector")) EditorApplication.ExecuteMenuItem("YooAsset/AssetBundle Collector");
            if (GUILayout.Button("Validate and build real local AssetBundles") &&
                EditorUtility.DisplayDialog("Build AssetBundles", $"Target {_profile.Target}, package {_profile.BuildPackage}, version {_profile.PackageVersion}\n{_profile.OutputRoot}\nContinue?", "Build", "Cancel"))
            {
                var record = UIYooAssetBuilder.Build(_profile);
                _message = $"{record.Status}\n{record.Output}\n{record.Error}";
                _message += record.Summary == null ? "\nReport summary unavailable for this canceled/failed build." :
                    $"\nOfficial report: {record.Summary.AssetFileTotalCount} assets, {record.Summary.AllBundleTotalCount} bundles, " +
                    $"{record.Summary.AllBundleTotalSize} bytes; builder {record.Summary.BuildSeconds}s.\n{record.ReportPath}";
                if (record.Status == "Failed") UnityEngine.Debug.LogError(_message);
            }
            if (GUILayout.Button("Open version history / official reports") && Directory.Exists(_profile.OutputRoot))
                EditorUtility.RevealInFinder(_profile.OutputRoot);
        }
        private static void Locate(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (!asset) return;
            Selection.activeObject = asset; EditorGUIUtility.PingObject(asset);
        }
        private void LocateNode(string node)
        {
            var page = _profile ? _profile.Pages.FirstOrDefault(p => p.Id == node) : null;
            Locate(page?.Prefab ? AssetDatabase.GetAssetPath(page.Prefab) : node);
        }
        public static void LocateIssue(UIEditorIssue issue)
        {
            Transform root = null;
            if (issue.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                root = PrefabStageUtility.OpenPrefab(issue.AssetPath)?.prefabContentsRoot.transform;
            else if (issue.AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(issue.AssetPath);
                if (sceneAsset && AssetDatabase.OpenAsset(sceneAsset))
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(issue.AssetPath);
                    var roots = scene.IsValid() ? scene.GetRootGameObjects() : Array.Empty<GameObject>();
                    if (issue.SiblingRoute.Length > 0 && issue.SiblingRoute[0] < roots.Length)
                        root = roots[issue.SiblingRoute[0]].transform;
                }
            }
            if (!root || issue.SiblingRoute.Length == 0) { Locate(issue.AssetPath); return; }
            foreach (var index in issue.SiblingRoute.Skip(1))
            {
                if (index < 0 || index >= root.childCount) { Locate(issue.AssetPath); return; }
                root = root.GetChild(index);
            }
            Selection.activeGameObject = root.gameObject; EditorGUIUtility.PingObject(root.gameObject);
        }
    }
}
