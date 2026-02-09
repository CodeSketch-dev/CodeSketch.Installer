using System;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [Serializable]
    public class UPMInstallEntry
    {
        public string Name;

        public UPMPackageInstallType InstallType;

        public string PackageName;
        public string Version;

        public string GitUrl;

        public string RegistryName;
        public string RegistryUrl;
        public string[] RegistryScopes;
        public bool IsDependency; // 👈 KEY FLAG

        public string GetInstallString()
        {
            return InstallType switch
            {
                UPMPackageInstallType.GitURL => GitUrl,
                _ => string.IsNullOrEmpty(Version)
                    ? PackageName
                    : $"{PackageName}@{Version}"
            };
        }
    }
}