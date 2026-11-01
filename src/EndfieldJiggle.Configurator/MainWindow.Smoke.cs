using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EndfieldJiggle.Configurator.Core;

namespace EndfieldJiggle.Configurator;

public partial class MainWindow
{
    internal async Task RunIsolatedSmokeAsync(string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Preserve an existing smoke-test output directory.");
        Directory.CreateDirectory(output);
        if (!Title.EndsWith(InstallablePackage.ApplicationVersion, StringComparison.Ordinal) ||
            !ProductVersionText.Text.EndsWith(InstallablePackage.ApplicationVersion, StringComparison.Ordinal))
            throw new InvalidDataException("UI product version differs from the current assembly.");
        package = InstallablePackage.FromAssembly(Assembly.GetExecutingAssembly(),
            Environment.ProcessPath ?? throw new IOException("No executable path."));
        if (!package.Available) throw new InvalidDataException("Smoke test requires embedded installation resources.");
        string efmi = Path.Combine(output, "FixtureEFMI");
        Directory.CreateDirectory(Path.Combine(efmi, "Mods"));
        File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
            "[Include]\ninclude_recursive = Mods\nexclude_recursive = DISABLED*\n" +
            "[Hunting]\nreload_config = no_modifiers VK_F10\n");
        File.WriteAllText(Path.Combine(efmi, "d3d11.dll"), "isolated-fixture-not-a-loader");
        InstallationResult result = package.Install(package.Preview(efmi), requireStopped: false);
        // Simulate moving a manually extracted package into an arbitrary category.
        File.Delete(Path.Combine(result.RuntimeDirectory, "EndfieldJiggle.install.json"));
        string nestedRuntime = Path.Combine(efmi, "Mods", "Tools", "Touch");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedRuntime)!);
        Directory.Move(result.RuntimeDirectory, nestedRuntime);
        int installedFiles = package.Preview(efmi).FileCount;
        await OpenWorkspaceAsync(efmi);
        if (store?.Root != nestedRuntime || !InstallButton.IsEnabled ||
            installation?.RuntimeDirectory != nestedRuntime)
            throw new InvalidDataException("Nested runtime UI discovery or upgrade preview failed.");
        result = package.Install(installation, requireStopped: false);
        await OpenWorkspaceAsync(nestedRuntime);
        if (!InstallRestoreButton.IsEnabled)
            throw new InvalidDataException("Nested runtime UI recovery is unavailable.");
        string outfit = Path.Combine(efmi, "Mods", "SampleOutfit");
        Directory.CreateDirectory(outfit);
        string source = Path.Combine(outfit, "body.ini");
        File.WriteAllText(source,
            "namespace = Example\n[Constants]\n" +
            "; Persisted state is hosted by qaqm_state_0123456789ab.ini\n" +
            "global $variant = $\\QAQM\\Persist\\Bridge_0123456789ab\\variant\n" +
            "[TextureOverrideBody]\nhash = 01234567\nrun = CustomShaderExtract\n" +
            "ps-t0 = ResourceAppearance\ndrawindexed = 12, 3, 0\n" +
            "[ResourceAppearance]\nfilename = missing-appearance.dds\n" +
            "[CustomShaderExtract]\nvs = helper.hlsl\ndraw = 4096, 0\n");
        File.WriteAllText(Path.Combine(outfit, "helper.hlsl"), "// isolated analysis fixture\n");
        File.WriteAllText(source + ".qaqm-persistbak",
            "namespace = Example\n[Constants]\nglobal persist $variant = 1\n");
        await SelectOutfitAsync(outfit);
        QaqmRecoveryPlan initialState = await InspectStateAsync();
        if (!initialState.CanApply || stateStore is null)
            throw new InvalidDataException("Sample state recovery not available.");
        stateStore.Apply(initialState.Fingerprint, requireStopped: false);
        QaqmRecoveryPlan recovered = await InspectStateAsync();
        if (recovered.Issues.Count != 0 || recovered.MissingHosts.Count != 0)
            throw new InvalidDataException("Sample recovery not recognized.");
        await InspectOutfitAsync();
        if (outfitStore is null || outfitInspection?.SupportedDraws.Count != 1 ||
            outfitInspection.AuxiliaryDraws.Count != 1 || outfitInspection.MissingTextureResources.Count != 1 ||
            outfitInspection.MissingRequiredResources.Count != 0 || !AdaptOutfitButton.IsEnabled ||
            !OutfitDetailsText.Text.Contains("[外观贴图缺失]", StringComparison.Ordinal))
            throw new InvalidDataException("Sample native indexed draw not recognized.");
        outfitStore.Apply(outfitInspection.Fingerprint, requireStopped: false, allowMissingTextures: true);
        await InspectOutfitAsync();
        if (!outfitInspection!.AlreadyAdapted || outfitInspection.MissingTextureResources.Count != 1 ||
            outfitInspection.AuxiliaryDraws.Count != 1 || AdaptOutfitButton.IsEnabled)
            throw new InvalidDataException("Adaptation or persistent resource diagnostics failed.");
        outfitStore.Restore(requireStopped: false);
        await InspectOutfitAsync();
        TouchSettings originalSettings = store!.Read().Settings;
        SetControls(TouchSettings.Default);
        if (ReadControls() != TouchSettings.Default || StartupEnabledCheck.IsChecked != true ||
            DragAltCheck.IsChecked != true || !DragAltCheck.IsEnabled)
            throw new InvalidDataException("Default-on Alt plus mouse controls failed.");
        SetControls(TouchSettings.Default with { DefaultEnabled = false });
        if (ReadControls().DefaultEnabled)
            throw new InvalidDataException("Startup checkbox cannot disable touch.");
        TouchSettings editedSettings = originalSettings with { Drag = new("VK_LBUTTON") };
        SetControls(editedSettings);
        if (ReadControls() != editedSettings) throw new InvalidDataException("UI binding roundtrip failed.");
        ValidateHotSettingsTarget();
        store.Apply(editedSettings, store.Read().Fingerprint, requireStopped: false);
        if (store.Read().Settings != editedSettings) throw new InvalidDataException("Hot settings save failed.");
        store.RestoreLast(requireStopped: false);
        SetControls(originalSettings);
        if (SettingsProfile.Import(SettingsProfile.Export(originalSettings)) != originalSettings)
            throw new InvalidDataException("Settings profile roundtrip failed.");
        await RefreshLibraryAsync();
        if (libraryRows.Count != 1 || (await CheckConflictsAsync(originalSettings)).Count != 0)
            throw new InvalidDataException("Mod library or self-conflict exclusion failed.");
        int screenshots = 0;
        FrameworkElement content = (FrameworkElement)Content;
        Content = null;
        Border surface = new() { Background = Background, Child = content };
        foreach ((int width, int height, string size) in new[] { (1120, 740, "desktop"), (880, 600, "compact") })
        foreach (TabItem tab in new[] { LibraryTab, TouchTab, OutfitTab, DiagnosticsTab, InstallationTab })
        {
            MainTabs.SelectedItem = tab;
            surface.Width = width;
            surface.Height = height;
            surface.Measure(new Size(width, height));
            surface.Arrange(new Rect(0, 0, width, height));
            surface.UpdateLayout();
            RenderTargetBitmap bitmap = new(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string name = size + "-" + MainTabs.Items.IndexOf(tab) + ".png";
            using FileStream image = File.Create(Path.Combine(output, name));
            encoder.Save(image);
            screenshots++;
        }
        stateStore.Restore(requireStopped: false);
        InstallablePackage.RestoreInstallation(result.RuntimeDirectory, requireStopped: false);
        if (!File.Exists(source) || !File.Exists(Path.Combine(efmi, "d3dx.ini")))
            throw new InvalidDataException("Recovery changed unrelated fixture files.");
        File.WriteAllText(Path.Combine(output, "smoke-report.json"), JsonSerializer.Serialize(new
        {
            embeddedInstaller = true, installedFiles,
            screenshots, stateRecovery = true, outfitApplyRestore = true, installerRestore = true,
            hotSettingsSaveRestore = true, profiles = true, modLibrary = true, selfConflictsExcluded = true,
            defaultOnAltMouseControls = true, startupCanBeDisabled = true,
            auxiliaryDrawClassification = true, textureWarningAdaptation = true, textureWarningRetained = true,
            nestedRuntimeDiscovery = true, nestedUpgradeRestore = true,
            productVersion = InstallablePackage.ApplicationVersion,
            liveInstallationModified = false, inGameVerified = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
