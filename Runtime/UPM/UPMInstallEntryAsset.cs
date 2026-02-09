using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(
        menuName = "CodeSketch/UPM/UPM Install Entry",
        fileName = "UPMInstallEntry"
    )]
    public class UPMInstallEntryAsset : ScriptableObject
    {
        [Header("Info")]
        public string Name;

        [Header("Install")]
        public UPMPackageInstallType InstallType;

        [Header("Package")]
        public string PackageName;
        public string Version;
        public string GitUrl;

        [Header("Scoped Registry")]
        public string RegistryName;
        public string RegistryUrl;
        public string[] RegistryScopes;

        [Header("Flags")]
        public bool IsDependency = true;

        // ================= CONVERT =================

        public UPMInstallEntry ToEntry()
        {
            return new UPMInstallEntry
            {
                Name = Name,
                InstallType = InstallType,
                PackageName = PackageName,
                Version = Version,
                GitUrl = GitUrl,
                RegistryName = RegistryName,
                RegistryUrl = RegistryUrl,
                RegistryScopes = RegistryScopes,
                IsDependency = IsDependency
            };
        }
    }
}