using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(menuName = "CodeSketch/UPM/UnityPackage", fileName = "UnityPackage")]
    public class UnityPackageAsset : ScriptableObject, IUPMPackageAsset
    {
        [Header("Info")] public string Name;

        [Header("UnityPackage")]
        [Tooltip("Path or filename of .unitypackage relative to installer Third-Party folder")]
        public string PackagePath;

        [Header("Flags")] public bool IsDependency = false;
        [Header("Management")]
        [Tooltip("Mark this package as essential (auto-checked on window open). Default: false")]
        public bool IsEssential = false;

        public UPMInstallEntry ToEntry()
        {
            return new UPMInstallEntry
            {
                Name = Name,
                InstallType = UPMPackageInstallType.UnityPackage,
                PackageName = Name,
                Version = "",
                IsDependency = IsDependency,
                GitUrl = PackagePath
            };
        }

        bool IUPMPackageAsset.IsEssential { get => IsEssential; set => IsEssential = value; }
    }
}
