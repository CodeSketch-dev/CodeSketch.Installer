using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CodeSketch.Installer.Editor
{
    public class CodeSketchPackageMap : ScriptableObject
    {
        [System.Serializable]
        public class MapEntry
        {
            public string Name;
            public string InstalledPath;
            public string SourcePath;
            public string Version;
        }

        public List<MapEntry> Entries = new List<MapEntry>();

        const string RESOURCE_PATH = "CodeSketchPackageMap"; // will be stored in Resources
        const string ASSET_PATH = "Assets/CodeSketch.Installer/Editor/Resources/CodeSketchPackageMap.asset";

        public static CodeSketchPackageMap LoadOrCreate()
        {
            var map = Resources.Load<CodeSketchPackageMap>(RESOURCE_PATH);
            if (map != null) return map;

            if (!AssetDatabase.IsValidFolder("Assets/CodeSketch.Installer/Editor/Resources"))
            {
                AssetDatabase.CreateFolder("Assets/CodeSketch.Installer", "Editor");
                AssetDatabase.CreateFolder("Assets/CodeSketch.Installer/Editor", "Resources");
            }

            map = CreateInstance<CodeSketchPackageMap>();
            AssetDatabase.CreateAsset(map, ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return map;
        }

        public static void SaveMapping(string name, string installedPath, string sourcePath, string version)
        {
            var map = LoadOrCreate();
            var entry = map.Entries.Find(e => e.Name == name);
            if (entry == null)
            {
                entry = new MapEntry { Name = name, InstalledPath = installedPath, SourcePath = sourcePath, Version = version };
                map.Entries.Add(entry);
            }
            else
            {
                entry.InstalledPath = installedPath;
                entry.SourcePath = sourcePath;
                entry.Version = version;
            }
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        public static MapEntry GetMapping(string name)
        {
            var map = Resources.Load<CodeSketchPackageMap>(RESOURCE_PATH);
            if (map == null) return null;
            return map.Entries.Find(e => e.Name == name);
        }

        public static void RemoveMapping(string name)
        {
            var map = Resources.Load<CodeSketchPackageMap>(RESOURCE_PATH);
            if (map == null) return;
            var entry = map.Entries.Find(e => e.Name == name);
            if (entry != null)
            {
                map.Entries.Remove(entry);
                EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
