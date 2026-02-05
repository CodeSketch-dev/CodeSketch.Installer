#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace CodeSketch.Installer.Editor
{
    /// <summary>
    /// Utility quản lý Scripting Define Symbols cho nhiều BuildTargetGroup.
    /// Dùng cho Installer / Feature Toggle.
    /// </summary>
    public static class CodeSketch_DefineSymbolUtility
    {
        // =====================================================
        // PUBLIC API
        // =====================================================

        /// <summary>
        /// Add hoặc remove 1 define cho tất cả platform support.
        /// </summary>
        public static void SetDefine(string define, bool enabled)
        {
            if (string.IsNullOrEmpty(define))
                return;

            foreach (var group in SupportedGroups())
            {
                Apply(group, define, enabled);
            }
        }

        /// <summary>
        /// Add hoặc remove nhiều define cùng lúc.
        /// </summary>
        public static void SetDefines(IEnumerable<string> defines, bool enabled)
        {
            if (defines == null)
                return;

            foreach (var define in defines)
            {
                SetDefine(define, enabled);
            }
        }

        /// <summary>
        /// Kiểm tra define có tồn tại không (theo build target hiện tại).
        /// </summary>
        public static bool HasDefine(string define)
        {
            if (string.IsNullOrEmpty(define))
                return false;

            var group = EditorUserBuildSettings.selectedBuildTargetGroup;

#if UNITY_2021_2_OR_NEWER
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);

            return PlayerSettings
                .GetScriptingDefineSymbols(namedTarget)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Contains(define);
#else
            return PlayerSettings
                .GetScriptingDefineSymbolsForGroup(group)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Contains(define);
#endif
        }

        // =====================================================
        // CORE APPLY
        // =====================================================

        static void Apply(BuildTargetGroup group, string define, bool enabled)
        {
#if UNITY_2021_2_OR_NEWER
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);

            var defines = PlayerSettings
                .GetScriptingDefineSymbols(namedTarget)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            bool changed = ModifyList(defines, define, enabled);

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(
                    namedTarget,
                    string.Join(";", defines)
                );
            }
#else
            var defines = PlayerSettings
                .GetScriptingDefineSymbolsForGroup(group)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            bool changed = ModifyList(defines, define, enabled);

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbolsForGroup(
                    group,
                    string.Join(";", defines)
                );
            }
#endif
        }

        static bool ModifyList(List<string> defines, string define, bool enabled)
        {
            if (enabled)
            {
                if (!defines.Contains(define))
                {
                    defines.Add(define);
                    return true;
                }
            }
            else
            {
                if (defines.Remove(define))
                    return true;
            }

            return false;
        }

        // =====================================================
        // SUPPORTED GROUPS
        // =====================================================

        static BuildTargetGroup[] SupportedGroups()
        {
            return new[]
            {
                BuildTargetGroup.Standalone,
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.WebGL
            };
        }
    }
}
#endif
