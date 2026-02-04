#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace CodeSketch.Installer.Editor
{
    public static class CodeSketch_ManifestUtility
    {
        const string MANIFEST_PATH = "Packages/manifest.json";

        public static bool HasPackage(string packageName)
        {
            var json = JObject.Parse(File.ReadAllText(MANIFEST_PATH));
            return json["dependencies"]?[packageName] != null;
        }

        public static void AddPackage(string packageName, string version)
        {
            var json = JObject.Parse(File.ReadAllText(MANIFEST_PATH));
            var deps = (JObject)json["dependencies"];

            if (deps != null && deps[packageName] != null)
                return;

            if (deps != null) deps.Add(packageName, version);
            File.WriteAllText(MANIFEST_PATH, json.ToString());
        }

        public static void RemovePackage(string packageName)
        {
            var json = JObject.Parse(File.ReadAllText(MANIFEST_PATH));
            var deps = (JObject)json["dependencies"];

            if (deps != null && deps[packageName] == null)
                return;

            if (deps != null) deps.Remove(packageName);
            File.WriteAllText(MANIFEST_PATH, json.ToString());
        }
    }
}
#endif
