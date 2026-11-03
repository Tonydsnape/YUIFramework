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
    // Resources are implicit Player inputs. Exempting them in a report would lie:
    // produce an explicit isolated build workspace instead, never move source assets.
    public static class UIBuildWorkspace
    {
        public static string Export(UIBuildProfile profile, string destination)
        {
            if (!profile || profile.Policy != UIBuildPolicy.Production)
                throw new ArgumentException("Only a Production profile can prepare a production workspace.");
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var target = Path.GetFullPath(destination);
            UIBuildGate.RejectLinkedAncestors(target);
            if (!Path.IsPathRooted(destination) || Directory.Exists(target) || File.Exists(target) ||
                target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith(target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a new absolute external workspace; existing projects are never overwritten.");
            var excluded = new HashSet<string>(profile.AuditedDemoResourcePaths, StringComparer.Ordinal);
            foreach (var path in excluded)
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.Contains("/Resources/") ||
                    path.Split('/').Any(p => p == ".." || p == ".") || !File.Exists(path) ||
                    !AssetDatabase.LoadMainAssetAtPath(path) ||
                    Path.GetFullPath(path).StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == false)
                    throw new ArgumentException("Only exact existing demo Resources files can be excluded: " + path);
            var roots = profile.Scenes.Cast<UnityEngine.Object>().Concat(profile.Extras).Concat(profile.Pages.Select(p => p.Prefab))
                .Concat(profile.RequiredResources.Select(p => p.Asset)).Where(p => p).Select(AssetDatabase.GetAssetPath).ToArray();
            var dependencies = new HashSet<string>(roots.SelectMany(p => AssetDatabase.GetDependencies(p, true)), StringComparer.Ordinal);
            foreach (var required in dependencies.Intersect(excluded))
                throw new InvalidOperationException("Cannot exclude a declared target dependency: " + required);
            var temporary = Path.Combine(Path.GetDirectoryName(target), ".yui-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            Directory.CreateDirectory(temporary);
            try
            {
                foreach (var directory in new[] { "Assets", "Packages", "ProjectSettings" })
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(source, directory), "*", SearchOption.AllDirectories))
                    {
                        var relative = file.Substring(source.Length + 1).Replace('\\', '/');
                        if (excluded.Contains(relative) || relative.EndsWith(".meta", StringComparison.Ordinal) &&
                            excluded.Contains(relative.Substring(0, relative.Length - 5))) continue;
                        var output = Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar));
                        if (Application.platform == RuntimePlatform.WindowsEditor && Math.Max(output.Length, target.Length + relative.Length + 1) >= 260)
                            throw new PathTooLongException("Choose a shorter external workspace path for Unity 2022 Windows: " + relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(output)); File.Copy(file, output, false);
                        using var sha = SHA256.Create();
                        hashes.Add(relative, BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(output))).Replace("-", ""));
                    }
                File.WriteAllText(Path.Combine(temporary, "ui-staging-manifest.json"), JsonConvert.SerializeObject(new
                {
                    profile = AssetDatabase.GetAssetPath(profile), policy = "Production",
                    createdUtc = DateTime.UtcNow.ToString("O"), excludedDemoResources = excluded.OrderBy(p => p).ToArray(),
                    status = "PreparedNotValidated", limitation = "Dynamic code-only loads must be explicitly declared. Run the same validator in this workspace; preparation is not production certification.",
                    sha256 = hashes
                }, Formatting.Indented));
                Directory.Move(temporary, target);
                return target;
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
    }
}
