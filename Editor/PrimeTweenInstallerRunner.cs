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
                var targetTypeName = "CodeSketch.Installer.PrimeTweenCustom.CodeSketchPrimeTweenInstallerInspector";
                Type type = null;

                // Prefer types from assemblies that are not in the PackageCache (i.e., the Assets/ copy)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType(targetTypeName, false, false);
                        if (t != null)
                        {
                            var loc = (asm.Location ?? string.Empty).Replace('\\', '/');
                            if (!string.IsNullOrEmpty(loc) && !loc.Contains("PackageCache") && !loc.Contains("/packages/"))
                            {
                                type = t;
                                break;
                            }
                            // keep as fallback if nothing better found
                            if (type == null) type = t;
                        }
                    }
                    catch { }
                }

                // final fallback
                if (type == null)
                    type = Type.GetType(targetTypeName, false);

                if (type == null)
                {
                    Debug.LogWarning("PrimeTweenInstallerRunner: installer type not found.");
                    EditorApplication.update -= Update;
                    return;
                }

                var checkMethod = type.GetMethod("CheckPluginInstalled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var installMethod = type.GetMethod("InstallPlugin", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                if (checkMethod == null || installMethod == null)
                {
                    Debug.LogWarning("PrimeTweenInstallerRunner: installer methods not found.");
                    EditorApplication.update -= Update;
                    return;
                }

                // We have a valid installer type; run the check once and stop updating
                RunPrimeTweenCheck();
                EditorApplication.update -= Update;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"PrimeTweenInstallerRunner: {ex.Message}");
                EditorApplication.update -= Update;
            }
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

                // Runner no longer creates assets automatically.

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
