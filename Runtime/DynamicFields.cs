using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace HotReload.Runtime
{
    /// <summary>
    /// Provides dynamic runtime field storage for objects.
    /// Because modifying the memory layout of an already compiled C# class at runtime can crash the CLR,
    /// this utility allows attaching new state/variables to any object during hot reload sessions.
    /// </summary>
    public static class DynamicFields
    {
        private static readonly ConditionalWeakTable<object, Dictionary<string, object>> Storage =
            new ConditionalWeakTable<object, Dictionary<string, object>>();

        /// <summary>
        /// Sets a dynamic field on any object.
        /// </summary>
        public static void SetDynamicField(this object target, string fieldName, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("Field name cannot be null or empty", nameof(fieldName));

            var table = Storage.GetOrCreateValue(target);
            lock (table)
            {
                table[fieldName] = value;
            }
        }

        /// <summary>
        /// Retrieves a dynamic field value, or returns the default value if not set.
        /// </summary>
        public static T GetDynamicField<T>(this object target, string fieldName, T defaultValue = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("Field name cannot be null or empty", nameof(fieldName));

            if (Storage.TryGetValue(target, out var table))
            {
                lock (table)
                {
                    if (table.TryGetValue(fieldName, out object val) && val is T typedVal)
                    {
                        return typedVal;
                    }
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Checks if a dynamic field exists on an object.
        /// </summary>
        public static bool HasDynamicField(this object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName)) return false;
            if (Storage.TryGetValue(target, out var table))
            {
                lock (table)
                {
                    return table.ContainsKey(fieldName);
                }
            }
            return false;
        }

        /// <summary>
        /// Clears all dynamic fields attached to an object.
        /// </summary>
        public static void ClearDynamicFields(this object target)
        {
            if (target != null && Storage.TryGetValue(target, out var table))
            {
                lock (table)
                {
                    table.Clear();
                }
            }
        }
    }
}
