// Auto-run helper to ensure PrimeTween installer checks/installs when
// CodeSketch Installer window is opened.
#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace CodeSketch.Installer.Editor
{
    [InitializeOnLoad]
    static class PrimeTweenInstallerRunner
    {
        static object s_processedInstance;

        static PrimeTweenInstallerRunner()
        {
            EditorApplication.update += Update;
        }

        static void Update()
        {
            try
            {
                var windowType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == "CodeSketchInstallerWindow");
                if (windowType == null) return;

                var fi = windowType.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi == null) return;

                var inst = fi.GetValue(null);
                if (inst == null) return;

                if (s_processedInstance == inst) return;
                s_processedInstance = inst;

                EditorApplication.delayCall += RunPrimeTweenCheck;
            }
            catch { }
        }

        static void RunPrimeTweenCheck()
        {
            try
            {
                // Prefer our cloned installer inspector if present, otherwise fall back to original
                var primType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => (t.Name == "CodeSketchPrimeTweenInstallerInspector" && t.Namespace != null && t.Namespace.Contains("CodeSketch"))
                                         || (t.Name == "InstallerInspector" && t.Namespace == "PrimeTween"));

                MethodInfo checkMethod = primType?.GetMethod("CheckPluginInstalled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                MethodInfo installMethod = primType?.GetMethod("InstallPlugin", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) ??
                                           primType?.GetMethod("installPlugin", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                bool installed = true;
                if (checkMethod != null)
                {
                    installed = (bool)checkMethod.Invoke(null, null);
                }
                else
                {
                    var listReq = Client.List(true);
                    while (!listReq.IsCompleted) { }
                    installed = listReq.Result.Any(r => r.name == "com.kyrylokuzyk.primetween");
                }

                // Ensure our cloned installer asset exists so users see our inspector UI
                try
                {
                    var assetType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                        .FirstOrDefault(t => t.Name == "CodeSketchPrimeTweenInstallerAsset");
                    if (assetType != null)
                    {
                        var assetPath = "Assets/CodeSketch.Installer/Editor/CodeSketchPrimeTweenInstaller.asset";
                        var existing = AssetDatabase.LoadAssetAtPath(assetPath, assetType);
                        if (existing == null)
                        {
                            var so = ScriptableObject.CreateInstance(assetType);
                            AssetDatabase.CreateAsset((UnityEngine.Object)so, assetPath);
                            AssetDatabase.SaveAssets();
                            Debug.Log($"Created {assetPath} for CodeSketch PrimeTween installer.");
                        }
                    }
                }
                catch { }

                if (!installed)
                {
                    if (installMethod != null)
                    {
                        try
                        {
                            // Check CodeSketch settings for auto-install preference
                            bool autoInstall = false;
                            try
                            {
                                var settings = UnityEngine.Resources.Load("CodeSketchInstallerSettings") as CodeSketch.Installer.Runtime.CodeSketchInstallerSettings;
                                if (settings != null)
                                {
                                    autoInstall = settings.AutoInstallPrimeTweenOnOpen;
                                }
                            }
                            catch { }

                            if (autoInstall)
                            {
                                installMethod.Invoke(null, null);
                            }
                            else
                            {
                                if (EditorUtility.DisplayDialog("PrimeTween", "PrimeTween is not installed. Install PrimeTween now?", "Install", "Skip"))
                                {
                                    installMethod.Invoke(null, null);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"PrimeTweenInstallerRunner install failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"PrimeTweenInstallerRunner: {e.Message}");
            }
        }
    }
}
#endif
