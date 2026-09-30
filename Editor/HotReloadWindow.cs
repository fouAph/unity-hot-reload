using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HotReload.Editor
{
    public class HotReloadWindow : EditorWindow
    {
        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "▶  Run", "⚙  Settings", "❓  Help" };
        private Vector2 _timelineScroll;
        private Vector2 _settingsScroll;
        private bool _timelineFoldout = true;
        private MonoScript _selectedScript;

        // Custom GUIStyles
        private GUIStyle _bannerStyle;
        private GUIStyle _bannerTitleStyle;
        private GUIStyle _cardStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _bulletStyle;
        private GUIStyle _timestampStyle;
        private GUIStyle _tabButtonStyle;
        private GUIStyle _tabButtonActiveStyle;
        private bool _stylesInitialized;

        private static readonly Color BannerBgColor = new Color(0.18f, 0.42f, 0.48f, 0.45f);
        private static readonly Color CardBgColor = new Color(0.15f, 0.30f, 0.35f, 0.25f);
        private static readonly Color PillGreen = new Color(0.2f, 0.8f, 0.4f);
        private static readonly Color PillOrange = new Color(1.0f, 0.7f, 0.2f);
        private static readonly Color PillRed = new Color(1.0f, 0.35f, 0.35f);
        private static readonly Color PillGray = new Color(0.6f, 0.6f, 0.6f);

        [MenuItem("Tools/Custom Hot Reload")]
        public static void ShowWindow()
        {
            var window = GetWindow<HotReloadWindow>("Hot Reload");
            window.minSize = new Vector2(360, 480);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += RepaintOnUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintOnUpdate;
        }

        private void RepaintOnUpdate()
        {
            // Repaint when compiling or timeline is active so relative timestamps ("now", "5s ago") update
            if (HotReloadCompiler.IsCompiling || (HotReloadCompiler.History.Count > 0 && _selectedTab == 0))
            {
                Repaint();
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized && _bannerStyle != null) return;

            _bannerStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(16, 16, 14, 14),
                margin = new RectOffset(4, 4, 6, 8),
                alignment = TextAnchor.MiddleCenter
            };

            _bannerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.85f, 0.95f, 1.0f) }
            };

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(4, 4, 4, 6)
            };

            _pillStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };

            _bulletStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                richText = true,
                padding = new RectOffset(16, 0, 1, 1),
                normal = { textColor = new Color(0.80f, 0.88f, 0.92f) }
            };

            _timestampStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.55f, 0.65f, 0.72f) }
            };

            _tabButtonStyle = new GUIStyle(EditorStyles.toolbarButton)
            {
                fixedHeight = 28,
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            _tabButtonActiveStyle = new GUIStyle(_tabButtonStyle)
            {
                fontStyle = FontStyle.Bold
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            DrawTopTabs();

            switch (_selectedTab)
            {
                case 0:
                    DrawRunTab();
                    break;
                case 1:
                    DrawSettingsTab();
                    break;
                case 2:
                    DrawHelpTab();
                    break;
            }
        }

        private void DrawTopTabs()
        {
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                for (int i = 0; i < _tabNames.Length; i++)
                {
                    bool isSelected = _selectedTab == i;
                    var style = isSelected ? _tabButtonActiveStyle : _tabButtonStyle;
                    var prevColor = GUI.backgroundColor;
                    if (isSelected)
                    {
                        GUI.backgroundColor = new Color(0.35f, 0.65f, 0.95f);
                    }

                    if (GUILayout.Button(_tabNames[i], style))
                    {
                        _selectedTab = i;
                        GUI.FocusControl(null);
                    }
                    GUI.backgroundColor = prevColor;
                }
            }
            EditorGUILayout.Space(4);
        }

        private void DrawRunTab()
        {
            // 1. Logo Banner
            DrawBanner();

            EditorGUILayout.Space(4);

            // 2. Status Row (Pill + Refresh + Stop/Start)
            DrawStatusBar();

            EditorGUILayout.Space(6);

            // 3. Timeline Section
            DrawTimelineSection();
        }

        private void DrawBanner()
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = BannerBgColor;

            using (new EditorGUILayout.VerticalScope(_bannerStyle))
            {
                EditorGUILayout.LabelField("🔥  HOT RELOAD", _bannerTitleStyle, GUILayout.Height(28));
            }

            GUI.backgroundColor = prevBg;
        }

        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                // Status Pill
                string statusText;
                Color statusColor;

                if (!HotReloadWatcher.IsEnabled)
                {
                    statusText = "■  Stopped (Inactive)";
                    statusColor = PillGray;
                }
                else if (HotReloadCompiler.IsCompiling)
                {
                    statusText = "⏳  Compiling...";
                    statusColor = PillOrange;
                }
                else if (HotReloadCompiler.LastStatus == "Compile error")
                {
                    statusText = "✖  Compile error";
                    statusColor = PillRed;
                }
                else if (HotReloadCompiler.History.Count > 0 && HotReloadCompiler.History[0].Status == "Reload finished")
                {
                    statusText = "✔  Reload finished";
                    statusColor = PillGreen;
                }
                else
                {
                    statusText = "●  Running (Watching for changes)";
                    statusColor = PillGreen;
                }

                var prevColor = GUI.color;
                GUI.color = statusColor;
                EditorGUILayout.LabelField(statusText, _pillStyle, GUILayout.Height(26));
                GUI.color = prevColor;

                GUILayout.FlexibleSpace();

                // ↻ Reload Button
                if (GUILayout.Button(new GUIContent("↻", "Trigger compile of last changed file or selection"), GUILayout.Width(32), GUILayout.Height(24)))
                {
                    if (!string.IsNullOrEmpty(HotReloadCompiler.LastChangedFile) && File.Exists(HotReloadCompiler.LastChangedFile))
                    {
                        HotReloadCompiler.CompileAndPatch(HotReloadCompiler.LastChangedFile);
                    }
                    else if (_selectedScript != null)
                    {
                        string path = Path.Combine(Directory.GetCurrentDirectory(), AssetDatabase.GetAssetPath(_selectedScript));
                        HotReloadCompiler.CompileAndPatch(path);
                    }
                    else
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }
                }

                // Stop / Start Button with tinted color
                bool isRunning = HotReloadWatcher.IsEnabled;
                string btnText = isRunning ? "■ Stop" : "▶ Start";
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isRunning ? new Color(0.9f, 0.45f, 0.45f) : new Color(0.4f, 0.8f, 0.5f);

                if (GUILayout.Button(btnText, GUILayout.Width(72), GUILayout.Height(24)))
                {
                    HotReloadWatcher.IsEnabled = !isRunning;
                }
                GUI.backgroundColor = prevBg;
            }
        }

        private void DrawTimelineSection()
        {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = CardBgColor;

            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                GUI.backgroundColor = prevBg;

                // Timeline Header Row
                using (new EditorGUILayout.HorizontalScope())
                {
                    _timelineFoldout = EditorGUILayout.Foldout(_timelineFoldout, "▼  Timeline", true, EditorStyles.foldoutHeader);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(48)))
                    {
                        HotReloadCompiler.History.Clear();
                    }
                }

                if (_timelineFoldout)
                {
                    EditorGUILayout.Space(6);

                    var history = HotReloadCompiler.History;
                    if (history.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No reload events recorded yet.\nEdit any script while in Play Mode or Edit Mode to see live detours appear here.", MessageType.Info);
                    }
                    else
                    {
                        _timelineScroll = EditorGUILayout.BeginScrollView(_timelineScroll, GUILayout.MaxHeight(260));

                        for (int i = 0; i < history.Count; i++)
                        {
                            var evt = history[i];
                            DrawTimelineEntry(evt);
                            if (i < history.Count - 1) EditorGUILayout.Space(4);
                        }

                        EditorGUILayout.EndScrollView();
                    }
                }
            }
        }

        private void DrawTimelineEntry(ReloadEvent evt)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Top row: Status + Time ago
                using (new EditorGUILayout.HorizontalScope())
                {
                    var prevColor = GUI.color;
                    string icon;
                    if (evt.Status == "Stopped")
                    {
                        GUI.color = PillGray;
                        icon = "■";
                    }
                    else if (evt.Status == "Started")
                    {
                        GUI.color = PillGreen;
                        icon = "▶";
                    }
                    else
                    {
                        GUI.color = evt.IsSuccess ? PillGreen : PillRed;
                        icon = evt.IsSuccess ? "✔" : "✖";
                    }

                    EditorGUILayout.LabelField($"{icon}  {evt.Status}", EditorStyles.boldLabel);
                    GUI.color = prevColor;

                    EditorGUILayout.LabelField(evt.GetTimeAgo(), _timestampStyle, GUILayout.Width(80));
                }

                // File name if available
                if (!string.IsNullOrEmpty(evt.FileName))
                {
                    EditorGUILayout.LabelField($"File: {evt.FileName}", EditorStyles.miniLabel);
                }

                // Bullets of detoured methods
                if (evt.PatchedMethods != null && evt.PatchedMethods.Count > 0)
                {
                    foreach (var method in evt.PatchedMethods)
                    {
                        EditorGUILayout.LabelField($"•  <b>{method}</b>", _bulletStyle);
                    }
                }
                else if (evt.IsSuccess)
                {
                    EditorGUILayout.LabelField("•  No method changes detected", _bulletStyle);
                }

                // Error message if failed
                if (!evt.IsSuccess && !string.IsNullOrEmpty(evt.ErrorMessage))
                {
                    EditorGUILayout.HelpBox(evt.ErrorMessage, MessageType.Error);
                }
            }
        }

        private void DrawSettingsTab()
        {
            _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll);

            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField("Automation Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space(6);

                bool watcherEnabled = EditorGUILayout.Toggle("Auto-Watch Files (*.cs)", HotReloadWatcher.IsEnabled);
                if (watcherEnabled != HotReloadWatcher.IsEnabled)
                {
                    HotReloadWatcher.IsEnabled = watcherEnabled;
                }

                bool lockPlayMode = EditorGUILayout.Toggle("Lock Domain Reload in Play Mode", HotReloadWatcher.LockDomainReloadInPlayMode);
                if (lockPlayMode != HotReloadWatcher.LockDomainReloadInPlayMode)
                {
                    HotReloadWatcher.LockDomainReloadInPlayMode = lockPlayMode;
                }

                bool disableAutoRefresh = EditorGUILayout.Toggle("Disable Native Auto-Refresh", HotReloadWatcher.DisableUnityAutoRefresh);
                if (disableAutoRefresh != HotReloadWatcher.DisableUnityAutoRefresh)
                {
                    HotReloadWatcher.DisableUnityAutoRefresh = disableAutoRefresh;
                }

                EditorGUILayout.Space(6);

                int scriptChangesMode = EditorPrefs.GetInt("ScriptCompilationDuringPlay", 1);
                string[] modeNames = { "Recompile And Continue Playing", "Recompile After Finished Playing", "Stop Playing And Recompile" };
                int newMode = EditorGUILayout.Popup("Script Changes While Playing", scriptChangesMode, modeNames);
                if (newMode != scriptChangesMode)
                {
                    EditorPrefs.SetInt("ScriptCompilationDuringPlay", newMode);
                }

                if (newMode == 2)
                {
                    EditorGUILayout.HelpBox("⚠️ 'Stop Playing And Recompile' will cause Unity to exit Play Mode whenever you save! Change to 'Recompile After Finished Playing' for seamless hot reload.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(8);

            // Console Logging Preferences
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField("Console Logging Preferences", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Choose which debug messages print to Unity's Console:", EditorStyles.miniLabel);
                EditorGUILayout.Space(6);

                // Quick Presets Row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Presets:", GUILayout.Width(60));
                    if (GUILayout.Button("Verbose", EditorStyles.miniButtonLeft)) HotReloadLogger.ApplyPreset(LogVerbosityPreset.Verbose);
                    if (GUILayout.Button("Normal", EditorStyles.miniButtonMid)) HotReloadLogger.ApplyPreset(LogVerbosityPreset.Normal);
                    if (GUILayout.Button("Minimal", EditorStyles.miniButtonMid)) HotReloadLogger.ApplyPreset(LogVerbosityPreset.Minimal);
                    if (GUILayout.Button("Silent", EditorStyles.miniButtonRight)) HotReloadLogger.ApplyPreset(LogVerbosityPreset.Silent);
                }

                EditorGUILayout.Space(6);

                HotReloadLogger.LogFileChanges = EditorGUILayout.ToggleLeft("  Log File Changes (\"Detected change in: ...\")", HotReloadLogger.LogFileChanges);
                HotReloadLogger.LogMethodDetours = EditorGUILayout.ToggleLeft("  Log Method Detours (\"Successfully detoured: ...\")", HotReloadLogger.LogMethodDetours);
                HotReloadLogger.LogCompletionSummary = EditorGUILayout.ToggleLeft("  Log Completion Summary (\"Complete! Applied X patches\")", HotReloadLogger.LogCompletionSummary);
                HotReloadLogger.LogCallbacks = EditorGUILayout.ToggleLeft("  Log [HotReloaded] Callbacks", HotReloadLogger.LogCallbacks);
                HotReloadLogger.LogSystemInfo = EditorGUILayout.ToggleLeft("  Log Internal Setup & Compiler Info", HotReloadLogger.LogSystemInfo);

                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("Compiler errors and fatal warnings are always logged to Console regardless of filter settings.", MessageType.None);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField("Manual Operations", EditorStyles.boldLabel);
                EditorGUILayout.Space(6);

                _selectedScript = (MonoScript)EditorGUILayout.ObjectField("Target Script", _selectedScript, typeof(MonoScript), false);

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = _selectedScript != null;
                    if (GUILayout.Button("Manual Hot Reload", GUILayout.Height(26)))
                    {
                        string path = AssetDatabase.GetAssetPath(_selectedScript);
                        if (!string.IsNullOrEmpty(path))
                        {
                            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);
                            HotReloadCompiler.CompileAndPatch(fullPath);
                        }
                    }
                    GUI.enabled = true;

                    if (GUILayout.Button("Full Recompile (Ctrl+R)", GUILayout.Height(26)))
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }

                    GUI.enabled = MethodDetour.ActivePatchCount > 0;
                    if (GUILayout.Button("Revert All", GUILayout.Height(26)))
                    {
                        MethodDetour.RevertAll();
                    }
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.Space(8);

            // Active Patches Inspector
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField($"Active Detours ({MethodDetour.ActivePatchCount})", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);

                var patches = MethodDetour.Patches;
                if (patches.Count == 0)
                {
                    EditorGUILayout.LabelField("No active method detours.", EditorStyles.miniLabel);
                }
                else
                {
                    MethodBase toRevert = null;
                    foreach (var kvp in patches)
                    {
                        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                        {
                            string orig = $"{kvp.Key.DeclaringType?.Name}.{kvp.Key.Name}";
                            EditorGUILayout.LabelField(orig, EditorStyles.miniLabel);
                            if (GUILayout.Button("Revert", EditorStyles.miniButton, GUILayout.Width(50)))
                            {
                                toRevert = kvp.Key;
                            }
                        }
                    }
                    if (toRevert != null) MethodDetour.Revert(toRevert);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHelpTab()
        {
            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField("💡 How Custom Hot Reload Works", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("1. Save any script while in Play Mode or Edit Mode.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("2. Unity's bundled Roslyn compiles the changes into an in-memory assembly.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("3. Memory pointers jump directly to the new methods in <100ms.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("4. Zero domain reload freeze, zero game resets.", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField("Supported Live (< 100ms)", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("✔ Method bodies & math calculations", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("✔ Property getters & setters (`get_`, `set_`)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("✔ Operator overloads (`+`, `-`, etc.)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("✔ [HotReloaded] lifecycle callbacks", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("✔ Adding variables via this.SetDynamicField()", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.VerticalScope(_cardStyle))
            {
                EditorGUILayout.LabelField("Requires Full Recompile (Ctrl + R)", EditorStyles.boldLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("• Adding new member fields to show in Inspector", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Changing class inheritance (: MonoBehaviour)", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("• Adding brand new .cs classes", EditorStyles.miniLabel);
            }
        }
    }
}
