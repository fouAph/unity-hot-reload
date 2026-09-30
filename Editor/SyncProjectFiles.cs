using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;

namespace HotReload.Editor
{
    [InitializeOnLoad]
    public static class SyncProjectFiles
    {
        static SyncProjectFiles()
        {
            EditorApplication.delayCall += Sync;
        }

        [MenuItem("Tools/Regenerate Project Files")]
        public static void Sync()
        {
            var editor = CodeEditor.Editor.CurrentCodeEditor;
            Debug.Log($"[SyncProjectFiles] Triggering project files generation via {editor?.GetType().FullName}...");
            
            try
            {
                editor?.SyncAll();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SyncProjectFiles] CodeEditor.SyncAll warning: {ex.Message}");
            }

            try
            {
                var syncVsType = typeof(EditorApplication).Assembly.GetType("UnityEditor.SyncVS");
                var syncMethod = syncVsType?.GetMethod("SyncSolution", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                syncMethod?.Invoke(null, null);
                Debug.Log("[SyncProjectFiles] UnityEditor.SyncVS.SyncSolution() invoked successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SyncProjectFiles] SyncSolution invocation note: {ex.Message}");
            }
        }
    }
}
