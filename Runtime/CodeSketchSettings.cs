using UnityEngine;
using System.Collections.Generic;

namespace CodeSketch.Installer.Runtime
{
    [CreateAssetMenu(
        fileName = "CodeSketchInstallerSettings",
        menuName = "CodeSketch/Installer Settings"
    )]
    public class CodeSketchInstallerSettings : ScriptableObject
    {
        // =====================================================
        // GENERAL
        // =====================================================

        [Header("General")]
        public bool AlwaysShowOnStartup = true;

        // =====================================================
        // REQUIRED PACKAGES
        // =====================================================

        [Header("Required Packages (Auto Install)")]
        [Tooltip("Danh sách package bắt buộc của project")]
        public List<UPMInstallEntryAsset> RequiredPackages = new();

        // =====================================================
        // FEATURE DEFINITIONS (DEFINE SYMBOLS)
        // =====================================================

        [Header("Feature Toggles (Scripting Define Symbols)")]
        [Tooltip("Danh sách feature bật/tắt bằng define symbols")]
        public List<InstallerFeatureDefinitionAsset> Features = new();
    }
}