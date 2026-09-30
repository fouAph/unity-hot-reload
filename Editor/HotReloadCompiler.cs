using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HotReload.Editor
{
    public class ReloadEvent
    {
        public DateTime Timestamp = DateTime.Now;
        public string Status = "Reload finished";
        public bool IsSuccess = true;
        public string FileName;
        public string ErrorMessage;
        public List<string> PatchedMethods = new List<string>();

        public string GetTimeAgo()
        {
            var diff = DateTime.Now - Timestamp;
            if (diff.TotalSeconds < 5) return "now";
            if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            return Timestamp.ToString("HH:mm:ss");
        }
    }

    /// <summary>
    /// Compiles C# source files using Unity's official bundled Roslyn CLI (netcorerun + csc.dll),
    /// completely isolated from Mono runtime image incompatibilities.
    /// Pairs and patches modified methods, properties, and operators using MethodDetour,
    /// and invokes [HotReloaded] callbacks across scene instances.
    /// </summary>
    public static class HotReloadCompiler
    {
        private static string _netcorerunPath;
        private static string _cscDllPath;
        private static bool _initialized;

        public static readonly List<ReloadEvent> History = new List<ReloadEvent>();
        public static string LastStatus = "Waiting for changes";
        public static bool IsCompiling = false;
        public static string LastChangedFile = "";

        public static bool Initialize()
        {
            if (_initialized) return true;

            string unityData = EditorApplication.applicationContentsPath;

            // 1. Primary Unity 6 compiler runner
            string netcore = Path.Combine(unityData, "netcorerun", "netcorerun.exe");
            string csc = Path.Combine(unityData, "DotNetSdkRoslyn", "csc.dll");

            if (File.Exists(netcore) && File.Exists(csc))
            {
                _netcorerunPath = netcore;
                _cscDllPath = csc;
                _initialized = true;
                Debug.Log("<color=#8be9fd>[HotReload]</color> Compiler pipeline initialized via Unity netcorerun + csc.dll.");
                return true;
            }

            // 2. Fallback to Roslyn csc.exe if available
            string[] fallbackCsCs = {
                Path.Combine(unityData, "Tools", "Roslyn", "csc.exe"),
                Path.Combine(unityData, "MonoBleedingEdge", "lib", "mono", "msbuild", "Current", "bin", "Roslyn", "csc.exe")
            };

            string fallback = fallbackCsCs.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(fallback))
            {
                _netcorerunPath = fallback;
                _cscDllPath = null;
                _initialized = true;
                Debug.Log("<color=#8be9fd>[HotReload]</color> Compiler pipeline initialized via fallback csc.exe.");
                return true;
            }

            Debug.LogError("[HotReload] Could not locate Unity C# compiler (netcorerun.exe or csc.exe).");
            return false;
        }

        /// <summary>
        /// Compiles a source file and automatically patches all matching methods.
        /// </summary>
        public static bool CompileAndPatch(string filePath)
        {
            if (!Initialize()) return false;

            if (!File.Exists(filePath))
            {
                Debug.LogError($"[HotReload] Source file not found: {filePath}");
                return false;
            }

            LastChangedFile = filePath;
            string hintName = Path.GetFileNameWithoutExtension(filePath);
            return CompileAndPatchFile(filePath, hintName);
        }

        private static bool CompileAndPatchFile(string sourceFilePath, string hintName)
        {
            IsCompiling = true;
            LastStatus = "Compiling...";

            string tempDir = Path.Combine(Application.dataPath, "..", "Temp", "HotReload");
            Directory.CreateDirectory(tempDir);

            string fileId = $"{hintName}_{Guid.NewGuid():N}";
            string outputDll = Path.Combine(tempDir, $"{fileId}.dll").Replace('\\', '/');
            string rspPath = Path.Combine(tempDir, $"{fileId}.rsp").Replace('\\', '/');

            try
            {
                // Build compilation response file (.rsp)
                var rspLines = new List<string>();
                rspLines.Add("-target:library");
                rspLines.Add("-nowarn:0169,0649,0414");

                // Preprocessor defines
                var defines = new List<string> { "UNITY_EDITOR", "DEBUG" };
                defines.AddRange(EditorUserBuildSettings.activeScriptCompilationDefines);
                rspLines.Add($"-define:{string.Join(";", defines.Distinct())}");

                // Output DLL
                rspLines.Add($"-out:\"{outputDll}\"");

                // Gather active assembly references
                bool hasCoreModule = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && Path.GetFileName(a.Location).Equals("UnityEngine.CoreModule.dll", StringComparison.OrdinalIgnoreCase));

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location) || !File.Exists(asm.Location))
                        continue;

                    string fileName = Path.GetFileName(asm.Location);

                    // Skip monolithic forwarding assembly if modular assemblies are used to prevent CS0433
                    if (hasCoreModule && string.Equals(fileName, "UnityEngine.dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    rspLines.Add($"-r:\"{asm.Location.Replace('\\', '/')}\"");
                }

                // Add source file
                rspLines.Add($"\"{sourceFilePath.Replace('\\', '/')}\"");

                // Write rsp file
                File.WriteAllLines(rspPath, rspLines);

                // Run compilation process
                var psi = new ProcessStartInfo();
                if (!string.IsNullOrEmpty(_cscDllPath))
                {
                    psi.FileName = _netcorerunPath;
                    psi.Arguments = $"\"{_cscDllPath}\" \"@{rspPath}\"";
                }
                else
                {
                    psi.FileName = _netcorerunPath;
                    psi.Arguments = $"\"@{rspPath}\"";
                }

                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Debug.LogError("[HotReload] Failed to start compiler process.");
                        IsCompiling = false;
                        LastStatus = "Start failed";
                        return false;
                    }

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(15000);

                    if (process.ExitCode != 0)
                    {
                        string errMsg = $"{stdout}\n{stderr}";
                        Debug.LogError($"<color=#ff5555>[HotReload Compiler Error]</color>\n{errMsg}");

                        History.Insert(0, new ReloadEvent
                        {
                            Status = "Compile error",
                            IsSuccess = false,
                            FileName = Path.GetFileName(sourceFilePath),
                            ErrorMessage = errMsg
                        });

                        IsCompiling = false;
                        LastStatus = "Compile error";
                        return false;
                    }
                }

                if (!File.Exists(outputDll))
                {
                    Debug.LogError("[HotReload] Compiler exited successfully but output assembly was not created.");
                    IsCompiling = false;
                    LastStatus = "Output missing";
                    return false;
                }

                // Load compiled assembly into memory
                byte[] assemblyBytes = File.ReadAllBytes(outputDll);
                Assembly newAssembly = Assembly.Load(assemblyBytes);

                var patchedMethods = new List<string>();
                bool patched = PatchAssemblyMethods(newAssembly, patchedMethods);

                History.Insert(0, new ReloadEvent
                {
                    Status = "Reload finished",
                    IsSuccess = true,
                    FileName = Path.GetFileName(sourceFilePath),
                    PatchedMethods = patchedMethods
                });

                if (History.Count > 50) History.RemoveAt(History.Count - 1);

                IsCompiling = false;
                LastStatus = "Reload finished";
                return patched;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotReload] Hot reload failed: {ex.Message}\n{ex.StackTrace}");
                History.Insert(0, new ReloadEvent
                {
                    Status = "Reload failed",
                    IsSuccess = false,
                    FileName = Path.GetFileName(sourceFilePath),
                    ErrorMessage = ex.Message
                });
                IsCompiling = false;
                LastStatus = "Reload failed";
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(rspPath)) File.Delete(rspPath);
                    if (File.Exists(outputDll)) File.Delete(outputDll);
                }
                catch
                {
                    // Ignore file lock on cleanup
                }
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }

        private static bool PatchAssemblyMethods(Assembly newAssembly, List<string> patchedMethodSignatures)
        {
            int patchCount = 0;
            var updatedTypes = new HashSet<Type>();
            var newTypes = GetLoadableTypes(newAssembly);

            // Prioritize user project assemblies
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderByDescending(a => a.GetName().Name == "Assembly-CSharp" || a.GetName().Name.StartsWith("HotReload"))
                .ToList();

            foreach (var newType in newTypes)
            {
                if (newType == null) continue;

                Type originalType = null;
                foreach (var asm in allAssemblies)
                {
                    if (asm == newAssembly) continue;
                    try
                    {
                        var t = asm.GetType(newType.FullName, false);
                        if (t != null)
                        {
                            originalType = t;
                            break;
                        }
                    }
                    catch
                    {
                        // Safely ignore any assemblies with missing dependencies or deleted files
                    }
                }

                if (originalType == null) continue;

                // Inspect all methods including property getters, setters, and operators
                var methods = newType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var newMethod in methods)
                {
                    // Filter out compiler generated lambda display classes and constructors
                    if (newMethod.Name.StartsWith("<") || newMethod.Name == ".ctor" || newMethod.Name == ".cctor")
                        continue;

                    var paramTypes = newMethod.GetParameters().Select(p => p.ParameterType).ToArray();

                    var originalMethod = originalType.GetMethod(
                        newMethod.Name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                        null,
                        paramTypes,
                        null
                    );

                    if (originalMethod != null)
                    {
                        if (MethodDetour.Patch(originalMethod, newMethod))
                        {
                            patchCount++;
                            updatedTypes.Add(originalType);
                            string sig = $"{newMethod.ReturnType.Name} {originalType.Name}::{newMethod.Name}";
                            patchedMethodSignatures.Add(sig);
                        }
                    }
                }
            }

            // Trigger [HotReloaded] callbacks across affected types
            foreach (var type in updatedTypes)
            {
                TriggerHotReloadCallbacks(type);
            }

            HotReloadLogger.LogCompletion(patchCount);
            return patchCount > 0;
        }

        private static void TriggerHotReloadCallbacks(Type targetType)
        {
            var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (var method in methods)
            {
                bool hasAttr = method.GetCustomAttributes(true).Any(a => a.GetType().Name == "HotReloadedAttribute");
                bool isNamedCallback = method.Name.Equals("OnHotReloaded", StringComparison.OrdinalIgnoreCase);

                if (!hasAttr && !isNamedCallback)
                    continue;

                if (method.GetParameters().Length != 0)
                    continue;

                try
                {
                    if (method.IsStatic)
                    {
                        method.Invoke(null, null);
                        HotReloadLogger.LogCallbackInvoked($"{targetType.Name}.{method.Name}", 1, true);
                    }
                    else if (typeof(UnityEngine.Component).IsAssignableFrom(targetType))
                    {
                        var instances = UnityEngine.Object.FindObjectsByType(targetType, FindObjectsSortMode.None);
                        foreach (var instance in instances)
                        {
                            method.Invoke(instance, null);
                        }
                        if (instances.Length > 0)
                        {
                            HotReloadLogger.LogCallbackInvoked($"{targetType.Name}.{method.Name}", instances.Length, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HotReload] Error executing callback {targetType.Name}.{method.Name}: {ex.InnerException?.Message ?? ex.Message}");
                }
            }
        }
    }
}
