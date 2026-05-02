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
        Vector2 _essentialsScroll = Vector2.zero;
        List<Rect> _essentialsRowRects = new List<Rect>();
        List<Rect> _othersRowRects = new List<Rect>();
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

        // Drag & drop support
        Rect _essentialsRect;
        Rect _packagesRect;

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

                    // Auto-install essentials from settings (legacy migration handled elsewhere)
                    if (_settings != null && _settings.EssentialPackages != null && _settings.EssentialPackages.Count > 0)
                    {
                        foreach (var obj in _settings.EssentialPackages)
                        {
                            if (obj == null) continue;
                            var ia = obj as IUPMPackageAsset;
                            if (ia == null) continue;
                            var e = ia.ToEntry();
                            if (!IsInstalled(e)) toInstall.Add(e);
                        }
                    }

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

            // consider essentials defined in settings and any Resources/Packages assets marked essential
            if (_settings != null && _settings.EssentialPackages != null)
            {
                foreach (var assetObj in _settings.EssentialPackages)
                {
                    if (assetObj == null) continue;
                    var ia = assetObj as IUPMPackageAsset;
                    if (ia == null) continue;
                    var entry = ia.ToEntry();
                    if (string.IsNullOrEmpty(entry.PackageName)) continue;
                    if (IsInstalled(entry)) continue;
                    _missingRequiredPackages.Add(entry);
                }
            }

            try
            {
                var resourcesPackages = Resources.LoadAll<ScriptableObject>("Packages");
                if (resourcesPackages != null && resourcesPackages.Length > 0)
                {
                    foreach (var rp in resourcesPackages)
                    {
                        if (rp == null) continue;
                        var ia = rp as IUPMPackageAsset;
                        if (ia == null) continue;
                        if (!ia.IsEssential) continue;
                        var entry = ia.ToEntry();
                        if (string.IsNullOrEmpty(entry.PackageName)) continue;
                        if (IsInstalled(entry)) continue;
                        // avoid duplicates
                        if (!_missingRequiredPackages.Exists(x => x.PackageName == entry.PackageName))
                            _missingRequiredPackages.Add(entry);
                    }
                }
            }
            catch { }
        }

        void DrawRequiredPackagesSection()
        {
            GUILayout.Label("Packages", EditorStyles.boldLabel);

            // build combined list from settings and any ScriptableObject package assets under Resources/Packages
            var combined = new List<ScriptableObject>();
            if (_settings != null)
            {
                if (_settings.EssentialPackages != null && _settings.EssentialPackages.Count > 0)
                    combined.AddRange(_settings.EssentialPackages.Cast<ScriptableObject>());
                if (_settings.OtherPackages != null && _settings.OtherPackages.Count > 0)
                    combined.AddRange(_settings.OtherPackages.Cast<ScriptableObject>());
            }

            try
            {
                var resourcesPackages = Resources.LoadAll<ScriptableObject>("Packages");
                if (resourcesPackages != null && resourcesPackages.Length > 0)
                {
                    bool settingsChanged = false;
                    foreach (var rp in resourcesPackages)
                    {
                        if (rp == null) continue;
                        if (!combined.Contains(rp))
                            combined.Add(rp);

                        bool inSettings = false;
                        if (_settings != null)
                        {
                            if ((_settings.EssentialPackages != null && _settings.EssentialPackages.Contains(rp)) ||
                                (_settings.OtherPackages != null && _settings.OtherPackages.Contains(rp)))
                                inSettings = true;
                        }

                        var ia = rp as IUPMPackageAsset;
                        bool wantEssential = ia != null && ia.IsEssential;

                        if (!inSettings && _settings != null)
                        {
                            if (wantEssential)
                            {
                                if (_settings.EssentialPackages == null) _settings.EssentialPackages = new List<ScriptableObject>();
                                _settings.EssentialPackages.Add(rp);
                                EditorUtility.SetDirty(_settings);
                                settingsChanged = true;
                            }
                            else
                            {
                                if (_settings.OtherPackages == null) _settings.OtherPackages = new List<ScriptableObject>();
                                _settings.OtherPackages.Add(rp);
                                EditorUtility.SetDirty(_settings);
                                settingsChanged = true;
                            }
                        }
                    }

                    if (settingsChanged)
                        AssetDatabase.SaveAssets();
                }
            }
            catch { }

            if (combined.Count == 0)
            {
                EditorGUILayout.HelpBox("No packages defined. Create package ScriptableObjects under Resources/Packages or add them to CodeSketchInstallerSettings.", MessageType.Info);
                GUILayout.Space(10);
                return;
            }

            // scrollable list with checkboxes
            // split into two scroll lists: Essentials (top) and Packages (bottom)
            var essentials = new List<ScriptableObject>();
            var others = new List<ScriptableObject>();
            for (int i = 0; i < combined.Count; i++)
            {
                var aObj = combined[i];
                if (aObj == null) continue;
                IUPMPackageAsset ia = aObj as IUPMPackageAsset;
                bool isEssential = false;
                if (ia != null)
                {
                    bool settingsHas = false;
                    if (_settings != null)
                    {
                        if ((_settings.EssentialPackages != null && _settings.EssentialPackages.Any(x => x == aObj)))
                            settingsHas = true;
                    }

                    isEssential = ia.IsEssential || settingsHas;
                }

                if (isEssential) essentials.Add(aObj);
                else others.Add(aObj);
            }

            // prepare row rect trackers
            _essentialsRowRects.Clear();

            GUILayout.Label("Essential Packages", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _essentialsScroll = EditorGUILayout.BeginScrollView(_essentialsScroll, GUILayout.Height(120));
            for (int i = 0; i < essentials.Count; i++)
            {
                var aObj = essentials[i];
                if (aObj == null) continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                // selection checkbox
                string key = aObj.GetInstanceID().ToString();
                bool sel = false;
                _selection.TryGetValue(key, out sel);
                bool nextSel = EditorGUILayout.Toggle(sel, GUILayout.Width(18));
                if (nextSel != sel)
                {
                    _selection[key] = nextSel;
                }

                // reorder buttons
                if (GUILayout.Button("▲", GUILayout.Width(18))) MoveItemInSettings(aObj, true, -1);
                if (GUILayout.Button("▼", GUILayout.Width(18))) MoveItemInSettings(aObj, true, +1);

                GUILayout.Label("☰", GUILayout.Width(18));
                EditorGUILayout.LabelField(aObj.name);

                // quick toggle button to move to Other Packages
                if (GUILayout.Button("⇄", GUILayout.Width(24)))
                {
                    ToggleEssential(aObj, false);
                }

                GUILayout.FlexibleSpace();
                var ia = aObj as IUPMPackageAsset;
                bool installed = ia != null && IsInstalled(ia.ToEntry());
                EditorGUILayout.LabelField(installed ? "Installed" : "Not installed", GUILayout.Width(120));
                EditorGUILayout.EndHorizontal();

                // store rect for this row to support drop insertion
                try { _essentialsRowRects.Add(GUILayoutUtility.GetLastRect()); } catch { }

                // start drag when clicking the label area
                var lastRect = GUILayoutUtility.GetLastRect();
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && lastRect.Contains(e.mousePosition))
                {
                    try
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new UnityEngine.Object[] { aObj };
                        DragAndDrop.StartDrag(aObj.name);
                        e.Use();
                    }
                    catch { }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // capture rect for drop handling (normalize to full width and expected height)
            _essentialsRect = GUILayoutUtility.GetLastRect();
            _essentialsRect.x = 0;
            _essentialsRect.width = position.width;
            _essentialsRect.height = 120 + 8;

            GUILayout.Space(6);

            _othersRowRects.Clear();

            GUILayout.Label("Other Packages", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _packagesScroll = EditorGUILayout.BeginScrollView(_packagesScroll, GUILayout.Height(220));
            for (int i = 0; i < others.Count; i++)
            {
                var aObj = others[i];
                if (aObj == null) continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                // selection checkbox
                string key = aObj.GetInstanceID().ToString();
                bool sel = false;
                _selection.TryGetValue(key, out sel);
                bool nextSel = EditorGUILayout.Toggle(sel, GUILayout.Width(18));
                if (nextSel != sel)
                {
                    _selection[key] = nextSel;
                }
                // reorder buttons for OtherPackages
                if (GUILayout.Button("▲", GUILayout.Width(18))) MoveItemInSettings(aObj, false, -1);
                if (GUILayout.Button("▼", GUILayout.Width(18))) MoveItemInSettings(aObj, false, +1);

                GUILayout.Label("☰", GUILayout.Width(18));
                EditorGUILayout.LabelField(aObj.name);

                // quick toggle button to move to Essential Packages
                if (GUILayout.Button("⇄", GUILayout.Width(24)))
                {
                    ToggleEssential(aObj, true);
                }

                GUILayout.FlexibleSpace();
                var ia = aObj as IUPMPackageAsset;
                bool installed = ia != null && IsInstalled(ia.ToEntry());
                EditorGUILayout.LabelField(installed ? "Installed" : "Not installed", GUILayout.Width(120));
                EditorGUILayout.EndHorizontal();

                // store rect for this row to support drop insertion
                try { _othersRowRects.Add(GUILayoutUtility.GetLastRect()); } catch { }

                var lastRect = GUILayoutUtility.GetLastRect();
                var e = Event.current;
                if (e.type == EventType.MouseDown && e.button == 0 && lastRect.Contains(e.mousePosition))
                {
                    try
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new UnityEngine.Object[] { aObj };
                        DragAndDrop.StartDrag(aObj.name);
                        e.Use();
                    }
                    catch { }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // capture rect for drop handling (normalize to full width and expected height)
            _packagesRect = GUILayoutUtility.GetLastRect();
            _packagesRect.x = 0;
            _packagesRect.width = position.width;
            _packagesRect.height = 220 + 8;

            // Handle drag updates / perform between the two panels
            var evt = Event.current;
            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
            {
                var mouse = evt.mousePosition;
                bool overEss = _essentialsRect.Contains(mouse);
                bool overPack = _packagesRect.Contains(mouse);
                if (overEss || overPack)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            var so = obj as ScriptableObject;
                            if (so == null) continue;
                            var ia = so as IUPMPackageAsset;
                            if (ia == null) continue;

                            if (overEss)
                            {
                                int insertIndex = ComputeInsertIndex(_essentialsRowRects, evt.mousePosition.y, _essentialsScroll.y, _essentialsRect.y);
                                MoveItemAcrossLists(so, true, insertIndex);
                            }
                            else if (overPack)
                            {
                                int insertIndex = ComputeInsertIndex(_othersRowRects, evt.mousePosition.y, _packagesScroll.y, _packagesRect.y);
                                MoveItemAcrossLists(so, false, insertIndex);
                            }
                        }
                        AssetDatabase.SaveAssets();
                        RefreshPackageState();
                        Repaint();
                        evt.Use();
                    }
                    else
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    }
                }
            }

            GUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(IsBusy());
            if (GUILayout.Button("Install Selected"))
            {
                var toInstall = new List<UPMInstallEntry>();

                // 1) Essentials first (respect ordering from settings.EssentialPackages if present)
                var orderedEssentials = new List<ScriptableObject>();
                if (_settings != null && _settings.EssentialPackages != null && _settings.EssentialPackages.Count > 0)
                    orderedEssentials.AddRange(_settings.EssentialPackages);
                else // fall back to the visible essentials ordering
                    orderedEssentials.AddRange(essentials);

                foreach (var aObj in orderedEssentials)
                {
                    if (aObj == null) continue;
                    string key = aObj.GetInstanceID().ToString();
                    bool sel = false; _selection.TryGetValue(key, out sel);

                    var ia2 = aObj as IUPMPackageAsset;
                    if (ia2 == null) continue;

                    if (sel || ia2.IsEssential)
                    {
                        var e = ia2.ToEntry();
                        if (!IsInstalled(e)) toInstall.Add(e);
                    }
                }

                // 2) Then add other selected packages in settings.OtherPackages order if available, otherwise in combined order
                var otherOrder = new List<ScriptableObject>();
                if (_settings != null && _settings.OtherPackages != null && _settings.OtherPackages.Count > 0)
                    otherOrder.AddRange(_settings.OtherPackages);
                else
                    otherOrder.AddRange(combined.Where(x => !orderedEssentials.Contains(x)));

                foreach (var aObj in otherOrder)
                {
                    if (aObj == null) continue;
                    string key = aObj.GetInstanceID().ToString();
                    bool sel = false; _selection.TryGetValue(key, out sel);
                    if (!sel) continue;

                    var ia2 = aObj as IUPMPackageAsset;
                    if (ia2 == null) continue;
                    var e = ia2.ToEntry();
                    if (!IsInstalled(e)) toInstall.Add(e);
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

        void ToggleEssential(ScriptableObject so, bool wantEssential)
        {
            if (so == null) return;
            var ia = so as IUPMPackageAsset;
            if (ia == null) return;

            if (ia.IsEssential == wantEssential)
                return;

            ia.IsEssential = wantEssential;

            try
            {
                if (_settings != null)
                {
                    if (wantEssential)
                    {
                        if (_settings.OtherPackages != null && _settings.OtherPackages.Remove(so))
                            EditorUtility.SetDirty(_settings);

                        if (_settings.EssentialPackages == null) _settings.EssentialPackages = new List<ScriptableObject>();
                        if (!_settings.EssentialPackages.Contains(so)) _settings.EssentialPackages.Add(so);
                        EditorUtility.SetDirty(_settings);
                    }
                    else
                    {
                        if (_settings.EssentialPackages != null && _settings.EssentialPackages.Remove(so))
                            EditorUtility.SetDirty(_settings);

                        if (_settings.OtherPackages == null) _settings.OtherPackages = new List<ScriptableObject>();
                        if (!_settings.OtherPackages.Contains(so)) _settings.OtherPackages.Add(so);
                        EditorUtility.SetDirty(_settings);
                    }
                }
            }
            catch { }

            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();
            RefreshPackageState();
            Repaint();
        }

        void MoveItemInSettings(ScriptableObject so, bool inEssentialList, int delta)
        {
            if (so == null || delta == 0 || _settings == null)
                return;

            List<ScriptableObject> list = inEssentialList ? _settings.EssentialPackages : _settings.OtherPackages;
            if (list == null)
                return;

            int idx = list.IndexOf(so);
            if (idx < 0)
                return;

            int newIdx = Mathf.Clamp(idx + delta, 0, list.Count - 1);
            if (newIdx == idx)
                return;

            list.RemoveAt(idx);
            list.Insert(newIdx, so);
            EditorUtility.SetDirty(_settings);
            AssetDatabase.SaveAssets();
            RefreshPackageState();
            Repaint();
        }

        int ComputeInsertIndex(List<Rect> rects, float mouseY, float scrollY, float scrollAreaTop)
        {
            if (rects == null || rects.Count == 0)
                return 0;

            // If mouse is above the first visible row, insert at start
            var first = rects[0];
            float firstVisibleTop = first.y - scrollY + scrollAreaTop;
            if (mouseY < firstVisibleTop)
                return 0;

            // Iterate rows using their visible positions (accounting for scroll)
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                float visibleTop = r.y - scrollY + scrollAreaTop;
                float visibleMid = visibleTop + r.height * 0.5f;
                if (mouseY < visibleMid)
                    return i;
            }

            // default: after last
            return rects.Count;
        }

        void MoveItemAcrossLists(ScriptableObject so, bool toEssential, int insertIndex)
        {
            if (so == null || _settings == null) return;

            try
            {
                // remove from both lists
                if (_settings.EssentialPackages != null && _settings.EssentialPackages.Remove(so)) EditorUtility.SetDirty(_settings);
                if (_settings.OtherPackages != null && _settings.OtherPackages.Remove(so)) EditorUtility.SetDirty(_settings);

                var target = toEssential ? _settings.EssentialPackages : _settings.OtherPackages;
                if (target == null)
                {
                    target = new List<ScriptableObject>();
                    if (toEssential) _settings.EssentialPackages = target; else _settings.OtherPackages = target;
                }

                insertIndex = Mathf.Clamp(insertIndex, 0, target.Count);
                if (!target.Contains(so)) target.Insert(insertIndex, so);

                var ia = so as IUPMPackageAsset;
                if (ia != null) ia.IsEssential = toEssential;

                EditorUtility.SetDirty(so);
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                RefreshPackageState();
                Repaint();
            }
            catch { }
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
            {
                // migrate legacy RequiredPackages into EssentialPackages if needed
                if ((settings.EssentialPackages == null || settings.EssentialPackages.Count == 0) && settings.RequiredPackages != null && settings.RequiredPackages.Count > 0)
                {
                    settings.EssentialPackages = new List<ScriptableObject>(settings.RequiredPackages);
                    settings.RequiredPackages.Clear();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }

                return settings;
            }

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
