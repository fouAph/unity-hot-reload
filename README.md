# ⚡ Custom Hot Reload for Unity (x64)

A high-performance runtime method-detouring Hot Reload tool built for Unity. It enables instant C# method, property, and operator updates during Play Mode or Edit Mode in **< 100 milliseconds** without triggering Unity's heavy Domain Reload.

---

## 📦 Installation via Unity Package Manager (Git URL)

### Option A: Via Unity Package Manager Window (Recommended)
1. In Unity, open **Window > Package Manager**.
2. Click the **[+]** button in the top-left corner and select **Add package from git URL...**
3. Paste your GitHub repository URL:
   ```text
   https://github.com/fouaph/unity-hot-reload.git
   ```
   *(Or specify a tag/version: `https://github.com/fouaph/unity-hot-reload.git#v1.0.0`)*
4. Click **Add**. Unity will download and install the package automatically into `Packages/`.

### Option B: Via `Packages/manifest.json`
Open your project's `Packages/manifest.json` and add the dependency under `"dependencies"`:
```json
{
  "dependencies": {
    "com.fouaph.hotreload": "https://github.com/fouaph/unity-hot-reload.git#v1.0.0",
    ...
  }
}
```

---

## 🔄 How to Update the Package

### 1. Release Strategy (For Package Maintainers)
Follow **Semantic Versioning** (`MAJOR.MINOR.PATCH`):
1. Make your code changes and test locally.
2. Bump the `"version"` field in `package.json` (e.g., from `1.0.0` to `1.0.1`).
3. Document additions/fixes in `CHANGELOG.md`.
4. Commit, push, and create a Git Tag:
   ```bash
   git add .
   git commit -m "Release v1.0.1"
   git tag v1.0.1
   git push origin main --tags
   ```

### 2. Updating in Consumer Unity Projects
- **If pinned to a Git Tag (`#v1.0.0`)**:
  Simply update the tag in `Packages/manifest.json` to `#v1.0.1`. Unity will automatically re-resolve and pull the new release.
- **If tracking the `main` branch**:
  1. Open **Window > Package Manager**.
  2. Select **Hot Reload for Unity**.
  3. Click **Update** in the bottom-right corner.
  4. Alternatively, right-click in `Packages/manifest.json` or delete the entry from `Packages/packages-lock.json` and Unity will re-fetch the latest commit.

### 3. Local Development (Dual Workflow)
While developing new features for Hot Reload, you don't need to push every commit to GitHub:
- Reference your local folder directly in `Packages/manifest.json`:
  ```json
  "com.fouaph.hotreload": "file:../../unity-hot-reload"
  ```
- Changes made locally will update inside Unity instantly! Once tested, push your changes to GitHub and publish a new tag.

---

## 🚀 Features

- ⚡ **Sub-Second Reloads (<100ms)**: Updates method bodies, property getters/setters, and operators on the fly.
- 🔒 **Domain Reload Locking in Play Mode**: Automatically calls `EditorApplication.LockReloadAssemblies()` during Play Mode so Unity won't freeze or kick off its native domain reload when you save `.cs` files.
- ⏯️ **Start & Stop Engine Control**: Pause Hot Reload anytime to restore original compiled bytecodes and unlock assemblies.
- 🔔 **`[HotReloaded]` Lifecycle Callbacks**: Decorate static or instance methods with `[HotReloaded]` to re-run initialization, refresh cached UI, or rebind data when reloaded.
- 📦 **Dynamic Field Storage (`DynamicFields`)**: Attach new runtime state/variables to any object on the fly using `this.SetDynamicField("key", value)` and `this.GetDynamicField<T>("key")` without altering class memory layout.
- 🛡️ **Uncompiled Field Detection**: Inspector overlay alerts you if newly declared serialized class fields require a domain recompile.
- ⚙️ **Preprocessor Symbols**: Dynamic Roslyn compiler automatically inherits project defines (`UNITY_EDITOR`, `DEBUG`, active platform symbols).
- 🔄 **Reversible**: Restore original method bytecodes anytime with a single click.

---

## 🖥️ How to Use

### 1. Open the Hot Reload Window
In the top menu, navigate to:
**Tools > Custom Hot Reload**

- **▶ Start / ■ Stop**: Engages or pauses the live hot reload engine.
- **↻ Compile Now**: Manually triggers compilation of the last modified file or selected script.
- **Timeline**: Displays real-time history of reloads, detoured method signatures, and relative timestamps.
- **Settings**: Configure file auto-watching, domain reload locks, and console logging verbosity (Verbose, Normal, Minimal, Silent).

---

## 🧪 Testing the Live Hot Reload

1. In the Package Manager, select **Hot Reload for Unity** and click **Import Demo** under the **Samples** section (or use `HotReloadDemo.cs`).
2. Attach `HotReloadDemo` to any GameObject in your scene.
3. Enter **Play Mode** in the Unity Editor.
4. Open `HotReloadDemo.cs` in your code editor while the game is running:
   - Change `BonusMultiplier`:
     ```csharp
     public float BonusMultiplier => 5.0f;
     ```
   - Change `GetMessage()`:
     ```csharp
     return "[HOT RELOADED LIVE!] Speed boosted!";
     ```
5. **Save the file (`Ctrl+S`)**.
6. Check the Unity Console & Game view: the method executes with the updated logic instantly without reloading the domain!

---

## 🛠️ Architecture

```
File Save ➔ FileSystemWatcher (Debounced)
                    │
                    ▼
Roslyn Compiler (netcorerun.exe + csc.dll via .rsp)
                    │
                    ▼
Assembly.Load (Dynamic In-Memory DLL)
                    │
                    ▼
MethodDetour (Win32 VirtualProtect + x64 12-byte JMP patch)
                    │
                    ▼
[HotReloaded] Callbacks Dispatched to Scene Instances
```

---

## 📋 Capabilities Matrix

| Supported Live (< 100ms) | How to handle |
| :--- | :--- |
| Method logic & calculations | Instant automatic patch |
| Property getters & setters (`get_`, `set_`) | Instant automatic patch |
| Operator overloads (`+`, `-`, `==`, etc.) | Instant automatic patch |
| Local delegates, lambdas, and LINQ | Instant automatic patch |
| Adding new runtime variables | Use `this.SetDynamicField("name", val)` |
| Post-reload notifications & refresh | Add `[HotReloaded]` to any method |
| Changing class inheritance / interfaces | Requires standard Domain Reload (exit Play Mode or press Recompile) |
| Adding new class-level serialized fields / events | Click `[Recompile]` once to update memory layout |
