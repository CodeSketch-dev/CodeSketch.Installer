#if UNITY_EDITOR
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
            {
                Debug.LogError("manifest.json not found");
                return false;
            }

            var root = JObject.Parse(File.ReadAllText(MANIFEST_PATH));

            if (root["dependencies"] == null)
                root["dependencies"] = new JObject();

            var deps = (JObject)root["dependencies"];

            // Already exists
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
        // SCOPED REGISTRY
        // =====================================================

        /// <summary>
        /// Ensure a scoped registry exists in manifest.json.
        /// - Nếu registry chưa có → add mới
        /// - Nếu registry có nhưng thiếu scope → add scope
        /// - An toàn khi gọi nhiều lần
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

            var registry = registries
                .FirstOrDefault(r => r["url"]?.ToString() == url) as JObject;

            bool dirty = false;

            if (registry == null)
            {
                registry = new JObject
                {
                    ["name"] = name,
                    ["url"] = url,
                    ["scopes"] = new JArray()
                };

                registries.Add(registry);
                dirty = true;
            }

            if (registry["scopes"] == null)
            {
                registry["scopes"] = new JArray();
                dirty = true;
            }

            var scopeArray = (JArray)registry["scopes"];

            if (scopes != null)
            {
                foreach (var scope in scopes)
                {
                    if (scopeArray.All(s => s.ToString() != scope))
                    {
                        scopeArray.Add(scope);
                        dirty = true;
                    }
                }
            }

            if (!dirty)
                return false;

            File.WriteAllText(MANIFEST_PATH, root.ToString());
            Debug.Log($"[Installer] Scoped registry ensured: {url}");

            return true;
        }
    }
}
#endif
