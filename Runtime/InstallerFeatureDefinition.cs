using System;
using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Feature toggle hiển thị trên Installer Window.
    /// - Bật/tắt define symbols
    /// - Có thể kèm package install/remove
    /// </summary>
    [Serializable]
    public class InstallerFeatureDefinition
    {
        [Header("Info")]
        public string Key;
        public string Label;

        // ================= DEFINE =================

        [Header("Scripting Define Symbols")]
        public string[] DefineSymbols;

        // ================= OPTIONAL PACKAGE =================

        [Header("Optional Package")]
        public bool UsePackages;

        [Tooltip("Cách cài package cho feature này")]
        public List<UPMInstallEntry> Packages;

        // ================= HELPERS =================

        public bool HasDefines =>
            DefineSymbols != null && DefineSymbols.Length > 0;

        public bool HasPackages =>
            UsePackages && Packages != null && Packages.Count > 0;
    }
}