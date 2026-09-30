using System;

namespace HotReload.Runtime
{
    /// <summary>
    /// Methods marked with [HotReloaded] will automatically be invoked immediately after
    /// their class is hot-reloaded without domain reload.
    /// Can be applied to:
    /// - Static methods: [HotReloaded] static void OnReload() { ... }
    /// - Instance methods on MonoBehaviours: [HotReloaded] void OnReload() { ... }
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class HotReloadedAttribute : Attribute
    {
    }
}
