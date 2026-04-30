#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using System.Reflection;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using CodeSketch.Installer.Runtime;

namespace CodeSketch.Installer.Editor
{
    public class CodeSketchInstallerWindow : EditorWindow
    {
        static CodeSketchInstallerWindow _instance;
        int _tabIndex = 0;
        readonly string[] _tabLabels = { "Packages", "Features", "ThirdParty" };
        int _thirdPartySelected = -1;
        Vector2 _packagesScroll = Vector2.zero;
        Dictionary<string, bool> _selection = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        CodeSketchInstallerSettings _settings;

        const string SETTINGS_RESOURCE_PATH = "CodeSketchInstallerSettings";
        const string SETTINGS_ASSET_PATH =
            "Assets/CodeSketch.Installer/Editor/Resources/CodeSketchInstallerSettings.asset";

        AddRequest _addRequest;
        RemoveRequest _removeRequest;
        ListRequest _listRequest;

        readonly HashSet<string> _installedPackages = new();

        readonly Queue<UPMInstallEntry> _requiredInstallQueue = new();
        readonly Queue<UPMInstallEntry> _featureInstallQueue = new();
        readonly Queue<UPMInstallEntry> _featureRemoveQueue = new();

        readonly HashSet<string> _ensuredRegistries = new();

        readonly List<UPMInstallEntry> _missingRequiredPackages = new();

        bool _needResolve;
        bool _resolveScheduled;

        // ================= BUSY OVERLAY =================
        bool _showBusyOverlay;
        string _busyMessage = "Working...";

        // ================= FRAMEWORK (HARDCODE) =================
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
            _instance = GetWindow<CodeSketchInstallerWindow>("CodeSketch Installer");
        }

        static string TryGetMappingInstalledPath(string name)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    var t = asm.GetType("CodeSketch.Installer.Editor.CodeSketchPackageMap");
                    if (t != null)
                    {
                        var mi = t.GetMethod("GetMapping", BindingFlags.Public | BindingFlags.Static);
                        if (mi != null)
                        {
                            var mapObj = mi.Invoke(null, new object[] { name });
                            if (mapObj != null)
                            {
                                var f = mapObj.GetType().GetField("InstalledPath");
                                if (f != null)
                                    return f.GetValue(mapObj) as string;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        static void TryRemoveMapping(string name)
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    var t = asm.GetType("CodeSketch.Installer.Editor.CodeSketchPackageMap");
                    if (t != null)
                    {
                        var mi = t.GetMethod("RemoveMapping", BindingFlags.Public | BindingFlags.Static);
                        if (mi != null)
                        {
                            mi.Invoke(null, new object[] { name });
                        }
                        return;
                    }
                }
            }
            catch { }
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

            // ensure essential packages are installed (in-order top-down)
            EditorApplication.delayCall += () =>
            {
                try
                {
                    if (IsBusy())
                        return;

                    var toInstall = new List<UPMInstallEntry>();

                    var requiredList = Resources.Load<CodeSketch.Installer.Runtime.RequiredPackageList>("RequiredPackageList");
                    var optionalList = Resources.Load<CodeSketch.Installer.Runtime.OptionalPackageList>("OptionalPackageList");

                    // helper to append essentials in order from a ScriptableObject list
                    void AppendEssentials(IEnumerable<ScriptableObject> list)
                    {
                        if (list == null) return;
                        foreach (var obj in list)
                        {
                            if (obj == null) continue;
                            var ia = obj as IUPMPackageAsset;
                            if (ia == null) continue;
                            if (!ia.IsEssential) continue;
                            var e = ia.ToEntry();
                            if (!IsInstalled(e)) toInstall.Add(e);
                        }
                    }

                    // order: RequiredPackageList, OptionalPackageList, legacy settings.RequiredPackages
                    if (requiredList != null) AppendEssentials(requiredList.Packages);
                    if (optionalList != null) AppendEssentials(optionalList.Packages);

                    if (_settings != null && _settings.RequiredPackages != null)
                        AppendEssentials(_settings.RequiredPackages.Cast<ScriptableObject>());

                    if (toInstall.Count > 0)
                    {
                        StartInstallPackages(toInstall);
                    }
                }
                catch { }
            };
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

            DrawHeader();

            _tabIndex = GUILayout.Toolbar(_tabIndex, _tabLabels);

            switch (_tabIndex)
            {
                case 0:
                    DrawRequiredPackagesSection();
                    DrawFrameworkSection();
                    break;
                case 1:
                    DrawFeaturesSection();
                    break;
                case 2:
                    DrawThirdPartySection();
                    break;
            }

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

        void RefreshMissingRequiredPackages()
        {
            _missingRequiredPackages.Clear();

            if (_settings.RequiredPackages == null)
                return;

            foreach (var asset in _settings.RequiredPackages)
            {
                if (asset == null)
                    continue;

                var entry = asset.ToEntry();

                if (string.IsNullOrEmpty(entry.PackageName))
                    continue;

                if (IsInstalled(entry))
                    continue;

                _missingRequiredPackages.Add(entry);
            }
        }

        void DrawRequiredPackagesSection()
        {
            GUILayout.Label("Packages", EditorStyles.boldLabel);

            // load lists (new ScriptableObjects)
            var requiredList = Resources.Load<CodeSketch.Installer.Runtime.RequiredPackageList>("RequiredPackageList");
            var optionalList = Resources.Load<CodeSketch.Installer.Runtime.OptionalPackageList>("OptionalPackageList");

            // fallback to old settings.RequiredPackages if present
            var combined = new List<ScriptableObject>();
            if (requiredList != null && requiredList.Packages != null) combined.AddRange(requiredList.Packages);
            if (optionalList != null && optionalList.Packages != null) combined.AddRange(optionalList.Packages);
            if ((_settings.RequiredPackages != null) && _settings.RequiredPackages.Count > 0)
                combined.AddRange(_settings.RequiredPackages.Cast<ScriptableObject>());

            if (combined.Count == 0)
            {
                EditorGUILayout.HelpBox("No packages defined. Create RequiredPackageList/OptionalPackageList assets.", MessageType.Info);
                GUILayout.Space(10);
                return;
            }

            // scrollable list with checkboxes
            _packagesScroll = EditorGUILayout.BeginScrollView(_packagesScroll, GUILayout.Height(220));
            for (int i = 0; i < combined.Count; i++)
            {
                var aObj = combined[i];
                if (aObj == null) continue;

                string key = aObj.GetInstanceID().ToString();
                IUPMPackageAsset ia = aObj as IUPMPackageAsset;
                bool isEssential = false;
                if (ia != null)
                    isEssential = ia.IsEssential || (requiredList != null && requiredList.Packages.Contains(aObj)) || (_settings.RequiredPackages != null && _settings.RequiredPackages.Any(x => x == aObj));
                bool current = false;
                _selection.TryGetValue(key, out current);

                EditorGUILayout.BeginHorizontal();
                bool next;
                // determine initial toggle state: if we've stored a value use it, otherwise default essentials=true, optional=false
                bool hasValue = _selection.TryGetValue(key, out var stored);
                bool initial = hasValue ? stored : isEssential;
                next = GUILayout.Toggle(initial, "", GUILayout.Width(18));
                if (isEssential)
                    EditorGUILayout.LabelField("(Essential)", GUILayout.Width(80));
                _selection[key] = next;

                // persist change back to asset: if the package asset exposes IsEssential, update it when user toggles
                try
                {
                    if (ia != null && ia.IsEssential != next)
                    {
                        ia.IsEssential = next;
                        var so = aObj as ScriptableObject;
                        if (so != null)
                        {
                            EditorUtility.SetDirty(so);
                            AssetDatabase.SaveAssets();
                        }
                    }
                }
                catch { }

                string label;
                if (ia != null)
                {
                    var temp = ia.ToEntry();
                    label = string.IsNullOrEmpty(temp.Name) ? temp.PackageName : temp.Name + " (" + temp.PackageName + ")";
                }
                else
                {
                    label = aObj.name;
                }
                EditorGUILayout.LabelField(label);

                // installed status
                bool installed = false;
                if (ia != null)
                {
                    var entry = ia.ToEntry();
                    installed = IsInstalled(entry);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(installed ? "Installed" : "Not installed", GUILayout.Width(120));

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(IsBusy());
            if (GUILayout.Button("Install Selected"))
            {
                var toInstall = new List<UPMInstallEntry>();
                foreach (var aObj in combined)
                {
                    if (aObj == null) continue;
                    string key = aObj.GetInstanceID().ToString();
                    bool sel = false;
                    _selection.TryGetValue(key, out sel);
                    bool isEssential = (requiredList != null && requiredList.Packages.Contains(aObj)) || (_settings.RequiredPackages != null && _settings.RequiredPackages.Any(x => x == aObj));
                    if (sel || isEssential)
                    {
                        var ia2 = aObj as IUPMPackageAsset;
                        if (ia2 == null) continue;
                        var e = ia2.ToEntry();
                        if (!IsInstalled(e))
                            toInstall.Add(e);
                    }
                }

                if (toInstall.Count > 0)
                {
                    StartInstallPackages(toInstall);
                }
            }

            if (GUILayout.Button("Uninstall Selected"))
            {
                var toRemove = new List<UPMInstallEntry>();
                var thirdPartyFound = new List<IUPMPackageAsset>();
                foreach (var aObj in combined)
                {
                    if (aObj == null) continue;
                    string key = aObj.GetInstanceID().ToString();
                    bool sel = false;
                    _selection.TryGetValue(key, out sel);
                    if (!sel) continue;

                    var ia2 = aObj as IUPMPackageAsset;
                    if (ia2 == null) continue;
                    var e = ia2.ToEntry();
                    if (e.InstallType == UPMPackageInstallType.UnityPackage)
                    {
                        thirdPartyFound.Add(ia2);
                    }
                    else
                        toRemove.Add(e);
                }

                if (toRemove.Count > 0)
                    StartRemovePackages(toRemove);

                if (thirdPartyFound.Count > 0)
                {
                    // perform aggressive uninstall for each third-party unitypackage asset
                    var allTargets = new List<string>();
                    foreach (var ia in thirdPartyFound)
                    {
                        try
                        {
                            var name = ia.ToEntry().Name;
                            var mappingInstalled = TryGetMappingInstalledPath(name);
                            if (!string.IsNullOrEmpty(mappingInstalled) && !allTargets.Contains(mappingInstalled))
                                allTargets.Add(mappingInstalled);

                            var detected = CodeSketch.Installer.Editor.UnityPackagesUtils.GetInstalledPath(name);
                            if (!string.IsNullOrEmpty(detected) && !allTargets.Contains(detected))
                                allTargets.Add(detected);

                            // search Assets for matching folders
                            try
                            {
                                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                                var assetsRoot = Path.Combine(root, "Assets");
                                var normPackage = System.Text.RegularExpressions.Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", "");
                                var assetDirs = Directory.GetDirectories(assetsRoot, "*", SearchOption.AllDirectories);
                                foreach (var d2 in assetDirs)
                                {
                                    var folder = Path.GetFileName(d2);
                                    if (string.IsNullOrEmpty(folder)) continue;
                                    var normFolder = System.Text.RegularExpressions.Regex.Replace(folder.ToLowerInvariant(), "[^a-z0-9]", "");
                                    if (!normFolder.Contains(normPackage)) continue;
                                    if (!allTargets.Contains(d2)) allTargets.Add(d2);
                                }
                            }
                            catch { }
                        }
                        catch { }
                    }

                    if (allTargets.Count == 0)
                    {
                        EditorUtility.DisplayDialog("Uninstall", "No installed paths detected for selected third-party items.", "OK");
                    }
                    else
                    {
                        // include parents
                        var parentDirs = new List<string>();
                        foreach (var tt in allTargets)
                        {
                            try
                            {
                                var p = System.IO.Path.GetDirectoryName(tt);
                                if (!string.IsNullOrEmpty(p) && !parentDirs.Contains(p) && !allTargets.Contains(p))
                                    parentDirs.Add(p);
                            }
                            catch { }
                        }

                        allTargets.AddRange(parentDirs);
                        allTargets = allTargets.Distinct().OrderByDescending(s => s.Length).ToList();

                        var message = "The following paths will be deleted (children first, then parents):\n" + string.Join("\n", allTargets);
                        if (EditorUtility.DisplayDialog("Confirm Uninstall", message, "Delete", "Cancel"))
                        {
                            BeginBusy("Uninstalling...");
                            foreach (var t in allTargets)
                            {
                                try
                                {
                                    if (string.IsNullOrEmpty(t)) continue;
                                    var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                                    if (t.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var rel = t.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace("\\", "/");
                                        if (!rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) rel = "Assets/" + rel;
                                        AssetDatabase.DeleteAsset(rel);
                                    }
                                    else
                                    {
                                        if (Directory.Exists(t)) Directory.Delete(t, true);
                                        else if (File.Exists(t)) File.Delete(t);

                                        try
                                        {
                                            var di = new System.IO.DirectoryInfo(t);
                                            while (di != null && !di.Name.StartsWith("com.")) di = di.Parent;
                                            if (di != null && di.FullName.IndexOf("PackageCache", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                if (di.Exists) Directory.Delete(di.FullName, true);
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    EditorUtility.DisplayDialog("Uninstall Failed", ex.Message, "OK");
                                }
                            }

                            // remove mappings for selected
                            foreach (var ia in thirdPartyFound)
                            {
                                try { TryRemoveMapping(ia.ToEntry().Name); } catch { }
                            }

                            AssetDatabase.Refresh();
                            RefreshPackageState();
                            Repaint();
                            EndBusy();
                        }
                    }
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
        }

        void StartInstallRequiredPackages()
        {
            if (IsBusy())
                return;

            RefreshMissingRequiredPackages();

            if (_missingRequiredPackages.Count == 0)
                return;

            BeginBusy("Installing required packages...");

            _requiredInstallQueue.Clear();
            _ensuredRegistries.Clear();

            foreach (var entry in _missingRequiredPackages)
                _requiredInstallQueue.Enqueue(entry);

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
                CodeSketch_ManifestUtility.EnsureDependency(pkg.PackageName, pkg.Version);
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
        // FEATURES (GIỮ NGUYÊN)
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

        // =====================================================
        // THIRD PARTY
        // =====================================================

        void DrawThirdPartySection()
        {
            GUILayout.Label("Third-Party Packages", EditorStyles.boldLabel);

            var found = CodeSketch.Installer.Editor.UnityPackagesUtils.FindUnityPackagesInRepo();

            if (found == null || found.Count == 0)
            {
                EditorGUILayout.HelpBox("No unitypackage files found in installer or repo.", MessageType.Info);
                return;
            }





            GUILayout.BeginVertical(EditorStyles.helpBox);

            // selectable list with single selection and a detail area below
            for (int i = 0; i < found.Count; i++)
            {
                var p = found[i];
                GUILayout.BeginHorizontal();
                bool selected = (_thirdPartySelected == i);
                if (GUILayout.Toggle(selected, $"{p.Name} {p.Version}", "Button", GUILayout.ExpandWidth(false), GUILayout.Width(300)))
                {
                    _thirdPartySelected = i;
                }
                else
                {
                    if (selected)
                        _thirdPartySelected = -1;
                }

                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(p.InstalledVersion))
                    GUILayout.Label($"Installed: {p.InstalledVersion}", GUILayout.Width(140));
                else
                    GUILayout.Label("Installed: -", GUILayout.Width(140));

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.Space(6);

            // details for selected
            if (_thirdPartySelected >= 0 && _thirdPartySelected < found.Count)
            {
                var sel = found[_thirdPartySelected];
                EditorGUILayout.LabelField("Selected:", sel.Name + " " + sel.Version);
                EditorGUILayout.LabelField("Path:", sel.FilePath);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(IsBusy());

                bool hasInstalled = !string.IsNullOrEmpty(sel.InstalledVersion) || !string.IsNullOrEmpty(sel.InstalledPath);
                bool canUpdate = false;
                if (!string.IsNullOrEmpty(sel.Version))
                {
                    if (!string.IsNullOrEmpty(sel.InstalledVersion) && sel.InstalledVersion != "installed")
                        canUpdate = CodeSketch.Installer.Editor.UnityPackagesUtils.CompareVersionGreater(sel.Version, sel.InstalledVersion);
                    else if (sel.InstalledVersion == "installed")
                        canUpdate = true; // unknown installed version but package exists -> allow update
                }

                if (!hasInstalled)
                {
                    if (GUILayout.Button("Install", GUILayout.Width(100)))
                    {
                        BeginBusy($"Importing {sel.Name}...");
                        CodeSketch.Installer.Editor.UnityPackagesUtils.ImportUnityPackage(sel.FilePath, sel.Name);
                        EditorApplication.delayCall += () =>
                        {
                            AssetDatabase.Refresh();
                            EndBusy();
                            RefreshPackageState();
                            Repaint();
                        };
                    }
                }
                else
                {
                    if (canUpdate)
                    {
                        if (GUILayout.Button("Update", GUILayout.Width(100)))
                        {
                            BeginBusy($"Updating {sel.Name}...");
                            CodeSketch.Installer.Editor.UnityPackagesUtils.ImportUnityPackage(sel.FilePath, sel.Name);
                            EditorApplication.delayCall += () =>
                            {
                                AssetDatabase.Refresh();
                                EndBusy();
                                RefreshPackageState();
                                Repaint();
                            };
                        }
                    }

                    if (GUILayout.Button("Uninstall", GUILayout.Width(100)))
                    {
                        // Aggressive uninstall: use stored mapping if available, otherwise fall back to detected InstalledPath
                        var mappingInstalledPath = TryGetMappingInstalledPath(sel.Name);
                        var targets = new List<string>();

                        if (!string.IsNullOrEmpty(mappingInstalledPath))
                            targets.Add(mappingInstalledPath);

                        if (!string.IsNullOrEmpty(sel.InstalledPath) && !targets.Contains(sel.InstalledPath))
                            targets.Add(sel.InstalledPath);

                        // also search Assets for any folders matching normalized package name
                        try
                        {
                            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                            var assetsRoot = Path.Combine(root, "Assets");
                            var normPackage = System.Text.RegularExpressions.Regex.Replace(sel.Name.ToLowerInvariant(), "[^a-z0-9]", "");
                            var assetDirs = Directory.GetDirectories(assetsRoot, "*", SearchOption.AllDirectories);
                            foreach (var d2 in assetDirs)
                            {
                                var folder = Path.GetFileName(d2);
                                if (string.IsNullOrEmpty(folder)) continue;
                                var normFolder = System.Text.RegularExpressions.Regex.Replace(folder.ToLowerInvariant(), "[^a-z0-9]", "");
                                if (!normFolder.Contains(normPackage)) continue;
                                if (!targets.Contains(d2)) targets.Add(d2);
                            }
                        }
                        catch { }

                        if (targets.Count == 0)
                        {
                            var installedInfo = string.IsNullOrEmpty(sel.InstalledVersion) ? "not detected" : sel.InstalledVersion;
                            EditorUtility.DisplayDialog("Uninstall", $"No installed paths detected for {sel.Name}. Detected: {installedInfo}", "OK");
                        }
                        else
                        {
                            // include immediate parent folders (e.g. Demigiant) in deletion list
                            var parentDirs = new List<string>();
                            foreach (var tt in targets)
                            {
                                try
                                {
                                    var p = System.IO.Path.GetDirectoryName(tt);
                                    if (!string.IsNullOrEmpty(p) && !parentDirs.Contains(p) && !targets.Contains(p))
                                        parentDirs.Add(p);
                                }
                                catch { }
                            }

                            // merge parents into targets so they get deleted after children
                            targets.AddRange(parentDirs);

                            // ensure unique and delete children before parents
                            targets = targets.Distinct().OrderByDescending(s => s.Length).ToList();

                            var message = "The following paths will be deleted (children first, then parents):\n" + string.Join("\n", targets);
                            if (!EditorUtility.DisplayDialog("Confirm Uninstall", message, "Delete", "Cancel"))
                                return;

                            foreach (var t in targets)
                            {
                                try
                                {
                                    if (string.IsNullOrEmpty(t)) continue;
                                    var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                                    if (t.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var rel = t.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace("\\", "/");
                                        if (!rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) rel = "Assets/" + rel;
                                        AssetDatabase.DeleteAsset(rel);
                                    }
                                    else
                                    {
                                        // delete filesystem path
                                        if (Directory.Exists(t)) Directory.Delete(t, true);
                                        else if (File.Exists(t)) File.Delete(t);

                                        // if this was inside PackageCache, try to remove the top-level package folder (com.*)
                                        try
                                        {
                                            var di = new System.IO.DirectoryInfo(t);
                                            while (di != null && !di.Name.StartsWith("com.")) di = di.Parent;
                                            if (di != null && di.FullName.IndexOf("PackageCache", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                if (di.Exists) Directory.Delete(di.FullName, true);
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    EditorUtility.DisplayDialog("Uninstall Failed", ex.Message, "OK");
                                }
                            }

                            // remove mapping if exists
                            TryRemoveMapping(sel.Name);

                            AssetDatabase.Refresh();
                            RefreshPackageState();
                            Repaint();
                        }
                    }
                }

                if (GUILayout.Button("Reveal in Explorer", GUILayout.Width(140)))
                {
                    try
                    {
                        EditorUtility.RevealInFinder(sel.FilePath);
                    }
                    catch { }
                }

                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
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

        void StartInstallPackages(List<UPMInstallEntry> entries)
        {
            _featureInstallQueue.Clear();
            foreach (var e in entries)
                _featureInstallQueue.Enqueue(e);

            InstallNextFeaturePackage();
        }

        void StartRemovePackages(List<UPMInstallEntry> entries)
        {
            _featureRemoveQueue.Clear();
            foreach (var e in entries)
                _featureRemoveQueue.Enqueue(e);

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

            string key = $"{pkg.RegistryUrl}|{string.Join(",", pkg.RegistryScopes ?? Array.Empty<string>())}";
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

            if (_listRequest.Result != null)
            {
                foreach (var p in _listRequest.Result)
                    _installedPackages.Add(p.name);
            }

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
    }
}
#endif
