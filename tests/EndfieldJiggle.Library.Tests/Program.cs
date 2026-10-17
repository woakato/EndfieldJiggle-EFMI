using EndfieldJiggle.Configurator.Core;

string repository = Path.GetFullPath(args.Length == 0 ? Environment.CurrentDirectory : args[0]);
string fixture = Path.Combine(repository, "reports", "library-tests",
    DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
string mods = Path.Combine(fixture, "EFMI", "Mods");
string runtime = Path.Combine(mods, "EndfieldJiggleEFMI");
Directory.CreateDirectory(runtime);

void Write(string relative, string content)
{
    string path = Path.Combine(fixture, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content);
}

Write("EFMI/d3dx.ini", """
[Menu]
condition = $menu == 1
key = no_ctrl no_shift no_alt VK_F8
keyback = ctrl VK_F9
; key = VK_F12

[ShiftMenu]
key = no_modifiers VK_SHIFT
""");
Write("EFMI/Mods/Characters/Operator/body.ini", """
; EJ-OUTFIT/1 BEGIN D0123456789ABCDEF
[Texture]
key = VK_F8
""");
Write("EFMI/Mods/Characters/Operator/textures/nested.ini", "[Texture]\nkey = VK_F9\n");
Write("EFMI/Mods/Characters/Other/texture/ignored.ini", "[X]\nkey = VK_F10\n");
Write("EFMI/Mods/DISABLED_Pack/disabled.ini", "[X]\nkey = VK_F8\n");
Write("EFMI/Mods/Standalone/main.ini", "[X]\n; key = VK_F8\n");
Write("EFMI/Mods/Standalone/cover.jpg", "preview");
Write("EFMI/Mods/EndfieldJiggleEFMI/EndfieldJiggle.ini", "[KeyToggle]\nkey = VK_F8\n");
Write("EFMI/Mods/ConfiguratorBackups/old/old.ini", "[X]\nkey = VK_F8\n");
Write("EFMI/Core/system.ini", "[System]\nkey = VK_F9\n");

IReadOnlyList<ModLibraryEntry> entries = ModLibraryStore.Scan(mods);
Check(entries.Count == 3, "groups recurse while resources, runtime and backups are excluded");
ModLibraryEntry operatorMod = entries.Single(entry => entry.Name == "Operator");
Check(operatorMod.IniCount == 2 && operatorMod.State == "Adapter" && !operatorMod.Disabled,
    "parent project aggregates descendant INIs and detects adapter marker");
Check(entries.Single(entry => entry.Name == "DISABLED_Pack").Disabled,
    "disabled ancestors are represented");
Check(entries.Single(entry => entry.Name == "Standalone").PreviewPath?.EndsWith("cover.jpg",
    StringComparison.OrdinalIgnoreCase) == true, "only named preview candidates are selected");

TouchSettings settings = new(
    false,
    new KeyBinding("VK_F8"),
    new KeyBinding("VK_F7"),
    new KeyBinding("VK_F6"),
    new KeyBinding("VK_LBUTTON", Shift: true),
    new PhysicsSettings());
IReadOnlyList<KeyConflict> conflicts =
    ModLibraryStore.FindConflicts(Path.Combine(fixture, "EFMI"), runtime, settings);
Check(conflicts.Any(item => item.Binding.Contains("VK_F8", StringComparison.OrdinalIgnoreCase) &&
    item.SourceFile.EndsWith("d3dx.ini", StringComparison.OrdinalIgnoreCase) &&
    item.SourceLine == 3 && item.Condition == "condition = $menu == 1"),
    "case-insensitive key rows preserve line and raw condition");
Check(conflicts.Any(item => item.Binding.Contains("VK_SHIFT", StringComparison.OrdinalIgnoreCase)),
    "shift-plus-left-drag warns about a Shift primary-key menu");
Check(!conflicts.Any(item => item.SourceFile.Contains("DISABLED_", StringComparison.OrdinalIgnoreCase) ||
    item.SourceFile.Contains("ConfiguratorBackups", StringComparison.OrdinalIgnoreCase)),
    "disabled and backup INIs are not scanned");
Check(conflicts.Any(item => item.SourceFile.EndsWith("body.ini", StringComparison.OrdinalIgnoreCase)),
    "enabled mod INIs are checked for potential key conflicts");
Check(!conflicts.Any(item => item.SourceFile.StartsWith(runtime, StringComparison.OrdinalIgnoreCase)),
    "runtime never conflicts with its own bindings");
Write("EFMI/Mods/Standalone/shift.ini", "[KeyMenu]\nkey = SHIFT\n");
Check(ModLibraryStore.FindConflicts(Path.Combine(fixture, "EFMI"), runtime, settings)
    .Any(item => item.SourceFile.EndsWith("shift.ini", StringComparison.OrdinalIgnoreCase)),
    "bare Shift primary-key menu is recognized");
Check(SettingsProfile.Import(SettingsProfile.Export(settings)) == settings,
    "profiles preserve modifiers and parameter values");
bool rejectedWrongProduct = false;
try { SettingsProfile.Import(SettingsProfile.Export(settings).Replace("EndfieldJiggle", "OtherProduct")); }
catch (InvalidDataException) { rejectedWrongProduct = true; }
Check(rejectedWrongProduct, "profiles reject another product");

Write("EFMI/Mods/Standalone/alt.ini", "[KeyMenu]\nkey = ALT\n");
Write("EFMI/Mods/Standalone/alt-modifier.ini", "[KeyMenu]\nkey = no_ctrl no_shift alt VK_MENU\n");
Write("EFMI/Mods/Standalone/ctrl-alt.ini", "[KeyMenu]\nkey = ctrl VK_MENU\n");
IReadOnlyList<KeyConflict> altConflicts =
    ModLibraryStore.FindConflicts(Path.Combine(fixture, "EFMI"), runtime,
        TouchSettings.Default with { Drag = new("VK_MENU") });
Check(altConflicts.Any(item => item.SourceFile.EndsWith("alt.ini", StringComparison.OrdinalIgnoreCase)),
    "bare Alt alias conflicts with a VK_MENU drag");
Check(altConflicts.Any(item => item.SourceFile.EndsWith("alt-modifier.ini", StringComparison.OrdinalIgnoreCase)),
    "Alt primary key is physically held when comparing modifiers");
Check(!altConflicts.Any(item => item.SourceFile.EndsWith("ctrl-alt.ini", StringComparison.OrdinalIgnoreCase)),
    "Ctrl-restricted Alt does not overlap the default no-Ctrl drag");
Write("EFMI/Mods/Standalone/alt-mouse.ini", "[KeyMenu]\nkey = no_ctrl no_shift alt VK_LBUTTON\n");
Check(ModLibraryStore.FindConflicts(Path.Combine(fixture, "EFMI"), runtime, TouchSettings.Default)
    .Any(item => item.SourceFile.EndsWith("alt-mouse.ini", StringComparison.OrdinalIgnoreCase)),
    "Default Alt plus left mouse detects matching mouse chords");

Console.WriteLine($"Library tests passed. Fixtures: {fixture}");

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
