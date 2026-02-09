using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Cách cài đặt UPM package.
    /// </summary>
    public enum UPMPackageInstallType
    {
        /// <summary>
        /// Unity Registry / Default registry
        /// Ví dụ: com.unity.mobile.notifications
        /// → Client.Add(name@version)
        /// </summary>
        UnityRegistry,

        /// <summary>
        /// Git URL
        /// Ví dụ: https://github.com/user/repo.git
        /// → Client.Add(gitUrl)
        /// </summary>
        GitURL,

        /// <summary>
        /// Scoped Registry (npm / private registry)
        /// → EnsureScopedRegistry trước
        /// → Client.Add(name@version)
        /// </summary>
        ScopedRegistry
    }
}