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

            if (settings == null || settings.RequiredPackages == null || settings.RequiredPackages.Count == 0)
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

            foreach (var pkg in settings.RequiredPackages)
            {
                if (string.IsNullOrEmpty(pkg.PackageName))
                    continue;

                if (installed.Contains(pkg.PackageName))
                    continue;

                if (pkg.InstallType == Runtime.UPMPackageInstallType.ScopedRegistry)
                {
                    CodeSketch_ManifestUtility.EnsureScopedRegistry(
                        pkg.RegistryName,
                        pkg.RegistryUrl,
                        pkg.RegistryScopes
                    );

                    AssetDatabase.Refresh();
                }

                Client.Add(pkg.GetInstallString());
            }
        }
    }
}
#endif
