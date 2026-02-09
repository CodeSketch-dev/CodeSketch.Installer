using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Feature dạng Toggle (on / off)
    /// - Add / remove define symbols
    /// - Add / remove packages
    /// </summary>
    [CreateAssetMenu(
        menuName = "CodeSketch/Installer/Denifition Toggle",
        fileName = "Definition-"
    )]
    public class InstallerFeatureToggleAsset : ScriptableObject
    {
        [Header("Scripting Define Symbols")]
        public string[] DefineSymbols;

        [Header("Optional Packages")]
        public bool UsePackages;
        public List<UPMInstallEntry> Packages;

        // ================= HELPERS =================

        public bool HasDefines =>
            DefineSymbols != null && DefineSymbols.Length > 0;

        public bool HasPackages =>
            UsePackages && Packages != null && Packages.Count > 0;
    }
}