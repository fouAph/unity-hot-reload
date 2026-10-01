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
    /// Expression-bodied properties, methods, nested types, and sibling types are automatically excluded.
    /// </summary>
    [CustomEditor(typeof(MonoBehaviour), editorForChildClasses: true, isFallback = true)]
    [CanEditMultipleObjects]
    public class HotReloadInspectorOverlay : UnityEditor.Editor
    {
        private static readonly Dictionary<string, (DateTime lastWrite, List<UncompiledFieldInfo> fields)> Cache =
            new Dictionary<string, (DateTime, List<UncompiledFieldInfo>)>();

        // Hanya mencocokkan Serialized Fields:
        // 1. [SerializeField] (public/private/protected)
        // 2. public fields biasa (bukan static, const, readonly, atau [NonSerialized])
        private static readonly Regex SerializedFieldRegex = new Regex(
            @"(?:\[SerializeField\][^\n;]*?\s*(?:public|private|protected|internal)?|\bpublic)\s+(?!class|struct|enum|void|interface|delegate|event|static|const|readonly)([A-Za-z0-9_<>,\.\[\]\s]+?)\s+([A-Za-z0-9_]+)\s*(?:=[^;>]+)?;",
            RegexOptions.Compiled
        );

        [InitializeOnLoadMethod]
        private static void InitLifecycle()
        {
            Cache.Clear();
            AssemblyReloadEvents.beforeAssemblyReload += Cache.Clear;
            AssemblyReloadEvents.afterAssemblyReload += Cache.Clear;
            EditorApplication.playModeStateChanged += _ => Cache.Clear();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            // Overlay ini HANYA aktif jika Hot Reload dijalankan secara manual DAN saat Play Mode
            if (!HotReloadWatcher.IsEnabled || !EditorApplication.isPlaying) return;

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
                    EditorGUILayout.LabelField("Hot Reload Fields (exit Play Mode or Recompile to edit)", EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("Recompile", EditorStyles.miniButton, GUILayout.Width(75)))
                    {
                        Cache.Clear();
                        HotReloadWatcher.UnlockAssemblies();
                        UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                        EditorUtility.RequestScriptReload();
                        GUIUtility.ExitGUI();
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
            string cacheKey = $"{filePath}|{compiledType.FullName}";

            if (Cache.TryGetValue(cacheKey, out var cached) && cached.lastWrite == writeTime)
            {
                return cached.fields;
            }

            var uncompiledList = new List<UncompiledFieldInfo>();

            try
            {
                string code = File.ReadAllText(filePath);

                // Strip comments
                code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
                code = Regex.Replace(code, @"//.*", "");

                // Hanya ekstrak isi langsung dari class target (abaikan nested class/struct/method)
                string classBody = ExtractDirectClassBody(code, compiledType.Name);
                if (string.IsNullOrEmpty(classBody))
                {
                    Cache[cacheKey] = (writeTime, uncompiledList);
                    return uncompiledList;
                }

                // Kumpulkan field terkompilasi yang sudah ada di runtime
                var existingMembers = new HashSet<string>(StringComparer.Ordinal);
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

                for (Type t = compiledType; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(flags)) existingMembers.Add(f.Name);
                    foreach (var p in t.GetProperties(flags)) existingMembers.Add(p.Name);
                    foreach (var m in t.GetMethods(flags)) existingMembers.Add(m.Name);
                }

                var matches = SerializedFieldRegex.Matches(classBody);
                foreach (Match m in matches)
                {
                    string matchText = m.Value;

                    // Abaikan properties dan methods
                    if (matchText.Contains("=>") || matchText.Contains("{") || matchText.Contains("("))
                    {
                        continue;
                    }

                    // Abaikan [NonSerialized]
                    if (matchText.Contains("[NonSerialized]"))
                    {
                        continue;
                    }

                    if (m.Groups.Count >= 3)
                    {
                        string typeName = m.Groups[1].Value.Trim();
                        string fieldName = m.Groups[2].Value.Trim();

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

            Cache[cacheKey] = (writeTime, uncompiledList);
            return uncompiledList;
        }

        private static string ExtractDirectClassBody(string code, string className)
        {
            var match = Regex.Match(code, $@"\bclass\s+{Regex.Escape(className)}\b[^{{]*\{{");
            if (!match.Success) return "";

            int startIndex = match.Index + match.Length;
            int depth = 1;
            var sb = new System.Text.StringBuilder();

            for (int i = startIndex; i < code.Length; i++)
            {
                char c = code[i];

                if (c == '"')
                {
                    i++;
                    while (i < code.Length && code[i] != '"')
                    {
                        if (code[i] == '\\') i++;
                        i++;
                    }
                    continue;
                }

                if (c == '\'')
                {
                    i++;
                    while (i < code.Length && code[i] != '\'')
                    {
                        if (code[i] == '\\') i++;
                        i++;
                    }
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    continue;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) break;
                    continue;
                }

                // Hanya ambil karakter di level class utama (depth == 1)
                // Nested classes, nested structs, methods, dan property blocks otomatis dilewati
                if (depth == 1)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
