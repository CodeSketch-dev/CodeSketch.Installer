using System;
using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Một option trong feature dạng Options.
    /// Mỗi option có thể:
    /// - Có hoặc không define symbol
    /// - Có hoặc không package
    /// </summary>
    [Serializable]
    public class InstallerFeatureOption
    {
        public string Label;

        [Header("Scripting Define Symbol (optional)")]
        public string DefineSymbol;

        [Header("Optional Packages")]
        public bool UsePackages;
        public List<UPMInstallEntry> Packages;

        public bool HasDefine =>
            !string.IsNullOrEmpty(DefineSymbol);

        public bool HasPackages =>
            UsePackages && Packages != null && Packages.Count > 0;
    }
}