using System;
using UnityEditor;
using UnityEngine;

namespace HotReload.Editor
{
    public enum LogVerbosityPreset
    {
        Verbose,
        Normal,
        Minimal,
        Silent
    }

    /// <summary>
    /// Centralized logging manager for Custom Hot Reload.
    /// Controls which debug messages appear in Unity's Console based on user preferences.
    /// </summary>
    public static class HotReloadLogger
    {
        private const string KeyLogFileChanges = "HotReload_LogFileChanges";
        private const string KeyLogMethodDetours = "HotReload_LogMethodDetours";
        private const string KeyLogCompletion = "HotReload_LogCompletion";
        private const string KeyLogCallbacks = "HotReload_LogCallbacks";
        private const string KeyLogSystemInfo = "HotReload_LogSystemInfo";

        public static bool LogFileChanges
        {
            get => EditorPrefs.GetBool(KeyLogFileChanges, true);
            set => EditorPrefs.SetBool(KeyLogFileChanges, value);
        }

        public static bool LogMethodDetours
        {
            get => EditorPrefs.GetBool(KeyLogMethodDetours, true);
            set => EditorPrefs.SetBool(KeyLogMethodDetours, value);
        }

        public static bool LogCompletionSummary
        {
            get => EditorPrefs.GetBool(KeyLogCompletion, true);
            set => EditorPrefs.SetBool(KeyLogCompletion, value);
        }

        public static bool LogCallbacks
        {
            get => EditorPrefs.GetBool(KeyLogCallbacks, true);
            set => EditorPrefs.SetBool(KeyLogCallbacks, value);
        }

        public static bool LogSystemInfo
        {
            get => EditorPrefs.GetBool(KeyLogSystemInfo, false);
            set => EditorPrefs.SetBool(KeyLogSystemInfo, value);
        }

        public static void ApplyPreset(LogVerbosityPreset preset)
        {
            switch (preset)
            {
                case LogVerbosityPreset.Verbose:
                    LogFileChanges = true;
                    LogMethodDetours = true;
                    LogCompletionSummary = true;
                    LogCallbacks = true;
                    LogSystemInfo = true;
                    break;

                case LogVerbosityPreset.Normal:
                    LogFileChanges = true;
                    LogMethodDetours = true;
                    LogCompletionSummary = true;
                    LogCallbacks = true;
                    LogSystemInfo = false;
                    break;

                case LogVerbosityPreset.Minimal:
                    LogFileChanges = false;
                    LogMethodDetours = false;
                    LogCompletionSummary = true;
                    LogCallbacks = false;
                    LogSystemInfo = false;
                    break;

                case LogVerbosityPreset.Silent:
                    LogFileChanges = false;
                    LogMethodDetours = false;
                    LogCompletionSummary = false;
                    LogCallbacks = false;
                    LogSystemInfo = false;
                    break;
            }
        }

        public static void LogChangeDetected(string fileName)
        {
            if (LogFileChanges)
            {
                Debug.Log($"<color=#bd93f9>[HotReload]</color> Detected change in: <b>{fileName}</b>. Hot reloading...");
            }
        }

        public static void LogDetouredMethod(string original, string replacement)
        {
            if (LogMethodDetours)
            {
                Debug.Log($"<color=#50fa7b>[HotReload]</color> Successfully detoured: <b>{original}</b> ➔ <b>{replacement}</b>");
            }
        }

        public static void LogReverted(string methodName)
        {
            if (LogMethodDetours)
            {
                Debug.Log($"<color=#f1fa8c>[HotReload]</color> Reverted: <b>{methodName}</b>");
            }
        }

        public static void LogCompletion(int patchCount)
        {
            if (LogCompletionSummary)
            {
                Debug.Log($"<color=#50fa7b>[HotReload]</color> Complete! Applied {patchCount} method & property patch(es).");
            }
        }

        public static void LogCallbackInvoked(string callbackName, int instanceCount = 1, bool isStatic = false)
        {
            if (!LogCallbacks) return;

            if (isStatic)
            {
                Debug.Log($"<color=#ff79c6>[HotReload Callback]</color> Invoked static callback: <b>{callbackName}()</b>");
            }
            else
            {
                Debug.Log($"<color=#ff79c6>[HotReload Callback]</color> Invoked instance callback: <b>{callbackName}()</b> on {instanceCount} instance(s).");
            }
        }

        public static void LogInfo(string message)
        {
            if (LogSystemInfo)
            {
                Debug.Log($"<color=#8be9fd>[HotReload]</color> {message}");
            }
        }

        public static void LogError(string message)
        {
            // Errors always log
            Debug.LogError($"<color=#ff5555>[HotReload Error]</color> {message}");
        }
    }
}
