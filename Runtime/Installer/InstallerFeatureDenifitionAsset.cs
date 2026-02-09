using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Root feature definition hiển thị trên Installer Window.
    /// Chỉ giữ:
    /// - Label
    /// - Mode
    /// - Reference tới asset theo mode
    /// </summary>
    [CreateAssetMenu(
        menuName = "CodeSketch/Installer/Feature Definition",
        fileName = "Feature-"
    )]
    public class InstallerFeatureDefinitionAsset : ScriptableObject
    {
        [Header("Info")]
        public string Label;

        [Header("Feature Mode")]
        public InstallerFeatureMode Mode = InstallerFeatureMode.Toggle;

        [Header("Toggle Mode Asset")]
        public InstallerFeatureToggleAsset Toggle;

        [Header("Options Mode Asset")]
        public InstallerFeatureOptionsAsset Options;
    }
}