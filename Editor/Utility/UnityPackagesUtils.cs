using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using System.Reflection;
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
            public string InstalledPath; // path to installed folder if detected
        }

        static readonly Regex FileNameVersionRegex = new Regex(@"^(?<name>.+?)[-_ ]?v?(?<ver>\d+(?:\.\d+)*([-_][A-Za-z0-9]+)?)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex FolderVersionRegex = new Regex(@"(?<name>.+?)[-_ ]v?(?<ver>\d+(?:\.\d+)*([-_][A-Za-z0-9]+)?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Cache directories under Assets/ and Packages/ to avoid repeated expensive AllDirectories scans
        static List<string> _assetDirectoriesCache = null;
        static DateTime _assetCacheTime = DateTime.MinValue;
        const int AssetCacheTTLSeconds = 3;

        static void EnsureAssetDirectoryCache()
        {
            try
            {
                if (_assetDirectoriesCache != null && (DateTime.UtcNow - _assetCacheTime).TotalSeconds < AssetCacheTTLSeconds)
                    return;

                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var list = new List<string>();
                try { list.AddRange(Directory.GetDirectories(Path.Combine(root, "Assets"), "*", SearchOption.AllDirectories)); } catch { }
                try { list.AddRange(Directory.GetDirectories(Path.Combine(root, "Packages"), "*", SearchOption.AllDirectories)); } catch { }

                _assetDirectoriesCache = list;
                _assetCacheTime = DateTime.UtcNow;
            }
            catch { }
        }

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

            // also include any PackageCache copies (Unity may load package code from Library/PackageCache)
            var pathsToScan = new List<string>(thirdPartyPaths);
            try
            {
                var cacheRoot = Path.Combine(root, "Library", "PackageCache");
                if (Directory.Exists(cacheRoot))
                {
                    foreach (var sub in Directory.GetDirectories(cacheRoot))
                    {
                        var name = Path.GetFileName(sub);
                        if (name != null && name.StartsWith("com.codesketch.installer", StringComparison.OrdinalIgnoreCase))
                        {
                            var candidate = Path.Combine(sub, "Editor", "Third-Party");
                            pathsToScan.Add(candidate);
                        }
                    }
                }
            }
            catch { }



            foreach (var dir in pathsToScan)
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


            // Ensure directory cache is fresh before detecting installed versions
            EnsureAssetDirectoryCache();

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

                // Use cached directory list inside GetInstalledVersion/GetInstalledPath to avoid rescanning
                var entry = new UnityPackageEntry
                {
                    Name = name,
                    Version = ver,
                    FilePath = f,
                    InstalledVersion = GetInstalledVersion(name),
                    InstalledPath = GetInstalledPath(name)
                };

                list.Add(entry);
            }

            return list;
        }

        // naive detection: scan Assets and Packages folder names for installed folder that matches name+version
        public static string GetInstalledVersion(string packageName)
        {
            // Use cached directory list to avoid expensive repeated AllDirectories scans
            EnsureAssetDirectoryCache();
            var candidates = new List<string>();
            if (_assetDirectoriesCache != null)
            {
                candidates.AddRange(_assetDirectoriesCache);
            }

            string bestVer = null;
            string foundPath = null;

            foreach (var d in candidates)
            {
                var folder = Path.GetFileName(d);
                if (folder == null) continue;
                // normalize names (remove non-alphanumeric) to match variants like "DOTweenPro" vs "DOTween Pro"
                var normFolder = Regex.Replace(folder.ToLowerInvariant(), "[^a-z0-9]", "");
                var normPackage = Regex.Replace(packageName.ToLowerInvariant(), "[^a-z0-9]", "");
                if (!normFolder.Contains(normPackage)) continue;

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
                if (foundPath == null)
                    foundPath = d;
            }

            _lastFoundPath = foundPath;
            return bestVer;
        }

        static string _lastFoundPath = null;

        public static string GetInstalledPath(string packageName)
        {
            GetInstalledVersion(packageName);
            return _lastFoundPath;
        }

        // Import the unitypackage file via AssetDatabase
        public static void ImportUnityPackage(string packagePath, string packageName = null)
        {
            if (!File.Exists(packagePath))
            {
                return;
            }

            AssetDatabase.ImportPackage(packagePath, false);
            AssetDatabase.Refresh();
            if (!string.IsNullOrEmpty(packageName))
            {
                var installed = GetInstalledPath(packageName);
                TrySaveMapping(packageName, installed ?? string.Empty, packagePath, GetInstalledVersion(packageName));
            }
        }

        static void TrySaveMapping(string name, string installedPath, string sourcePath, string version)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    var t = asm.GetType("CodeSketch.Installer.Editor.CodeSketchPackageMap");
                    if (t != null)
                    {
                        var mi = t.GetMethod("SaveMapping", BindingFlags.Public | BindingFlags.Static);
                        if (mi != null)
                        {
                            mi.Invoke(null, new object[] { name, installedPath, sourcePath, version });
                        }
                        return;
                    }
                }
            }
            catch { }
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

        // public wrapper so UI can compare versions
        public static bool CompareVersionGreater(string a, string b)
        {
            return IsVersionGreater(a, b);
        }
    }
}
