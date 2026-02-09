#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CodeSketch.Installer.Runtime;

namespace CodeSketch.Installer.Editor
{
    /// <summary>
    /// Auto open CodeSketch Installer on Unity startup
    /// if "Always Show On Startup" is enabled.
    /// </summary>
    [InitializeOnLoad]
    public static class CodeSketchInstallerInit
    {
        static CodeSketchInstallerInit()
        {
            // Delay để đợi Unity load xong editor + assets
            EditorApplication.delayCall += TryOpenInstaller;
        }

        static void TryOpenInstaller()
        {
            // Đang compile / update scripts → bỏ qua
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var settings = Resources.Load<CodeSketchInstallerSettings>("CodeSketchInstallerSettings");

            if (settings == null)
                return;

            if (!settings.AlwaysShowOnStartup)
                return;

            // Mở Installer Window
            CodeSketch_InstallerWindow.Open();
        }
    }
}
#endif