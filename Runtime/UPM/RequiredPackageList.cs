using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(menuName = "CodeSketch/UPM/Required Package List", fileName = "RequiredPackageList")]
    public class RequiredPackageList : ScriptableObject
    {
        [Tooltip("Packages that are essential and should be installed automatically")]
        public List<ScriptableObject> Packages = new List<ScriptableObject>();
    }
}
