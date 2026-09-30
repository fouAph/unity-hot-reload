using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HotReload.Editor
{
    public struct UncompiledFieldInfo
    {
        public string Name;
        public string TypeName;
    }

    /// <summary>
    /// Custom Inspector overlay that detects newly declared serialized fields in source code
    /// that have not yet been compiled into Unity's AppDomain, matching commercial Hot Reload inspector behavior.
    /// Expression-bodied properties (e.g. public float speed => 10f;) and methods are automatically excluded.
    /// </summary>
    [CustomEditor(typeof(MonoBehaviour), editorForChildClasses: true, isFallback = true)]
    [CanEditMultipleObjects]
    public class HotReloadInspectorOverlay : UnityEditor.Editor
    {
        private static readonly Dictionary<string, (DateTime lastWrite, List<UncompiledFieldInfo> fields)> Cache =
            new Dictionary<string, (DateTime, List<UncompiledFieldInfo>)>();

        // Matches field declarations: public string questId; or [SerializeField] private Button playButton;
        // Specifically excludes properties (with => or { get; }) and methods
        private static readonly Regex FieldRegex = new Regex(
            @"(?:\[SerializeField\]\s*)?(?:public|private|protected)\s+(?!class|struct|enum|void|interface|delegate|event)([A-Za-z0-9_<>,\.\s]+?)\s+([A-Za-z0-9_]+)\s*(?:=[^;>]+)?;",
            RegexOptions.Compiled
        );

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var mono = target as MonoBehaviour;
            if (mono == null) return;

            var ms = MonoScript.FromMonoBehaviour(mono);
            if (ms == null) return;

            string assetPath = AssetDatabase.GetAssetPath(ms);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/")) return;

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            if (!File.Exists(fullPath)) return;

            var uncompiled = GetUncompiledFields(fullPath, mono.GetType());
            if (uncompiled != null && uncompiled.Count > 0)
            {
                EditorGUILayout.Space(6);

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Hot Reload Fields (enter Play Mode or Recompile to edit)", EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("Recompile", EditorStyles.miniButton, GUILayout.Width(75)))
                    {
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    }
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    foreach (var f in uncompiled)
                    {
                        EditorGUILayout.TextField(f.Name, $"None ({f.TypeName})");
                    }
                }
            }
        }

        private static List<UncompiledFieldInfo> GetUncompiledFields(string filePath, Type compiledType)
        {
            DateTime writeTime = File.GetLastWriteTimeUtc(filePath);

            if (Cache.TryGetValue(filePath, out var cached) && cached.lastWrite == writeTime)
            {
                return cached.fields;
            }

            var uncompiledList = new List<UncompiledFieldInfo>();

            try
            {
                string code = File.ReadAllText(filePath);
                var matches = FieldRegex.Matches(code);

                // Collect existing compiled fields, properties, and methods on the active type
                var existingMembers = new HashSet<string>(StringComparer.Ordinal);
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                foreach (var f in compiledType.GetFields(flags))
                {
                    existingMembers.Add(f.Name);
                }
                foreach (var p in compiledType.GetProperties(flags))
                {
                    existingMembers.Add(p.Name);
                }
                foreach (var m in compiledType.GetMethods(flags))
                {
                    existingMembers.Add(m.Name);
                }

                foreach (Match m in matches)
                {
                    string matchText = m.Value;

                    // Exclude properties (expression-bodied => or get/set blocks) and methods
                    if (matchText.Contains("=>") || matchText.Contains("{") || matchText.Contains("("))
                    {
                        continue;
                    }

                    if (m.Groups.Count >= 3)
                    {
                        string typeName = m.Groups[1].Value.Trim();
                        string fieldName = m.Groups[2].Value.Trim();

                        // Skip if the member already exists or is a property
                        if (existingMembers.Contains(fieldName))
                        {
                            continue;
                        }

                        if (compiledType.GetProperty(fieldName, flags) != null)
                        {
                            continue;
                        }

                        if (compiledType.GetMethod(fieldName, flags) != null)
                        {
                            continue;
                        }

                        uncompiledList.Add(new UncompiledFieldInfo
                        {
                            Name = fieldName,
                            TypeName = typeName
                        });
                    }
                }
            }
            catch
            {
                // Fallback gracefully on file read lock
            }

            Cache[filePath] = (writeTime, uncompiledList);
            return uncompiledList;
        }
    }
}
