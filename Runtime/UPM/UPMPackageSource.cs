using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [System.Serializable]
    public enum UPMPackageSource
    {
        UnityRegistry,   // com.unity.xxx
        GitURL,          // https://github.com/...
        ScopedRegistry   // npm / private registry
    }
}
