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
        [Tooltip("Danh sách package bắt buộc của project (ScriptableObject assets implementing IUPMPackageAsset)")]
        // Legacy field: older installations may have used this single list.
        // Keep it serialized for migration but hide from inspector; prefer using `EssentialPackages` and `OtherPackages`.
        [SerializeField]
        [HideInInspector]
        public List<ScriptableObject> RequiredPackages = new();

        [Tooltip("Essential packages shown in the Essentials list (ScriptableObject assets implementing IUPMPackageAsset)")]
        public List<ScriptableObject> EssentialPackages = new();

        [Tooltip("Other packages shown in the Packages list (ScriptableObject assets implementing IUPMPackageAsset)")]
        public List<ScriptableObject> OtherPackages = new();

        // =====================================================
        // FEATURE DEFINITIONS (DEFINE SYMBOLS)
        // =====================================================

        [Header("Feature Toggles (Scripting Define Symbols)")]
        [Tooltip("Danh sách feature bật/tắt bằng define symbols")]
        public List<InstallerFeatureDefinitionAsset> Features = new();

        // =====================================================
        // THIRD-PARTY INTEGRATIONS
        // =====================================================

        [Header("Third-Party Integrations")]
        [Tooltip("Hiển thị điều khiển cài đặt PrimeTween trong Installer window")]
        public bool ShowPrimeTweenControls = true;

        [Tooltip("Tự động cài PrimeTween khi mở Installer (nếu chưa cài).")]
        public bool AutoInstallPrimeTweenOnOpen = false;
    }
}