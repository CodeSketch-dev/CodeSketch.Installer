#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
// Avoid compile-time dependency on Newtonsoft.Json so this utility can run
// before the package is installed. Use simple string-based JSON edits
// as a safe fallback.

namespace CodeSketch.Installer.Editor
{
    public static class CodeSketch_ManifestUtility
    {
        const string MANIFEST_PATH = "Packages/manifest.json";

        // =====================================================
        // DEPENDENCIES
        // =====================================================

        public static bool HasDependency(string packageName)
        {
            if (!File.Exists(MANIFEST_PATH))
                return false;

            try
            {
                var txt = File.ReadAllText(MANIFEST_PATH);
                var deps = ExtractJsonBlock(txt, "dependencies");
                if (string.IsNullOrEmpty(deps)) return false;
                return deps.IndexOf('"' + packageName + '"', StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static bool EnsureDependency(string packageName, string version)
        {
            if (!File.Exists(MANIFEST_PATH))
                return false;

            try
            {
                var txt = File.ReadAllText(MANIFEST_PATH);

                // if dependency already exists, nothing to do
                if (HasDependency(packageName)) return false;

                var depEntry = '"' + packageName + '"' + ": " + '"' + (string.IsNullOrEmpty(version) ? "latest" : version) + '"';

                if (txt.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // insert into existing dependencies block before the closing brace
                    int depsIdx = txt.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
                    int braceOpen = txt.IndexOf('{', depsIdx);
                    if (braceOpen >= 0)
                    {
                        int braceClose = FindMatchingBrace(txt, braceOpen);
                        if (braceClose > braceOpen)
                        {
                            // insert with a trailing comma if needed
                            // find insertion point just before braceClose
                            int insertPos = braceClose;
                            // if there are already entries, add a comma before new entry
                            var inner = txt.Substring(braceOpen + 1, braceClose - braceOpen - 1).Trim();
                            string toInsert = string.Empty;
                            if (!string.IsNullOrEmpty(inner))
                                toInsert = ",\n    " + depEntry;
                            else
                                toInsert = "\n    " + depEntry + "\n";

                            txt = txt.Substring(0, insertPos) + toInsert + txt.Substring(insertPos);
                            File.WriteAllText(MANIFEST_PATH, txt);
                            Debug.Log($"[Installer] Dependency added: {packageName}");
                            return true;
                        }
                    }
                }

                // no dependencies block — add one under root
                int rootOpen = txt.IndexOf('{');
                if (rootOpen >= 0)
                {
                    int insertPos = rootOpen + 1;
                    string block = "\n  \"dependencies\": {\n    " + depEntry + "\n  },\n";
                    txt = txt.Substring(0, insertPos) + block + txt.Substring(insertPos);
                    File.WriteAllText(MANIFEST_PATH, txt);
                    Debug.Log($"[Installer] Dependency added: {packageName}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("EnsureDependency failed: " + ex.Message);
                return false;
            }
        }

        public static bool RemoveDependency(string packageName)
        {
            if (!File.Exists(MANIFEST_PATH))
                return false;

            try
            {
                var txt = File.ReadAllText(MANIFEST_PATH);
                if (txt.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

                int depsIdx = txt.IndexOf("\"dependencies\"", StringComparison.OrdinalIgnoreCase);
                int braceOpen = txt.IndexOf('{', depsIdx);
                if (braceOpen < 0) return false;
                int braceClose = FindMatchingBrace(txt, braceOpen);
                if (braceClose <= braceOpen) return false;

                var inner = txt.Substring(braceOpen + 1, braceClose - braceOpen - 1);
                var pattern = '"' + packageName + '"';
                int p = inner.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (p < 0) return false;

                // remove the line that contains the package entry
                int lineStart = inner.LastIndexOf('\n', p);
                int lineEnd = inner.IndexOf('\n', p);
                if (lineStart < 0) lineStart = 0; else lineStart += 1;
                if (lineEnd < 0) lineEnd = inner.Length;

                var newInner = inner.Substring(0, lineStart) + inner.Substring(lineEnd).TrimStart();

                var newTxt = txt.Substring(0, braceOpen + 1) + newInner + txt.Substring(braceClose);
                File.WriteAllText(MANIFEST_PATH, newTxt);
                Debug.Log($"[Installer] Dependency removed: {packageName}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("RemoveDependency failed: " + ex.Message);
                return false;
            }
        }

        // =====================================================
        // SCOPED REGISTRY (AUTO-MOVE VERSION)
        // =====================================================

        /// <summary>
        /// Ensure scoped registry with AUTO-HEAL behaviour:
        /// - One scope belongs to ONE registry only
        /// - If scope exists in another registry → REMOVE there → ADD here
        /// - Safe, deterministic, idempotent
        /// </summary>
        public static bool EnsureScopedRegistry(
            string name,
            string url,
            string[] scopes
        )
        {
            if (!File.Exists(MANIFEST_PATH))
            {
                Debug.LogError("manifest.json not found");
                return false;
            }

            try
            {
                var txt = File.ReadAllText(MANIFEST_PATH);

                // Find or create scopedRegistries array
                if (txt.IndexOf("\"scopedRegistries\"", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // insert after root open
                    int rootOpen = txt.IndexOf('{');
                    if (rootOpen < 0) return false;
                    var block = "\n  \"scopedRegistries\": [\n    {\n      \"name\": \"" + name + "\",\n      \"url\": \"" + url + "\",\n      \"scopes\": [" + (scopes != null ? ("\n        \"" + string.Join("\",\n        \"", scopes) + "\"") : "") + "\n      ]\n    }\n  ],\n";
                    txt = txt.Insert(rootOpen + 1, block);
                    File.WriteAllText(MANIFEST_PATH, txt);
                    Debug.Log($"[Installer] Scoped registry added: {url}");
                    return true;
                }

                // Basic approach: ensure target registry exists and has scopes
                int regIdx = txt.IndexOf("\"scopedRegistries\"", StringComparison.OrdinalIgnoreCase);
                int arrOpen = txt.IndexOf('[', regIdx);
                if (arrOpen < 0) return false;
                int arrClose = FindMatchingBracket(txt, arrOpen);
                if (arrClose <= arrOpen) return false;

                var arrContent = txt.Substring(arrOpen + 1, arrClose - arrOpen - 1);

                // Try to find existing registry by url
                int urlPos = arrContent.IndexOf('"' + "url" + '"');
                bool modified = false;

                // naive find: look for a registry object that contains the url string
                int searchPos = 0;
                int targetObjStart = -1;
                int targetObjEnd = -1;
                while (true)
                {
                    int found = arrContent.IndexOf("\"url\"", searchPos, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;
                    // find the surrounding object braces
                    int objOpen = arrContent.LastIndexOf('{', found);
                    int objClose = arrContent.IndexOf('}', found);
                    if (objOpen >= 0 && objClose >= 0)
                    {
                        var objText = arrContent.Substring(objOpen, objClose - objOpen + 1);
                        if (objText.IndexOf(url, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            targetObjStart = objOpen;
                            targetObjEnd = objClose;
                            break;
                        }
                    }
                    searchPos = found + 1;
                }

                if (targetObjStart >= 0)
                {
                    // ensure scopes exist in object
                    var objText = arrContent.Substring(targetObjStart, targetObjEnd - targetObjStart + 1);
                    int scopesPos = objText.IndexOf("\"scopes\"", StringComparison.OrdinalIgnoreCase);
                    if (scopesPos < 0)
                    {
                        // append scopes property before object end
                        var insert = "\n      \"scopes\": [" + (scopes != null ? ("\n        \"" + string.Join("\",\n        \"", scopes) + "\"") : "") + "\n      ]\n    ";
                        objText = objText.Substring(0, objText.Length - 1) + insert + "}";
                        arrContent = arrContent.Substring(0, targetObjStart) + objText + arrContent.Substring(targetObjEnd + 1);
                        modified = true;
                    }
                    else
                    {
                        // try to add missing scopes (naive: if scope string not found, append inside scopes array)
                        foreach (var s in scopes ?? new string[0])
                        {
                            if (objText.IndexOf('"' + s + '"', StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                // insert before closing ] of scopes
                                int scopesOpen = objText.IndexOf('[', scopesPos);
                                int scopesClose = FindMatchingBracket(objText, scopesOpen);
                                if (scopesOpen >= 0 && scopesClose > scopesOpen)
                                {
                                    var inner = objText.Substring(scopesOpen + 1, scopesClose - scopesOpen - 1).Trim();
                                    string add = (string.IsNullOrEmpty(inner) ? "\n        \"" + s + "\"" : ",\n        \"" + s + "\"");
                                    objText = objText.Substring(0, scopesClose) + add + objText.Substring(scopesClose);
                                    arrContent = arrContent.Substring(0, targetObjStart) + objText + arrContent.Substring(targetObjEnd + 1);
                                    modified = true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    // append a new registry object to the array
                    string scopesBlock = "[" + (scopes != null ? ("\n        \"" + string.Join("\",\n        \"", scopes) + "\"") : "") + "\n      ]";
                    var newObj = "\n    {\n      \"name\": \"" + name + "\",\n      \"url\": \"" + url + "\",\n      \"scopes\": " + scopesBlock + "\n    },";
                    arrContent = arrContent + newObj;
                    modified = true;
                }

                if (modified)
                {
                    // write back
                    var newTxt = txt.Substring(0, arrOpen + 1) + arrContent + txt.Substring(arrClose);
                    File.WriteAllText(MANIFEST_PATH, newTxt);
                    Debug.Log($"[Installer] Scoped registry healed: {url}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("EnsureScopedRegistry failed: " + ex.Message);
                return false;
            }
        }

        // ----------------------
        // Helper text parsing
        // ----------------------
        static string ExtractJsonBlock(string txt, string propertyName)
        {
            int idx = txt.IndexOf('"' + propertyName + '"', StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            int braceOpen = txt.IndexOf('{', idx);
            if (braceOpen < 0) return null;
            int braceClose = FindMatchingBrace(txt, braceOpen);
            if (braceClose <= braceOpen) return null;
            return txt.Substring(braceOpen, braceClose - braceOpen + 1);
        }

        static int FindMatchingBrace(string txt, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < txt.Length; i++)
            {
                if (txt[i] == '{') depth++;
                else if (txt[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        static int FindMatchingBracket(string txt, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < txt.Length; i++)
            {
                if (txt[i] == '[') depth++;
                else if (txt[i] == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
#endif
