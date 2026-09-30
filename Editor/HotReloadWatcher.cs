using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace HotReload.Editor
{
    /// <summary>
    /// Monitors script files in Assets/ for changes, debounces saves,
    /// dispatches HotReloadCompiler on the main thread, and locks domain reloads during Play Mode.
    /// </summary>
    [InitializeOnLoad]
    public static class HotReloadWatcher
    {
        private const string PrefKeyEnabled = "HotReload_Watcher_Enabled";
        private const string PrefKeyLockPlayMode = "HotReload_LockPlayMode_Enabled";

        private static FileSystemWatcher _watcher;
        private static Timer _debounceTimer;
        private static string _pendingFilePath;
        private static readonly object _lock = new object();
        private static bool _isAssembliesLocked;

        public static bool IsEnabled
        {
            get => EditorPrefs.GetBool(PrefKeyEnabled, true);
            set
            {
                bool prev = EditorPrefs.GetBool(PrefKeyEnabled, true);
                EditorPrefs.SetBool(PrefKeyEnabled, value);
                if (value) StartEngine(!prev);
                else StopEngine();
            }
        }

        public static void StartEngine(bool recordHistory = true)
        {
            StartWatcher();
            if (EditorApplication.isPlaying && LockDomainReloadInPlayMode)
            {
                LockAssemblies();
            }

            if (recordHistory)
            {
                HotReloadCompiler.History.Insert(0, new ReloadEvent
                {
                    Status = "Started",
                    IsSuccess = true,
                    FileName = "",
                    PatchedMethods = new List<string> { "Hot Reload active. Watching Assets/ (*.cs) for changes." }
                });
            }

            HotReloadLogger.LogInfo("Hot Reload started. File watcher active and ready.");
        }

        public static void StopEngine()
        {
            StopWatcher();
            int patchCount = MethodDetour.ActivePatchCount;
            MethodDetour.RevertAll();
            UnlockAssemblies();

            HotReloadCompiler.History.Insert(0, new ReloadEvent
            {
                Status = "Stopped",
                IsSuccess = true,
                FileName = "",
                PatchedMethods = new List<string> { patchCount > 0 ? $"Reverted {patchCount} active detour(s) back to original code." : "Watcher paused. Hot Reload inactive." }
            });

            HotReloadLogger.LogInfo($"Hot Reload stopped. Reverted {patchCount} detour(s) and unlocked domain reloads.");
        }

        public static bool LockDomainReloadInPlayMode
        {
            get => EditorPrefs.GetBool(PrefKeyLockPlayMode, true);
            set
            {
                EditorPrefs.SetBool(PrefKeyLockPlayMode, value);
                if (EditorApplication.isPlaying)
                {
                    if (value) LockAssemblies();
                    else UnlockAssemblies();
                }
            }
        }

        public static bool DisableUnityAutoRefresh
        {
            get => EditorPrefs.GetInt("kAutoRefresh", 1) == 0;
            set => EditorPrefs.SetInt("kAutoRefresh", value ? 0 : 1);
        }

        public static bool IsAssembliesLocked => _isAssembliesLocked;

        static HotReloadWatcher()
        {
            EditorApplication.delayCall += () =>
            {
                // Ensure Unity does not stop Play Mode when scripts change
                if (EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0) == 2)
                {
                    EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1);
                    HotReloadLogger.LogInfo("Set 'Script Changes While Playing' to 'Recompile After Finished Playing' so Unity will not stop Play Mode on save.");
                }

                if (IsEnabled)
                {
                    StartWatcher();
                }
            };

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!IsEnabled || !LockDomainReloadInPlayMode) return;

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                LockAssemblies();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                UnlockAssemblies();
                MethodDetour.RevertAll();
            }
        }

        public static void LockAssemblies()
        {
            if (!_isAssembliesLocked)
            {
                EditorApplication.LockReloadAssemblies();
                _isAssembliesLocked = true;
                HotReloadLogger.LogInfo("Domain reloads locked during Play Mode for seamless hot reloading.");
            }
        }

        public static void UnlockAssemblies()
        {
            if (_isAssembliesLocked)
            {
                EditorApplication.UnlockReloadAssemblies();
                _isAssembliesLocked = false;
                HotReloadLogger.LogInfo("Domain reloads unlocked.");
            }
        }

        public static void StartWatcher()
        {
            StopWatcher();

            try
            {
                string assetsPath = Application.dataPath;
                _watcher = new FileSystemWatcher(assetsPath, "*.cs")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };

                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.EnableRaisingEvents = true;

                HotReloadLogger.LogInfo("File watcher active on Assets/ (*.cs).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotReload] Failed to start FileSystemWatcher: {ex.Message}");
            }
        }

        public static void StopWatcher()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileChanged;
                _watcher.Created -= OnFileChanged;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            string fullPath = e.FullPath.Replace('\\', '/');

            // Ignore editor scripts or files within the HotReload tool itself
            if (fullPath.Contains("/Editor/") || fullPath.Contains("/HotReload/Editor/"))
                return;

            lock (_lock)
            {
                _pendingFilePath = fullPath;
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(OnDebounceElapsed, null, 250, Timeout.Infinite);
            }
        }

        private static void OnDebounceElapsed(object state)
        {
            string fileToReload;
            lock (_lock)
            {
                fileToReload = _pendingFilePath;
                _pendingFilePath = null;
            }

            if (string.IsNullOrEmpty(fileToReload) || !File.Exists(fileToReload))
                return;

            EditorApplication.delayCall += () =>
            {
                HotReloadLogger.LogChangeDetected(Path.GetFileName(fileToReload));
                HotReloadCompiler.CompileAndPatch(fileToReload);
            };
        }

        private static void Cleanup()
        {
            StopWatcher();
            UnlockAssemblies();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
