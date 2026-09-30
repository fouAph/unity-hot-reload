using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HotReload.Editor
{
    /// <summary>
    /// Handles low-level runtime method detouring (JIT function pointer redirection) for x64 architecture.
    /// Redirects calls from an original method to a replacement method without triggering a Unity Domain Reload.
    /// </summary>
    public static class MethodDetour
    {
        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const int PATCH_SIZE_X64 = 12;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private static readonly Dictionary<MethodBase, byte[]> OriginalBytes = new Dictionary<MethodBase, byte[]>();
        private static readonly Dictionary<MethodBase, MethodBase> ActivePatches = new Dictionary<MethodBase, MethodBase>();

        public static int ActivePatchCount => ActivePatches.Count;
        public static IReadOnlyDictionary<MethodBase, MethodBase> Patches => ActivePatches;

        /// <summary>
        /// Detours the original method to point to the replacement method.
        /// </summary>
        public static bool Patch(MethodBase original, MethodBase replacement)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));

            if (IntPtr.Size != 8)
            {
                Debug.LogError("[HotReload] Detouring currently only supports 64-bit architectures (x64).");
                return false;
            }

            try
            {
                // Force JIT compilation of both methods
                RuntimeHelpers.PrepareMethod(original.MethodHandle);
                RuntimeHelpers.PrepareMethod(replacement.MethodHandle);

                IntPtr srcPtr = original.MethodHandle.GetFunctionPointer();
                IntPtr dstPtr = replacement.MethodHandle.GetFunctionPointer();

                if (srcPtr == dstPtr)
                {
                    Debug.LogWarning($"[HotReload] Source and destination pointers are identical for {original.Name}.");
                    return false;
                }

                // Backup original bytes if not already patched
                if (!OriginalBytes.ContainsKey(original))
                {
                    byte[] backup = new byte[PATCH_SIZE_X64];
                    Marshal.Copy(srcPtr, backup, 0, PATCH_SIZE_X64);
                    OriginalBytes[original] = backup;
                }

                // Construct 12-byte x64 jump:
                // 48 B8 [8-byte target pointer] -> movabs rax, dstPtr
                // FF E0                         -> jmp rax
                byte[] patch = new byte[PATCH_SIZE_X64];
                patch[0] = 0x48;
                patch[1] = 0xB8;
                byte[] dstBytes = BitConverter.GetBytes(dstPtr.ToInt64());
                Array.Copy(dstBytes, 0, patch, 2, 8);
                patch[10] = 0xFF;
                patch[11] = 0xE0;

                // Make memory writable
                if (!VirtualProtect(srcPtr, (UIntPtr)PATCH_SIZE_X64, PAGE_EXECUTE_READWRITE, out uint oldProtect))
                {
                    int error = Marshal.GetLastWin32Error();
                    Debug.LogError($"[HotReload] VirtualProtect failed to set PAGE_EXECUTE_READWRITE. Win32Error: {error}");
                    return false;
                }

                // Write the jump patch
                Marshal.Copy(patch, 0, srcPtr, PATCH_SIZE_X64);

                // Restore memory protection
                VirtualProtect(srcPtr, (UIntPtr)PATCH_SIZE_X64, oldProtect, out _);

                // Flush CPU instruction cache
                FlushInstructionCache(GetCurrentProcess(), srcPtr, (UIntPtr)PATCH_SIZE_X64);

                ActivePatches[original] = replacement;
                HotReloadLogger.LogDetouredMethod($"{original.DeclaringType?.Name}.{original.Name}", $"{replacement.DeclaringType?.Name}.{replacement.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotReload] Failed to detour {original.DeclaringType?.Name}.{original.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reverts a previously detoured method back to its original code.
        /// </summary>
        public static bool Revert(MethodBase original)
        {
            if (original == null || !OriginalBytes.TryGetValue(original, out byte[] backup))
            {
                return false;
            }

            try
            {
                IntPtr srcPtr = original.MethodHandle.GetFunctionPointer();
                if (VirtualProtect(srcPtr, (UIntPtr)PATCH_SIZE_X64, PAGE_EXECUTE_READWRITE, out uint oldProtect))
                {
                    Marshal.Copy(backup, 0, srcPtr, PATCH_SIZE_X64);
                    VirtualProtect(srcPtr, (UIntPtr)PATCH_SIZE_X64, oldProtect, out _);
                    FlushInstructionCache(GetCurrentProcess(), srcPtr, (UIntPtr)PATCH_SIZE_X64);

                    OriginalBytes.Remove(original);
                    ActivePatches.Remove(original);
                    HotReloadLogger.LogReverted($"{original.DeclaringType?.Name}.{original.Name}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotReload] Failed to revert {original.Name}: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Reverts all active method detours.
        /// </summary>
        public static void RevertAll()
        {
            var methods = new List<MethodBase>(OriginalBytes.Keys);
            foreach (var method in methods)
            {
                Revert(method);
            }
            OriginalBytes.Clear();
            ActivePatches.Clear();
        }
    }
}
