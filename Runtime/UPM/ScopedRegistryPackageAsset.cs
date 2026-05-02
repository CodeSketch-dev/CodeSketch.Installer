using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(menuName = "CodeSketch/UPM/Scoped Registry Package", fileName = "ScopedRegistryPackage")]
    public class ScopedRegistryPackageAsset : ScriptableObject, IUPMPackageAsset
    {
        [Header("Info")] public string Name;

        [Header("Package")] public string PackageName;
        public string Version;

        [Header("Scoped Registry")]
        public string RegistryName;
        public string RegistryUrl;
        public string[] RegistryScopes;

        [Header("Flags")] public bool IsDependency = false;

        [Header("Management")]
        [Tooltip("Mark this package as essential (auto-checked on window open). Default: false")]
        public bool IsEssential = false;

        public UPMInstallEntry ToEntry()
        {
            return new UPMInstallEntry
            {
                Name = Name,
                InstallType = UPMPackageInstallType.ScopedRegistry,
                PackageName = PackageName,
                Version = Version,
                RegistryName = RegistryName,
                RegistryUrl = RegistryUrl,
                RegistryScopes = RegistryScopes,
                IsDependency = IsDependency
            };
        }

        bool IUPMPackageAsset.IsEssential { get => IsEssential; set => IsEssential = value; }
    }
}
