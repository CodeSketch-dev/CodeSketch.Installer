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

        Queue<UPMInstallEntry> _requiredInstallQueue = new();
        Queue<UPMInstallEntry> _featureInstallQueue  = new();
        Queue<UPMInstallEntry> _featureRemoveQueue   = new();

        HashSet<string> _ensuredRegistries = new();

        // ================= RESOLVE SCHEDULER =================
        bool _needResolve;
        bool _resolveScheduled;

        // ================= FRAMEWORK INSTALL =================
        bool _frameworkInstalled;

        static readonly UPMInstallEntry CODESKETCH =
            new UPMInstallEntry
            {
                Name = "CodeSketch",
                InstallType = UPMPackageInstallType.GitURL,
                GitUrl = "https://github.com/CodeSketch-dev/CodeSketch.git#main",
                PackageName = "CodeSketch",
                IsDependency = false
            };

        // =====================================================
        // INIT
        // =====================================================

        [MenuItem("CodeSketch/Tools/Installer")]
        static void Open()
        {
            _instance = GetWindow<CodeSketch_InstallerWindow>("CodeSketch Installer");
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
            RefreshPackageState();
        }

        void OnDisable()
        {
            ResetRequests();
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

            DrawRequiredPackagesSection();

            GUILayout.Space(12);
            GUILayout.Label("Features", EditorStyles.boldLabel);

            if (_settings.Features == null || _settings.Features.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No features defined in Installer Settings.",
                    MessageType.Info
                );
            }
            else
            {
                foreach (var feature in _settings.Features)
                    DrawFeatureToggle(feature);
            }

            EditorUtility.SetDirty(_settings);
        }

        // =====================================================
        // REQUIRED PACKAGES
        // =====================================================

        void DrawRequiredPackagesSection()
        {
            GUILayout.Space(12);
            GUILayout.Label("Required Packages", EditorStyles.boldLabel);

            if (_settings.RequiredPackages == null || _settings.RequiredPackages.Count == 0)
            {
                EditorGUILayout.HelpBox("No required packages defined.", MessageType.Info);
                return;
            }

            int missing = _settings.RequiredPackages
                .Count(p => !IsInstalled(p));

            EditorGUILayout.LabelField($"Missing packages: {missing}");

            EditorGUI.BeginDisabledGroup(IsBusy() || missing == 0);

            if (GUILayout.Button("Install Missing Required Packages"))
                StartInstallRequiredPackages();

            EditorGUI.EndDisabledGroup();
        }

        void StartInstallRequiredPackages()
        {
            _requiredInstallQueue.Clear();
            _ensuredRegistries.Clear();

            foreach (var pkg in _settings.RequiredPackages)
            {
                if (string.IsNullOrEmpty(pkg.PackageName))
                    continue;

                if (IsInstalled(pkg))
                    continue;

                _requiredInstallQueue.Enqueue(pkg);
            }

            InstallNextRequiredPackage();
        }

        void InstallNextRequiredPackage()
        {
            if (_requiredInstallQueue.Count == 0)
            {
                ScheduleResolveIfNeeded();
                return;
            }

            var pkg = _requiredInstallQueue.Dequeue();
            EnsureRegistryIfNeeded(pkg);

            if (pkg.IsDependency)
            {
                CodeSketch_ManifestUtility.EnsureDependency(
                    pkg.PackageName,
                    pkg.Version
                );

                MarkResolveNeeded();
                InstallNextRequiredPackage();
                return;
            }

            _addRequest = Client.Add(pkg.GetInstallString());
            EditorApplication.update += OnRequiredAddProgress;
        }

        void OnRequiredAddProgress()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnRequiredAddProgress;
            _addRequest = null;

            InstallNextRequiredPackage();
        }

        // =====================================================
        // FEATURES
        // =====================================================

        void DrawFeatureToggle(InstallerFeatureDefinition feature)
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

        bool GetFeatureState(InstallerFeatureDefinition feature)
        {
            if (feature.HasDefines)
                return feature.DefineSymbols.All(CodeSketch_DefineSymbolUtility.HasDefine);

            return false;
        }

        void ApplyFeature(InstallerFeatureDefinition feature, bool enable)
        {
            if (feature.HasDefines)
            {
                CodeSketch_DefineSymbolUtility.SetDefines(
                    feature.DefineSymbols,
                    enable
                );
            }

            if (!feature.HasPackages)
            {
                Repaint();
                return;
            }

            if (enable)
                StartInstallFeaturePackages(feature);
            else
                StartRemoveFeaturePackages(feature);

            Repaint();
        }

        void StartInstallFeaturePackages(InstallerFeatureDefinition feature)
        {
            _featureInstallQueue.Clear();
            _ensuredRegistries.Clear();

            foreach (var pkg in feature.Packages)
            {
                if (string.IsNullOrEmpty(pkg.PackageName))
                    continue;

                if (IsInstalled(pkg))
                    continue;

                _featureInstallQueue.Enqueue(pkg);
            }

            InstallNextFeaturePackage();
        }

        void InstallNextFeaturePackage()
        {
            if (_featureInstallQueue.Count == 0)
            {
                ScheduleResolveIfNeeded();
                return;
            }

            var pkg = _featureInstallQueue.Dequeue();
            EnsureRegistryIfNeeded(pkg);

            if (pkg.IsDependency)
            {
                CodeSketch_ManifestUtility.EnsureDependency(
                    pkg.PackageName,
                    pkg.Version
                );

                MarkResolveNeeded();
                InstallNextFeaturePackage();
                return;
            }

            _addRequest = Client.Add(pkg.GetInstallString());
            EditorApplication.update += OnFeatureAddProgress;
        }

        void OnFeatureAddProgress()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnFeatureAddProgress;
            _addRequest = null;

            InstallNextFeaturePackage();
        }

        void StartRemoveFeaturePackages(InstallerFeatureDefinition feature)
        {
            _featureRemoveQueue.Clear();

            foreach (var pkg in feature.Packages)
            {
                if (string.IsNullOrEmpty(pkg.PackageName))
                    continue;

                if (!IsInstalled(pkg))
                    continue;

                _featureRemoveQueue.Enqueue(pkg);
            }

            RemoveNextFeaturePackage();
        }

        void RemoveNextFeaturePackage()
        {
            if (_featureRemoveQueue.Count == 0)
            {
                ScheduleResolveIfNeeded();
                return;
            }

            var pkg = _featureRemoveQueue.Dequeue();

            if (pkg.IsDependency)
            {
                CodeSketch_ManifestUtility.RemoveDependency(pkg.PackageName);
                MarkResolveNeeded();
                RemoveNextFeaturePackage();
                return;
            }

            _removeRequest = Client.Remove(pkg.PackageName);
            EditorApplication.update += OnFeatureRemoveProgress;
        }

        void OnFeatureRemoveProgress()
        {
            if (_removeRequest == null || !_removeRequest.IsCompleted)
                return;

            EditorApplication.update -= OnFeatureRemoveProgress;
            _removeRequest = null;

            RemoveNextFeaturePackage();
        }

        // =====================================================
        // RESOLVE + FRAMEWORK INSTALL
        // =====================================================

        void MarkResolveNeeded()
        {
            _needResolve = true;
        }

        void ScheduleResolveIfNeeded()
        {
            if (!_needResolve || _resolveScheduled)
            {
                TryInstallFramework();
                RefreshPackageState();
                return;
            }

            _resolveScheduled = true;

            EditorApplication.delayCall += () =>
            {
                _resolveScheduled = false;
                _needResolve = false;

                Client.Resolve();

                EditorApplication.delayCall += () =>
                {
                    TryInstallFramework();
                    RefreshPackageState();
                };
            };
        }

        void TryInstallFramework()
        {
            if (_frameworkInstalled)
                return;

            if (IsInstalled(CODESKETCH))
            {
                _frameworkInstalled = true;
                return;
            }

            if (_addRequest != null || _removeRequest != null)
                return;

            Debug.Log("[Installer] Installing CodeSketch Framework...");

            _frameworkInstalled = true;
            _addRequest = Client.Add(CODESKETCH.GitUrl);
            EditorApplication.update += OnFrameworkAddProgress;
        }

        void OnFrameworkAddProgress()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnFrameworkAddProgress;
            _addRequest = null;

            Debug.Log("[Installer] CodeSketch Framework installed");
            RefreshPackageState();
        }

        // =====================================================
        // REGISTRY
        // =====================================================

        void EnsureRegistryIfNeeded(UPMInstallEntry pkg)
        {
            if (pkg.InstallType != UPMPackageInstallType.ScopedRegistry)
                return;

            string key =
                $"{pkg.RegistryUrl}|{string.Join(",", pkg.RegistryScopes ?? new string[0])}";

            if (_ensuredRegistries.Contains(key))
                return;

            CodeSketch_ManifestUtility.EnsureScopedRegistry(
                pkg.RegistryName,
                pkg.RegistryUrl,
                pkg.RegistryScopes
            );

            _ensuredRegistries.Add(key);
            AssetDatabase.Refresh();
        }

        // =====================================================
        // PACKAGE STATE
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

        bool IsInstalled(UPMInstallEntry pkg)
        {
            if (pkg.IsDependency)
                return CodeSketch_ManifestUtility.HasDependency(pkg.PackageName);

            return _installedPackages.Contains(pkg.PackageName);
        }

        // =====================================================
        // UTIL
        // =====================================================

        bool IsBusy()
        {
            return _addRequest != null
                || _removeRequest != null
                || _listRequest != null
                || _requiredInstallQueue.Count > 0
                || _featureInstallQueue.Count > 0
                || _featureRemoveQueue.Count > 0
                || _resolveScheduled;
        }

        void ResetRequests()
        {
            EditorApplication.update -= OnRequiredAddProgress;
            EditorApplication.update -= OnFeatureAddProgress;
            EditorApplication.update -= OnFeatureRemoveProgress;
            EditorApplication.update -= OnFrameworkAddProgress;
            EditorApplication.update -= OnListProgress;

            _addRequest = null;
            _removeRequest = null;
            _listRequest = null;

            _requiredInstallQueue.Clear();
            _featureInstallQueue.Clear();
            _featureRemoveQueue.Clear();
            _ensuredRegistries.Clear();

            _needResolve = false;
            _resolveScheduled = false;
            _frameworkInstalled = false;
        }

        // =====================================================
        // SETTINGS
        // =====================================================

        static CodeSketchInstallerSettings LoadOrCreateSettings()
        {
            var settings =
                Resources.Load<CodeSketchInstallerSettings>(SETTINGS_RESOURCE_PATH);

            if (settings != null)
                return settings;

            settings = CreateInstance<CodeSketchInstallerSettings>();

            var folder = "Assets/CodeSketch.Installer/Editor/Resources";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets/CodeSketch.Installer", "Editor");
                AssetDatabase.CreateFolder("Assets/CodeSketch.Installer/Editor", "Resources");
            }

            AssetDatabase.CreateAsset(settings, SETTINGS_ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return settings;
        }
    }
}
#endif
