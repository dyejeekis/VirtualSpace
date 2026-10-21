# Known Issues

## Windows builds newer than 26100 may load the wrong VirtualDesktop DLL

The upper bound on the Windows version check in `VirtualSpace/Tools/SystemTool.cs` has been removed, so the app now starts on any build `>= 17763` (Windows 10 1809). Previously it refused to run on builds above 26200.

`VirtualSpace/Program.cs` (`AutoResolver`) picks which embedded `VirtualDesktop11_*.dll` to load from the OS build number:

| Build | DLL loaded |
|-------|------------|
| `<= 22489` | `VirtualDesktop11_21H2.dll` |
| `22621` | `VirtualDesktop11.dll` / `VirtualDesktop11_23H2.dll` / `VirtualDesktop11_22H2_3085.dll` (by revision) |
| `22631` | `VirtualDesktop11_23H2.dll` / `VirtualDesktop11_23H2_3085.dll` (by revision) |
| `26100` | `VirtualDesktop11_23H2.dll` / `VirtualDesktop11_24H2.dll` (by revision) |
| anything else | `VirtualDesktop11.dll` (22H2) |

Any build that isn't listed, such as `26200` or `26300`, falls through to the `default` case and loads the 22H2 `VirtualDesktop11.dll`. Its virtual desktop COM interfaces probably don't match newer Windows builds. The app may start but then crash or misbehave when it enumerates, switches or moves windows between desktops.

### Possible fix

Add a case for the newer build(s) in `Program.cs` that loads the 24H2 DLL, for example:

```csharp
case 26100 or 26200 or 26300:
```

or route every build `>= 26100` to `VirtualDesktop11_24H2.dll`. If the COM interface GUIDs changed in the newer build, a new `VirtualDesktop11_*` project with updated interop definitions will be needed instead.
