using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CodeSketch.Installer.PrimeTweenCustom
{
    // A local clone of PrimeTween's installer so we can customize behaviour
    // without being overwritten by package updates. Place this under
    // Assets/CodeSketch.Installer/... (Editor) so it stays under source control.
    internal class CodeSketchPrimeTweenInstallerAsset : ScriptableObject
    {
        [SerializeField] internal SceneAsset demoScene;
        [SerializeField] internal SceneAsset demoSceneUrp;
        [SerializeField] internal SceneAsset demoScenePro;
        [SerializeField] internal SceneAsset demoSceneProUrp;
        [SerializeField] internal Color uninstallButtonColor;
    }

    [CustomEditor(typeof(CodeSketchPrimeTweenInstallerAsset), false)]
    internal class CodeSketchPrimeTweenInstallerInspector : UnityEditor.Editor
    {
        internal const string pluginName = "PrimeTween";
        internal const string pluginPackageId = "com.kyrylokuzyk.primetween";

        // Try to find internal files in our CodeSketch folder first, then Packages / PackageCache
        internal static string TgzPath => GetInternalFilePath("com.kyrylokuzyk.primetween.tgz");
        internal static string NewTgzPath => GetInternalFilePath($"com.kyrylokuzyk.primetween-{version}.tgz");

        const string documentationUrl = "https://github.com/KyryloKuzyk/PrimeTween";

        static string GetInternalFilePath(string fileName)
        {
            // First preference: user-specified path in CodeSketchInstallerSettings
            try
            {
                var settings = UnityEngine.Resources.Load<CodeSketch.Installer.Runtime.CodeSketchInstallerSettings>("CodeSketchInstallerSettings");
                if (settings != null && !string.IsNullOrEmpty(settings.PrimeTweenTgzPath))
                {
                    var userPath = settings.PrimeTweenTgzPath.Replace('\\', '/');
                    if (File.Exists(userPath))
                        return userPath;
                    // also check if it's project-relative (Assets/...)
                    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                    var candidate = Path.Combine(projectRoot, settings.PrimeTweenTgzPath).Replace('\\', '/');
                    if (File.Exists(candidate))
                        return candidate;
                    // If the user-provided path looks like a package path but the exact file wasn't found,
                    // try to locate the archive inside that package folder using known internal locations.
                    try
                    {
                        var userPathLower = userPath.ToLowerInvariant();
                        if (userPathLower.Contains("packages") || userPathLower.Contains(pluginPackageId.ToLowerInvariant()))
                        {
                            var packageCacheDir = Path.Combine(projectRoot, "Library", "PackageCache");
                            if (Directory.Exists(packageCacheDir))
                            {
                                foreach (var dir in Directory.GetDirectories(packageCacheDir, "*", SearchOption.TopDirectoryOnly))
                                {
                                    if (!dir.ToLowerInvariant().Contains(pluginPackageId.ToLowerInvariant()) && !dir.ToLowerInvariant().Contains("codesketch.installer"))
                                        continue;
                                    var c1 = Path.Combine(dir, "Plugins", "PrimeTween", "internal", fileName);
                                    if (File.Exists(c1)) return c1;
                                    var c1b = Path.Combine(dir, "Editor", "Plugins", "PrimeTween", "internal", fileName);
                                    if (File.Exists(c1b)) return c1b;
                                    var c2 = Path.Combine(dir, "PrimeTween", "internal", fileName);
                                    if (File.Exists(c2)) return c2;
                                    var c3 = Path.Combine(dir, "internal", fileName);
                                    if (File.Exists(c3)) return c3;
                                }
                            }
                            var packagesDir = Path.Combine(projectRoot, "Packages");
                            if (Directory.Exists(packagesDir))
                            {
                                foreach (var dir in Directory.GetDirectories(packagesDir, "*", SearchOption.TopDirectoryOnly))
                                {
                                    if (!dir.ToLowerInvariant().Contains(pluginPackageId.ToLowerInvariant()) && !dir.ToLowerInvariant().Contains("codesketch.installer"))
                                        continue;
                                    var d1 = Path.Combine(dir, "Plugins", "PrimeTween", "internal", fileName);
                                    if (File.Exists(d1)) return d1;
                                    var d1b = Path.Combine(dir, "Editor", "Plugins", "PrimeTween", "internal", fileName);
                                    if (File.Exists(d1b)) return d1b;
                                    var d2 = Path.Combine(dir, "PrimeTween", "internal", fileName);
                                    if (File.Exists(d2)) return d2;
                                    var d3 = Path.Combine(dir, "internal", fileName);
                                    if (File.Exists(d3)) return d3;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            // Prefer our copy location inside this project (Plugins folder) so it's under source control
            var assetsCandidate = Path.Combine("Assets", "CodeSketch.Installer", "Plugins", "PrimeTween", "internal", fileName).Replace('\\', '/');
            if (File.Exists(Path.GetFullPath(assetsCandidate)))
                return assetsCandidate;

            // fallback to other locations if present
            try
            {
                var otherCandidate = Path.Combine("Assets", "Plugins", "PrimeTween", "internal", fileName).Replace('\\', '/');
                if (File.Exists(Path.GetFullPath(otherCandidate)))
                    return otherCandidate;

                var guids = AssetDatabase.FindAssets("PrimeTweenInstaller t:Script");
                if (guids != null && guids.Length > 0)
                {
                    var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var dir = Path.GetDirectoryName(scriptPath).Replace('\\', '/');
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var parts = dir.Split('/');
                        for (int i = parts.Length - 1; i >= 0; i--)
                        {
                            if (parts[i].Equals("internal", StringComparison.OrdinalIgnoreCase))
                            {
                                var baseDir = string.Join("/", parts.Take(i + 1));
                                return Path.Combine(baseDir, fileName).Replace('\\', '/');
                            }
                        }
                        return Path.Combine(dir, fileName).Replace('\\', '/');
                    }
                }
            }
            catch { }

            // If installer is packaged and cached (Package Cache or Packages folder), try a few known locations
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                var packagesCandidate = Path.Combine(projectRoot, "Packages", pluginPackageId, "internal", fileName).Replace('\\', '/');
                if (File.Exists(packagesCandidate))
                    return packagesCandidate;

                var packageCacheDir = Path.Combine(projectRoot, "Library", "PackageCache");
                if (Directory.Exists(packageCacheDir))
                {
                    var allDirs = Directory.GetDirectories(packageCacheDir, "*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in allDirs)
                    {
                        var candidate1 = Path.Combine(dir, "Plugins", "PrimeTween", "internal", fileName);
                        if (File.Exists(candidate1)) return candidate1;
                        var candidate1b = Path.Combine(dir, "Editor", "Plugins", "PrimeTween", "internal", fileName);
                        if (File.Exists(candidate1b)) return candidate1b;
                        var candidate2 = Path.Combine(dir, "PrimeTween", "internal", fileName);
                        if (File.Exists(candidate2)) return candidate2;
                        var candidate3 = Path.Combine(dir, "internal", fileName);
                        if (File.Exists(candidate3)) return candidate3;
                    }
                    try
                    {
                        var matches = Directory.GetFiles(packageCacheDir, fileName, SearchOption.AllDirectories);
                        if (matches != null && matches.Length > 0)
                            return matches[0];
                    }
                    catch { }
                }

                var packagesDir = Path.Combine(projectRoot, "Packages");
                if (Directory.Exists(packagesDir))
                {
                    var allPkgDirs = Directory.GetDirectories(packagesDir, "*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in allPkgDirs)
                    {
                        var candidate1 = Path.Combine(dir, "Plugins", "PrimeTween", "internal", fileName);
                        if (File.Exists(candidate1)) return candidate1;
                        var candidate1b = Path.Combine(dir, "Editor", "Plugins", "PrimeTween", "internal", fileName);
                        if (File.Exists(candidate1b)) return candidate1b;
                        var candidate2 = Path.Combine(dir, "PrimeTween", "internal", fileName);
                        if (File.Exists(candidate2)) return candidate2;
                        var candidate3 = Path.Combine(dir, "internal", fileName);
                        if (File.Exists(candidate3)) return candidate3;
                    }
                    try
                    {
                        var matches2 = Directory.GetFiles(packagesDir, fileName, SearchOption.AllDirectories);
                        if (matches2 != null && matches2.Length > 0)
                            return matches2[0];
                    }
                    catch { }
                }
            }
            catch { }

            // final fallback to assets candidate path
            return assetsCandidate;
        }

        bool isInstalled;
        bool hasNewTgz;
        GUIStyle boldButtonStyle;
        GUIStyle uninstallButtonStyle;
        GUIStyle wordWrapLabelStyle;

        void OnEnable()
        {
            isInstalled = CheckPluginInstalled();
            hasNewTgz = File.Exists(NewTgzPath);
        }

        // Expose simple check/install so our runner can call it
        internal static bool CheckPluginInstalled()
        {
            var listRequest = Client.List(true);
            while (!listRequest.IsCompleted) { }
            return listRequest.Result.Any(_ => _.name == pluginPackageId);
        }

        internal static void InstallPlugin()
        {
            if (File.Exists(NewTgzPath))
            {
                MoveAndRenameTgzArchive();
            }
            try
            {
                Debug.Log($"PrimeTweenInstaller: TgzPath='{TgzPath}', NewTgzPath='{NewTgzPath}'");
                string full = Path.GetFullPath(TgzPath);
                Debug.Log($"PrimeTweenInstaller: full path='{full}'");
                if (!File.Exists(full))
                {
                    Debug.LogError($"PrimeTweenInstaller: archive not found at '{full}'");
                    EditorUtility.DisplayDialog("PrimeTween Installer", $"Archive not found: {full}", "OK");
                    return;
                }

                var uri = new Uri(full).AbsoluteUri;
                Debug.Log($"PrimeTweenInstaller: adding package from uri='{uri}'");
                var addRequest = Client.Add(uri);
                while (!addRequest.IsCompleted) { }
                if (addRequest.Status == StatusCode.Success)
                {
                    Debug.Log("PrimeTween installed successfully.");
                }
                else
                {
                    Debug.LogError($"PrimeTweenInstaller: install failed. Status={addRequest.Status}, Error={addRequest.Error?.message}");
                    EditorUtility.DisplayDialog("PrimeTween Installer", $"Install failed: {addRequest.Error?.message}", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"PrimeTweenInstaller: exception during install: {ex}");
                EditorUtility.DisplayDialog("PrimeTween Installer", ex.Message, "OK");
            }
        }

        public override void OnInspectorGUI()
        {
            if (boldButtonStyle == null) boldButtonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            var installer = (CodeSketchPrimeTweenInstallerAsset)target;
            if (uninstallButtonStyle == null)
            {
                var textColor = installer.uninstallButtonColor;
                // If color is not set (transparent), fall back to default button text color
                if (textColor.a <= 0f)
                    textColor = GUI.skin.button.normal.textColor;
                uninstallButtonStyle = new GUIStyle(GUI.skin.button) { normal = { textColor = textColor } };
            }
            if (wordWrapLabelStyle == null) wordWrapLabelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, margin = new RectOffset(4, 4, 8, 8) };

            EditorGUI.indentLevel = 5;
            GUILayout.Space(8);
            GUILayout.Label(pluginName, EditorStyles.boldLabel);
            GUILayout.Space(4);

            if (!isInstalled)
            {
                if (GUILayout.Button("Install " + pluginName))
                {
                    InstallPlugin();
                }
                return;
            }

            if (hasNewTgz)
            {
                if (GUILayout.Button($"Update to {version}", boldButtonStyle))
                {
                    Debug.Log("Update requested");
                }
                GUILayout.Space(8);
            }

            if (GUILayout.Button("Documentation", boldButtonStyle)) Application.OpenURL(documentationUrl);

            GUILayout.Space(8);
            if (GUILayout.Button("Uninstall", uninstallButtonStyle))
            {
                Client.Remove(pluginPackageId);
                isInstalled = false;
                var msg = $"Please remove the folder manually to uninstall {pluginName} completely: 'Assets/Plugins/{pluginName}'";
                EditorUtility.DisplayDialog(pluginName, msg, "Ok");
                Debug.Log(msg);
            }
        }

        internal static void MoveAndRenameTgzArchive()
        {
            if (!File.Exists(NewTgzPath)) return;
            File.Delete(TgzPath);
            File.Delete(TgzPath + ".meta");
            File.Move(NewTgzPath, TgzPath);
            if (File.Exists(NewTgzPath + ".meta"))
                File.Move(NewTgzPath + ".meta", TgzPath + ".meta");
        }

        internal const string version = "1.4.3";
    }
}
