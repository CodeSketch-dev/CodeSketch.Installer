using System;
using System.Collections.Generic;
using UnityEngine;

namespace CodeSketch.Installer.Runtime
{
    /// <summary>
    /// Feature hiển thị trên Installer Window.
    /// - Toggle mode: bật/tắt define + package
    /// - Options mode: chọn 1 option duy nhất (enum-like)
    /// </summary>
    [Serializable]
    public class InstallerFeatureDefinition
    {
        [Header("Info")]
        public string Label;

        // ================= MODE =================

        [Header("Feature Mode")]
        public InstallerFeatureMode Mode = InstallerFeatureMode.Toggle;

        // ================= TOGGLE MODE =================

        [Header("Scripting Define Symbols")]
        public string[] DefineSymbols;

        [Header("Optional Package")]
        public bool UsePackages;
        public List<UPMInstallEntry> Packages;

        // ================= OPTIONS MODE =================

        [Header("Options (Enum-like)")]
        public InstallerFeatureOption[] Options;

        public int DefaultOptionIndex;

        // ================= HELPERS =================

        public bool HasDefines =>
            DefineSymbols != null && DefineSymbols.Length > 0;

        public bool HasPackages =>
            UsePackages && Packages != null && Packages.Count > 0;

        public bool HasOptions =>
            Options != null && Options.Length > 0;
    }
}