#if UNITY_EDITOR
using CodeSketch.Installer.Runtime;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace CodeSketch.Installer.Editor
{
    public class CodeSketch_InstallerWindow : EditorWindow
    {
        static CodeSketch_InstallerWindow _instance;

        CodeSketchInstallerSettings _settings;

        const string SETTINGS_RESOURCE_PATH = "CodeSketchInstallerSettings";
        const string SETTINGS_ASSET_PATH =
            "Assets/CodeSketch.Installer/Editor/Resources/CodeSketchInstallerSettings.asset";

        AddRequest    _addRequest;
        RemoveRequest _removeRequest;
        ListRequest   _listRequest;

        HashSet<string> _installedPackages = new();

        // =====================================================
        // FEATURE DESCRIPTOR
        // =====================================================

        class FeatureToggle
        {
            public string Label;

            public string Define;

            public string PackageName;
            public string PackageVersion;

            public bool HasDefine  => !string.IsNullOrEmpty(Define);
            public bool HasPackage => !string.IsNullOrEmpty(PackageName);
        }

        List<FeatureToggle> _features;

        // =====================================================
        // INIT
        // =====================================================

        [MenuItem("CodeSketch/Tools/Installer")]
        static void Open()
        {
            _instance = GetWindow<CodeSketch_InstallerWindow>(
                "CodeSketch Installer"
            );
        }

        [InitializeOnLoadMethod]
        static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += () =>
            {
                if (_instance != null)
                {
                    _instance.ResetRequests();
                    _instance.RefreshPackageState();
                    _instance.Repaint();
                }
            };
        }

        void OnEnable()
        {
            _instance = this;

            ResetRequests();

            _settings = LoadOrCreateSettings();

            BuildFeatureList();
            RefreshPackageState();
        }

        void OnDisable()
        {
            ResetRequests();
        }

        // =====================================================
        // FEATURE CONFIG (CHỈ SỬA CHỖ NÀY KHI MUỐN THÊM)
        // =====================================================

        void BuildFeatureList()
        {
            _features = new List<FeatureToggle>
            {
                new FeatureToggle
                {
                    Label = "Internet",
                    Define = "CODESKETCH_INTERNET"
                },

                new FeatureToggle
                {
                    Label = "Mobile Notifications",
                    Define = "CODESKETCH_NOTIFICATIONS",
                    PackageName = "com.unity.mobile.notifications",
                    PackageVersion = "2.4.2"
                },

                // Ví dụ: package only
                /*
                new FeatureToggle
                {
                    Label = "Addressables",
                    PackageName = "com.unity.addressables",
                    PackageVersion = "1.21.19"
                }
                */
            };
        }

        // =====================================================
        // GUI
        // =====================================================

        void OnGUI()
        {
            if (_settings == null)
                return;

            GUILayout.Label("CodeSketch Installer", EditorStyles.boldLabel);
            GUILayout.Space(6);

            _settings.AlwaysShowOnStartup =
                EditorGUILayout.Toggle(
                    "Always show on startup",
                    _settings.AlwaysShowOnStartup
                );

            GUILayout.Space(10);
            GUILayout.Label("Features", EditorStyles.boldLabel);

            foreach (var feature in _features)
            {
                DrawFeatureToggle(feature);
            }

            EditorUtility.SetDirty(_settings);
        }

        // =====================================================
        // CORE TOGGLE LOGIC (DUY NHẤT)
        // =====================================================

        void DrawFeatureToggle(FeatureToggle feature)
        {
            bool busy = IsBusy();

            EditorGUI.BeginDisabledGroup(busy);

            bool currentValue = GetFeatureState(feature);

            bool newValue = EditorGUILayout.Toggle(
                feature.Label,
                currentValue
            );

            EditorGUI.EndDisabledGroup();

            if (busy || newValue == currentValue)
                return;

            ApplyFeature(feature, newValue);
        }

        bool GetFeatureState(FeatureToggle feature)
        {
            if (feature.HasPackage)
                return _installedPackages.Contains(feature.PackageName);

            if (feature.HasDefine)
                return HasDefine(feature.Define);

            return false;
        }

        void ApplyFeature(FeatureToggle feature, bool enable)
        {
            // DEFINE
            if (feature.HasDefine)
            {
                CodeSketch_DefineSymbolUtility.SetDefine(
                    feature.Define,
                    enable
                );
            }

            // PACKAGE
            if (feature.HasPackage)
            {
                if (enable)
                {
                    _addRequest = Client.Add(
                        string.IsNullOrEmpty(feature.PackageVersion)
                            ? feature.PackageName
                            : $"{feature.PackageName}@{feature.PackageVersion}"
                    );
                    EditorApplication.update += OnAddProgress;
                }
                else
                {
                    _removeRequest = Client.Remove(
                        feature.PackageName
                    );
                    EditorApplication.update += OnRemoveProgress;
                }
            }

            Repaint();
        }

        // =====================================================
        // PACKAGE MANAGER
        // =====================================================

        void RefreshPackageState()
        {
            _listRequest = Client.List(true);
            EditorApplication.update += OnListProgress;
        }

        void OnListProgress()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
                return;

            EditorApplication.update -= OnListProgress;

            _installedPackages.Clear();

            foreach (var p in _listRequest.Result)
                _installedPackages.Add(p.name);

            _listRequest = null;
            Repaint();
        }

        void OnAddProgress()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnAddProgress;
            _addRequest = null;

            RefreshPackageState();
        }

        void OnRemoveProgress()
        {
            if (_removeRequest == null || !_removeRequest.IsCompleted)
                return;

            EditorApplication.update -= OnRemoveProgress;
            _removeRequest = null;

            RefreshPackageState();
        }

        // =====================================================
        // UTIL
        // =====================================================

        bool IsBusy()
        {
            return _addRequest != null || _removeRequest != null || _listRequest != null;
        }

        bool HasDefine(string define)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;

            return PlayerSettings
                .GetScriptingDefineSymbolsForGroup(group)
                .Split(';')
                .Contains(define);
        }

        void ResetRequests()
        {
            EditorApplication.update -= OnAddProgress;
            EditorApplication.update -= OnRemoveProgress;
            EditorApplication.update -= OnListProgress;

            _addRequest = null;
            _removeRequest = null;
            _listRequest = null;
        }

        // =====================================================
        // SETTINGS
        // =====================================================

        static CodeSketchInstallerSettings LoadOrCreateSettings()
        {
            var settings =
                Resources.Load<CodeSketchInstallerSettings>(
                    SETTINGS_RESOURCE_PATH
                );

            if (settings != null)
                return settings;

            settings = CreateInstance<CodeSketchInstallerSettings>();

            var folder =
                "Assets/CodeSketch.Installer/Editor/Resources";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(
                    "Assets/CodeSketch.Installer",
                    "Editor"
                );
                AssetDatabase.CreateFolder(
                    "Assets/CodeSketch.Installer/Editor",
                    "Resources"
                );
            }

            AssetDatabase.CreateAsset(
                settings,
                SETTINGS_ASSET_PATH
            );

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return settings;
        }
    }
}
#endif
