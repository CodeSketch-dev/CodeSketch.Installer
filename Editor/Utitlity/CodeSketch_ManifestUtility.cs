#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;

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

            var json = JObject.Parse(File.ReadAllText(MANIFEST_PATH));
            return json["dependencies"]?[packageName] != null;
        }

        public static bool EnsureDependency(string packageName, string version)
        {
            if (!File.Exists(MANIFEST_PATH))
                return false;

            var root = JObject.Parse(File.ReadAllText(MANIFEST_PATH));

            if (root["dependencies"] == null)
                root["dependencies"] = new JObject();

            var deps = (JObject)root["dependencies"];

            if (deps[packageName] != null)
                return false;

            deps.Add(packageName, string.IsNullOrEmpty(version) ? "latest" : version);

            File.WriteAllText(MANIFEST_PATH, root.ToString());
            Debug.Log($"[Installer] Dependency added: {packageName}");
            return true;
        }

        public static bool RemoveDependency(string packageName)
        {
            if (!File.Exists(MANIFEST_PATH))
                return false;

            var root = JObject.Parse(File.ReadAllText(MANIFEST_PATH));
            var deps = root["dependencies"] as JObject;

            if (deps == null || deps[packageName] == null)
                return false;

            deps.Remove(packageName);
            File.WriteAllText(MANIFEST_PATH, root.ToString());

            Debug.Log($"[Installer] Dependency removed: {packageName}");
            return true;
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

            var root = JObject.Parse(File.ReadAllText(MANIFEST_PATH));

            if (root["scopedRegistries"] == null)
                root["scopedRegistries"] = new JArray();

            var registries = (JArray)root["scopedRegistries"];
            bool dirty = false;

            // =====================================================
            // 1. Find or create target registry (by URL)
            // =====================================================

            var targetRegistry = registries
                .OfType<JObject>()
                .FirstOrDefault(r => r["url"]?.ToString() == url);

            if (targetRegistry == null)
            {
                targetRegistry = new JObject
                {
                    ["name"] = name,
                    ["url"] = url,
                    ["scopes"] = new JArray()
                };

                registries.Add(targetRegistry);
                dirty = true;
            }

            if (targetRegistry["scopes"] == null)
            {
                targetRegistry["scopes"] = new JArray();
                dirty = true;
            }

            var targetScopes = (JArray)targetRegistry["scopes"];

            // =====================================================
            // 2. For each scope → remove from WRONG registries
            // =====================================================

            if (scopes != null)
            {
                foreach (var scope in scopes)
                {
                    foreach (var registry in registries.OfType<JObject>())
                    {
                        var regUrl = registry["url"]?.ToString();
                        if (regUrl == url)
                            continue;

                        if (registry["scopes"] is not JArray arr)
                            continue;

                        var token = arr.FirstOrDefault(s => s.ToString() == scope);
                        if (token != null)
                        {
                            arr.Remove(token);
                            dirty = true;

                            Debug.LogWarning(
                                $"[Installer] Scope '{scope}' removed from wrong registry '{regUrl}'"
                            );
                        }
                    }

                    // =================================================
                    // 3. Add scope to target registry if missing
                    // =================================================

                    if (targetScopes.All(s => s.ToString() != scope))
                    {
                        targetScopes.Add(scope);
                        dirty = true;

                        Debug.Log(
                            $"[Installer] Scope '{scope}' added to registry '{url}'"
                        );
                    }
                }
            }

            if (!dirty)
                return false;

            File.WriteAllText(MANIFEST_PATH, root.ToString());
            Debug.Log($"[Installer] Scoped registry healed: {url}");
            return true;
        }
    }
}
#endif
