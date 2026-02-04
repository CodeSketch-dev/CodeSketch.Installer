using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(
        fileName = "CodeSketchInstallerSettings",
        menuName = "CodeSketch/Installer Settings"
    )]
    public class CodeSketchInstallerSettings : ScriptableObject
    {
        [Header("General")]
        public bool AlwaysShowOnStartup = true;

        [Header("Features")]
        public bool MobileNotifications;
        public bool Internet;
    }
}
