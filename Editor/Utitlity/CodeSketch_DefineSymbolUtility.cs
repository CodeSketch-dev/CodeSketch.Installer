#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using System;
using System.Linq;

namespace CodeSketch.Installer.Editor
{
    public static class CodeSketch_DefineSymbolUtility
    {
        public static void SetDefine(string define, bool enabled)
        {
            // Chỉ apply cho những target Unity support define
            Apply(BuildTargetGroup.Standalone, define, enabled);
            Apply(BuildTargetGroup.Android, define, enabled);
            Apply(BuildTargetGroup.iOS, define, enabled);
            Apply(BuildTargetGroup.WebGL, define, enabled);
        }

        static void Apply(BuildTargetGroup group, string define, bool enabled)
        {
#if UNITY_2021_2_OR_NEWER
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);

            var defines = PlayerSettings
                .GetScriptingDefineSymbols(namedTarget)
                .Split(';', System.StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            bool changed = false;

            if (enabled && !defines.Contains(define))
            {
                defines.Add(define);
                changed = true;
            }
            else if (!enabled && defines.Contains(define))
            {
                defines.Remove(define);
                changed = true;
            }

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
        .Split(';', System.StringSplitOptions.RemoveEmptyEntries)
        .ToList();

    bool changed = false;

    if (enabled && !defines.Contains(define))
    {
        defines.Add(define);
        changed = true;
    }
    else if (!enabled && defines.Contains(define))
    {
        defines.Remove(define);
        changed = true;
    }

    if (changed)
    {
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
            group,
            string.Join(";", defines)
        );
    }
#endif
        }
    }
}
#endif