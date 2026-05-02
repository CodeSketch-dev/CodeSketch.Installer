#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using System.Linq;

namespace CodeSketch.Installer.Editor
{
    /// <summary>
    /// Auto-install required packages when project is opened.
    /// Optional – chỉ dùng cho template / framework.
    /// </summary>
    [InitializeOnLoad]
    static class CodeSketchInstallerBootstrap
    {
        static ListRequest _listRequest;

        static CodeSketchInstallerBootstrap()
        {
            EditorApplication.delayCall += TryAutoInstall;
        }

        static void TryAutoInstall()
        {
            var settings =
                Resources.Load<CodeSketch.Installer.Runtime.CodeSketchInstallerSettings>(
                    "CodeSketchInstallerSettings"
                );

            if (settings == null)
                return;

            // migrate if needed
            if ((settings.EssentialPackages == null || settings.EssentialPackages.Count == 0) && settings.RequiredPackages != null && settings.RequiredPackages.Count > 0)
            {
                settings.EssentialPackages = new System.Collections.Generic.List<UnityEngine.ScriptableObject>(settings.RequiredPackages);
                settings.RequiredPackages.Clear();
                UnityEditor.EditorUtility.SetDirty(settings);
                UnityEditor.AssetDatabase.SaveAssets();
            }

            if (settings.EssentialPackages == null || settings.EssentialPackages.Count == 0)
                return;

            _listRequest = Client.List(true);
            EditorApplication.update += OnListCompleted;
        }

        static void OnListCompleted()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
                return;

            EditorApplication.update -= OnListCompleted;

            var installed =
                _listRequest.Result.Select(p => p.name).ToHashSet();

            var settings =
                Resources.Load<CodeSketch.Installer.Runtime.CodeSketchInstallerSettings>(
                    "CodeSketchInstallerSettings"
                );

            foreach (var pkgObj in settings.EssentialPackages)
            {
                var ia = pkgObj as CodeSketch.Installer.Runtime.IUPMPackageAsset;
                if (ia == null) continue;

                var entry = ia.ToEntry();

                if (string.IsNullOrEmpty(entry.PackageName))
                    continue;

                if (installed.Contains(entry.PackageName))
                    continue;

                if (entry.InstallType == Runtime.UPMPackageInstallType.ScopedRegistry)
                {
                    CodeSketch_ManifestUtility.EnsureScopedRegistry(
                        entry.RegistryName,
                        entry.RegistryUrl,
                        entry.RegistryScopes
                    );

                    AssetDatabase.Refresh();
                }

                Client.Add(entry.GetInstallString());
            }
        }
    }
}
#endif
