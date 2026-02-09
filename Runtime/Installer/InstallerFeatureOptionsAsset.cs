using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Feature dạng Options (enum-like)
    /// - Chỉ 1 option active
    /// - Mỗi option có thể có define + package
    /// </summary>
    [CreateAssetMenu(
        menuName = "CodeSketch/Installer/Denifition Options",
        fileName = "Definition-"
    )]
    public class InstallerFeatureOptionsAsset : ScriptableObject
    {
        public InstallerFeatureOption[] Options;
        public int DefaultOptionIndex;

        public bool HasOptions =>
            Options != null && Options.Length > 0;
    }
}