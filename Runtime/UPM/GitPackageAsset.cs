using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(menuName = "CodeSketch/UPM/Git Package", fileName = "GitPackage")]
    public class GitPackageAsset : ScriptableObject, IUPMPackageAsset
    {
        [Header("Info")] public string Name;

        [Header("Install")] public string GitUrl;

        [Header("Package")]
        public string PackageName; // optional
        public string Version;

        [Header("Flags")] public bool IsDependency = true;
        [Header("Management")]
        [Tooltip("Mark this package as essential (auto-checked on window open). Default: false")]
        public bool IsEssential = false;

        public UPMInstallEntry ToEntry()
        {
            return new UPMInstallEntry
            {
                Name = Name,
                InstallType = UPMPackageInstallType.GitURL,
                GitUrl = GitUrl,
                PackageName = PackageName,
                Version = Version,
                IsDependency = IsDependency
            };
        }

        bool IUPMPackageAsset.IsEssential { get => IsEssential; set => IsEssential = value; }
    }
}
