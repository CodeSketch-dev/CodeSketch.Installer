#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using CodeSketch.Installer.Runtime;

namespace CodeSketch.Installer.Editor
{
    public class CodeSketch_InstallerWindow : EditorWindow
    {
        static CodeSketch_InstallerWindow _instance;

        CodeSketchInstallerSettings _settings;

        const string SETTINGS_RESOURCE_PATH = "CodeSketchInstallerSettings";
        const string SETTINGS_ASSET_PATH =
            "Assets/CodeSketch.Installer/Editor/Resources/CodeSketchInstallerSettings.asset";

        const string NUGET_UNITYPACKAGE_PATH =
            "Assets/CodeSketch.Installer/Packages/NuGetForUnity.4.5.0.unitypackage";

        AddRequest _addRequest;
        RemoveRequest _removeRequest;
        ListRequest _listRequest;

        readonly HashSet<string> _installedPackages = new();

        readonly Queue<UPMInstallEntry> _requiredInstallQueue = new();
        readonly Queue<UPMInstallEntry> _featureInstallQueue = new();
        readonly Queue<UPMInstallEntry> _featureRemoveQueue = new();

        readonly HashSet<string> _ensuredRegistries = new();

        bool _needResolve;
        bool _resolveScheduled;
        bool _nugetAutoInstallChecked;

        // ================= BUSY OVERLAY =================
        bool _showBusyOverlay;
        string _busyMessage = "Working...";

        // ================= FRAMEWORK =================
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
        public static void Open()
        {
            _instance = GetWindow<CodeSketch_InstallerWindow>("CodeSketch Installer");
        }

        [InitializeOnLoadMethod]
        static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += () =>
            {
                if (_instance == null) return;
                _instance.ResetRequests();
                _instance.RefreshPackageState();
                _instance.Repaint();
            };
        }

        void OnEnable()
        {
            _instance = this;
            ResetRequests();
            _settings = LoadOrCreateSettings();
            RefreshPackageState();

            EditorApplication.delayCall += TryAutoInstallNuGetForUnity;
        }

        void OnDisable()
        {
            ResetRequests();
            EditorApplication.delayCall -= TryAutoInstallNuGetForUnity;
        }

        // =====================================================
        // GUI
        // =====================================================

        void OnGUI()
        {
            if (_settings == null)
                return;

            DrawHeader();
            DrawRequiredPackagesSection();
            DrawFrameworkSection();
            DrawFeaturesSection();

            EditorUtility.SetDirty(_settings);

            DrawBusyOverlay();
        }

        void DrawHeader()
        {
            GUILayout.Label("CodeSketch Installer", EditorStyles.boldLabel);
            GUILayout.Space(4);

            _settings.AlwaysShowOnStartup =
                EditorGUILayout.Toggle("Always show on startup", _settings.AlwaysShowOnStartup);

            GUILayout.Space(8);
        }

        // =====================================================
        // REQUIRED PACKAGES
        // =====================================================

        void DrawRequiredPackagesSection()
        {
            GUILayout.Label("Required Packages", EditorStyles.boldLabel);

            if (_settings.RequiredPackages == null || _settings.RequiredPackages.Count == 0)
            {
                EditorGUILayout.HelpBox("No required packages defined.", MessageType.Info);
                return;
            }

            int missing = _settings.RequiredPackages.Count(p => !IsInstalled(p));
            EditorGUILayout.LabelField($"Missing packages: {missing}");

            EditorGUI.BeginDisabledGroup(IsBusy() || missing == 0);
            if (GUILayout.Button("Install Missing Required Packages"))
                StartInstallRequiredPackages();
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10);
        }

        // =====================================================
        // FRAMEWORK
        // =====================================================

        void DrawFrameworkSection()
        {
            GUILayout.Label("CodeSketch Framework", EditorStyles.boldLabel);

            bool installed = IsInstalled(CODESKETCH);

            EditorGUI.BeginDisabledGroup(installed || IsBusy());
            if (GUILayout.Button(
                    installed ? "CodeSketch Installed" : "Install CodeSketch Framework",
                    GUILayout.Height(26)))
            {
                InstallCodeSketchFramework();
            }
            EditorGUI.EndDisabledGroup();

            if (installed)
            {
                EditorGUILayout.HelpBox(
                    "CodeSketch framework is already installed.",
                    MessageType.Info);
            }

            GUILayout.Space(12);
        }

        void InstallCodeSketchFramework()
        {
            if (IsInstalled(CODESKETCH))
                return;

            BeginBusy("Installing CodeSketch Framework...");

            _addRequest = Client.Add(CODESKETCH.GitUrl);
            EditorApplication.update += OnFrameworkAddProgress;
        }

        void OnFrameworkAddProgress()
        {
            if (_addRequest == null || !_addRequest.IsCompleted)
                return;

            EditorApplication.update -= OnFrameworkAddProgress;
            _addRequest = null;

            EndBusy();
            RefreshPackageState();
        }

        // =====================================================
        // FEATURES
        // =====================================================

        void DrawFeaturesSection()
        {
            GUILayout.Label("Features", EditorStyles.boldLabel);

            if (_settings.Features == null || _settings.Features.Count == 0)
            {
                EditorGUILayout.HelpBox("No features defined.", MessageType.Info);
                return;
            }

            foreach (var feature in _settings.Features)
                DrawFeature(feature);
        }

        void DrawFeature(InstallerFeatureDefinitionAsset feature)
        {
            EditorGUI.BeginDisabledGroup(IsBusy());

            switch (feature.Mode)
            {
                case InstallerFeatureMode.Toggle:
                    DrawToggleFeature(feature.Label, feature.Toggle);
                    break;

                case InstallerFeatureMode.Options:
                    DrawOptionsFeature(feature.Label, feature.Options);
                    break;
            }

            EditorGUI.EndDisabledGroup();
        }

        void DrawToggleFeature(string label, InstallerFeatureToggleAsset toggle)
        {
            if (toggle == null)
                return;

            bool current =
                toggle.HasDefines &&
                toggle.DefineSymbols.All(CodeSketch_DefineSymbolUtility.HasDefine);

            bool next = EditorGUILayout.Toggle(label, current);
            if (next == current)
                return;

            if (toggle.HasDefines)
                CodeSketch_DefineSymbolUtility.SetDefines(toggle.DefineSymbols, next);

            if (!toggle.HasPackages)
                return;

            BeginBusy(next ? "Installing packages..." : "Removing packages...");
            if (next)
                StartInstallPackages(toggle.Packages);
            else
                StartRemovePackages(toggle.Packages);
        }

        void DrawOptionsFeature(string label, InstallerFeatureOptionsAsset options)
        {
            if (options == null || !options.HasOptions)
                return;

            int currentIndex = GetCurrentOptionIndex(options);
            string[] labels = options.Options.Select(o => o.Label).ToArray();

            int newIndex = EditorGUILayout.Popup(label, currentIndex, labels);
            if (newIndex == currentIndex)
                return;

            BeginBusy("Switching feature option...");
            ApplyOption(options, currentIndex, newIndex);
        }

        int GetCurrentOptionIndex(InstallerFeatureOptionsAsset asset)
        {
            for (int i = 0; i < asset.Options.Length; i++)
            {
                var opt = asset.Options[i];
                if (opt.HasDefine &&
                    CodeSketch_DefineSymbolUtility.HasDefine(opt.DefineSymbol))
                    return i;
            }

            return Mathf.Clamp(asset.DefaultOptionIndex, 0, asset.Options.Length - 1);
        }

        void ApplyOption(InstallerFeatureOptionsAsset asset, int oldIndex, int newIndex)
        {
            var oldOpt = asset.Options[oldIndex];
            var newOpt = asset.Options[newIndex];

            if (oldOpt.HasDefine)
                CodeSketch_DefineSymbolUtility.RemoveDefine(oldOpt.DefineSymbol);

            if (oldOpt.HasPackages)
                StartRemovePackages(oldOpt.Packages);

            if (newOpt.HasDefine)
                CodeSketch_DefineSymbolUtility.AddDefine(newOpt.DefineSymbol);

            if (newOpt.HasPackages)
                StartInstallPackages(newOpt.Packages);
        }

        // =====================================================
        // PACKAGE FLOW
        // =====================================================

        void StartInstallPackages(List<UPMInstallEntry> packages)
        {
            _featureInstallQueue.Clear();
            foreach (var p in packages)
                _featureInstallQueue.Enqueue(p);

            InstallNextFeaturePackage();
        }

        void StartRemovePackages(List<UPMInstallEntry> packages)
        {
            _featureRemoveQueue.Clear();
            foreach (var p in packages)
                _featureRemoveQueue.Enqueue(p);

            RemoveNextFeaturePackage();
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
                CodeSketch_ManifestUtility.EnsureDependency(pkg.PackageName, pkg.Version);
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
        // BUSY OVERLAY
        // =====================================================

        void BeginBusy(string message)
        {
            _busyMessage = message;
            _showBusyOverlay = true;
            Repaint();
        }

        void EndBusy()
        {
            _showBusyOverlay = false;
            Repaint();
        }

        void DrawBusyOverlay()
        {
            if (!_showBusyOverlay)
                return;

            var rect = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.35f));

            GUILayout.BeginArea(rect);
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(_busyMessage, EditorStyles.boldLabel);
            GUILayout.Label("Please wait...", EditorStyles.miniLabel);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndArea();
        }

        // =====================================================
        // UTIL
        // =====================================================

        bool IsInstalled(UPMInstallEntry pkg)
        {
            if (pkg.IsDependency)
                return CodeSketch_ManifestUtility.HasDependency(pkg.PackageName);

            return _installedPackages.Contains(pkg.PackageName);
        }

        bool IsBusy()
        {
            return _showBusyOverlay
                   || _addRequest != null
                   || _removeRequest != null
                   || _listRequest != null;
        }

        void MarkResolveNeeded() => _needResolve = true;

        void ScheduleResolveIfNeeded()
        {
            if (!_needResolve || _resolveScheduled)
            {
                EndBusy();
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
                    EndBusy();
                    RefreshPackageState();
                };
            };
        }

        void EnsureRegistryIfNeeded(UPMInstallEntry pkg)
        {
            if (pkg.InstallType != UPMPackageInstallType.ScopedRegistry)
                return;

            string key = $"{pkg.RegistryUrl}|{string.Join(",", pkg.RegistryScopes ?? new string[0])}";
            if (_ensuredRegistries.Contains(key))
                return;

            CodeSketch_ManifestUtility.EnsureScopedRegistry(
                pkg.RegistryName,
                pkg.RegistryUrl,
                pkg.RegistryScopes);

            _ensuredRegistries.Add(key);
        }

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

        void ResetRequests()
        {
            _addRequest = null;
            _removeRequest = null;
            _listRequest = null;

            _requiredInstallQueue.Clear();
            _featureInstallQueue.Clear();
            _featureRemoveQueue.Clear();
            _ensuredRegistries.Clear();

            _needResolve = false;
            _resolveScheduled = false;
            _showBusyOverlay = false;
        }

        // =====================================================
        // SETTINGS
        // =====================================================

        static CodeSketchInstallerSettings LoadOrCreateSettings()
        {
            var settings = Resources.Load<CodeSketchInstallerSettings>(SETTINGS_RESOURCE_PATH);
            if (settings != null)
                return settings;

            settings = CreateInstance<CodeSketchInstallerSettings>();

            if (!AssetDatabase.IsValidFolder("Assets/CodeSketch.Installer/Editor/Resources"))
            {
                AssetDatabase.CreateFolder("Assets/CodeSketch.Installer", "Editor");
                AssetDatabase.CreateFolder("Assets/CodeSketch.Installer/Editor", "Resources");
            }

            AssetDatabase.CreateAsset(settings, SETTINGS_ASSET_PATH);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }

        // =====================================================
        // NUGET
        // =====================================================

        bool IsNuGetForUnityInstalled()
        {
            return Type.GetType(
                "NugetForUnity.NugetForUnityEditor, NuGetForUnity"
            ) != null;
        }

        void TryAutoInstallNuGetForUnity()
        {
            if (_nugetAutoInstallChecked)
                return;

            _nugetAutoInstallChecked = true;

            if (EditorApplication.isCompiling)
                return;

            if (IsNuGetForUnityInstalled())
                return;

            BeginBusy("Installing NuGetForUnity...");
            AssetDatabase.ImportPackage(NUGET_UNITYPACKAGE_PATH, false);

            EditorApplication.delayCall += () =>
            {
                EndBusy();
                AssetDatabase.Refresh();
            };
        }
        
        void StartInstallRequiredPackages()
        {
            if (IsBusy())
                return;

            BeginBusy("Installing required packages...");

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
    }
}
#endif
