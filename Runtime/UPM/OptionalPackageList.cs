using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(menuName = "CodeSketch/UPM/Optional Package List", fileName = "OptionalPackageList")]
    public class OptionalPackageList : ScriptableObject
    {
        [Tooltip("Optional packages that can be installed or removed by the user")]
        public List<ScriptableObject> Packages = new List<ScriptableObject>();
    }
}
