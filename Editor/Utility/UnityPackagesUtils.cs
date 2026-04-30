using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CodeSketch.Installer.Editor
{
    public class UnityPackagesUtils
    {
        public class UnityPackageEntry
        {
            public string Name;
            public string Version;
            public string FilePath;
            public string InstalledVersion; // null if not installed
        }

        static readonly Regex FileNameVersionRegex = new Regex(@"^(?<name>.+?)[-_ ]?v?(?<ver>\d+(?:\.\d+)*([-_][A-Za-z0-9]+)?)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex FolderVersionRegex = new Regex(@"(?<name>.+?)[-_ ]v?(?<ver>\d+(?:\.\d+)*([-_][A-Za-z0-9]+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<UnityPackageEntry> FindUnityPackagesInRepo()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var candidates = new List<string>();

            // Prefer explicit Third-Party folders under the installer package to avoid scanning unrelated plugins
            var thirdPartyPaths = new[] {
                Path.Combine(root, "Packages", "CodeSketch.Installer", "Editor", "Third-Party"),
                Path.Combine(root, "Assets", "CodeSketch.Installer", "Editor", "Third-Party"),
                Path.Combine(root, "Assets", "CodeSketch.Installer", "Third-Party"),
                Path.Combine(root, "Packages", "CodeSketch.Installer", "Third-Party")
            };

            Debug.Log($"UnityPackagesUtils(Assets): scanning Third-Party paths: {string.Join(", ", thirdPartyPaths.Where(Directory.Exists))}");

            foreach (var dir in thirdPartyPaths)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    // top-level only to avoid duplicates inside nested demo folders
                    candidates.AddRange(Directory.GetFiles(dir, "*.unitypackage", SearchOption.TopDirectoryOnly));

                    // Also accept folders named like "Name v1.2.3" (some packages are unpacked folders)
                    var subdirs = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly);
                    foreach (var sd in subdirs)
                    {
                        var folderName = Path.GetFileName(sd);
                        if (string.IsNullOrEmpty(folderName)) continue;

                        // match file-like or folder-like version pattern
                        if (FileNameVersionRegex.IsMatch(folderName) || FolderVersionRegex.IsMatch(folderName))
                        {
                            candidates.Add(sd);
                        }
                    }
                }
                catch { }
            }

            // If none found in Third-Party, look for any unitypackage directly inside the installer's root (non-recursive)
            if (candidates.Count == 0)
            {
                var installerFolder = Path.Combine(root, "Packages", "CodeSketch.Installer");
                if (Directory.Exists(installerFolder))
                {
                    try { candidates.AddRange(Directory.GetFiles(installerFolder, "*.unitypackage", SearchOption.TopDirectoryOnly)); } catch { }
                }
            }

            var list = new List<UnityPackageEntry>();

            Debug.Log($"UnityPackagesUtils(Assets): candidates found: {candidates.Count}");
            foreach (var f in candidates.OrderByDescending(f =>
                File.Exists(f) ? File.GetLastWriteTimeUtc(f) : (Directory.Exists(f) ? Directory.GetLastWriteTimeUtc(f) : DateTime.MinValue)))
            {
                var fileName = Path.GetFileNameWithoutExtension(f);
                string name = fileName;
                string ver = "";

                var m = FileNameVersionRegex.Match(fileName);
                if (m.Success)
                {
                    name = m.Groups["name"].Value.Trim();
                    ver = m.Groups["ver"].Value.Trim();
                }

                var entry = new UnityPackageEntry
                {
                    Name = name,
                    Version = ver,
                    FilePath = f,
                    InstalledVersion = GetInstalledVersion(name)
                };

                list.Add(entry);
            }

            return list;
        }

        // naive detection: scan Assets and Packages folder names for installed folder that matches name+version
        public static string GetInstalledVersion(string packageName)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var candidates = new List<string>();

            try
            {
                candidates.AddRange(Directory.GetDirectories(Path.Combine(root, "Assets"), "*", SearchOption.AllDirectories));
            }
            catch { }

            try
            {
                candidates.AddRange(Directory.GetDirectories(Path.Combine(root, "Packages"), "*", SearchOption.AllDirectories));
            }
            catch { }

            string bestVer = null;

            foreach (var d in candidates)
            {
                var folder = Path.GetFileName(d);
                if (folder == null) continue;
                if (!folder.ToLowerInvariant().Contains(packageName.ToLowerInvariant())) continue;

                var m = FolderVersionRegex.Match(folder);
                if (m.Success)
                {
                    var ver = m.Groups["ver"].Value;
                    if (IsVersionGreater(ver, bestVer))
                        bestVer = ver;
                }
                else
                {
                    // if no version in folder name, but folder name matches package, assume installed with unknown version
                    if (bestVer == null)
                        bestVer = "installed";
                }
            }

            return bestVer;
        }

        // Import the unitypackage file via AssetDatabase
        public static void ImportUnityPackage(string packagePath)
        {
            if (!File.Exists(packagePath))
            {
                Debug.LogError($"UnityPackageImporter: package not found: {packagePath}");
                return;
            }

            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh();
            Debug.Log($"UnityPackageImporter: Imported {packagePath}");
        }

        // Simple version compare: returns true if a > b. Null/empty means unknown.
        static bool IsVersionGreater(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return false;
            if (string.IsNullOrEmpty(b)) return true;

            // split numeric parts
            var an = Regex.Replace(a, "[^0-9.]", "");
            var bn = Regex.Replace(b, "[^0-9.]", "");
            var ap = an.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var x) ? x : 0).ToArray();
            var bp = bn.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var x) ? x : 0).ToArray();

            int n = Math.Max(ap.Length, bp.Length);
            for (int i = 0; i < n; i++)
            {
                var ai = i < ap.Length ? ap[i] : 0;
                var bi = i < bp.Length ? bp[i] : 0;
                if (ai > bi) return true;
                if (ai < bi) return false;
            }

            // numeric parts equal, consider a not greater
            return false;
        }
    }
}
