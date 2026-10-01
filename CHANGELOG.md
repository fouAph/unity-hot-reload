# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-10-01

### Fixed
- **Uncompiled Field Inspector Overlay**: Fixed false-positive detection on nested classes (e.g. `[System.Serializable] public class Wheels`), structs, and sibling classes declared in the same source file by recursively inspecting compiled members across all nested and file-declared types.

## [1.0.0] - 2026-09-30

### Added
- **Out-of-Process Roslyn Compiler**: Isolated compilation using Unity's bundled `netcorerun.exe` and `csc.dll` via `.rsp` response file, avoiding Mono image incompatibilities.
- **Native x64 JIT Method Detouring**: 12-byte direct jump (`movabs rax, ptr; jmp rax`) utilizing Win32 `VirtualProtect` and `FlushInstructionCache`.
- **Domain Reload Locking in Play Mode**: Prevents native domain reloads when saving scripts, keeping Play Mode uninterrupted.
- **Hot Reload Editor Window**:
  - **Run Tab**: Live timeline with relative timestamps, file badges, and detoured method signatures.
  - **Start / Stop Engine Controller**: Pause and resume Hot Reload with automatic detour reversion and assembly unlocking.
  - **Settings Tab**: Customizable file watching, domain reload locks, and console logging verbosity levels (Verbose, Normal, Minimal, Silent).
  - **Help Tab**: Architecture diagram and capability guide.
- **Uncompiled Field Inspector Overlay**: Automatically detects and informs about newly added class fields awaiting domain recompile.
- **Runtime Utilities**:
  - `[HotReloaded]` attribute for automatic post-patch lifecycle callbacks.
  - `DynamicFields` extension methods using `ConditionalWeakTable` for state attachment without struct/class layout changes.
