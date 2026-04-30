using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(menuName = "CodeSketch/UPM/Third-Party Package List", fileName = "ThirdPartyPackageList")]
    public class ThirdPartyPackageList : ScriptableObject
    {
        [Tooltip("Third-party unitypackages located inside the installer/Third-Party folders")]
        public List<ScriptableObject> Packages = new List<ScriptableObject>();
    }
}
