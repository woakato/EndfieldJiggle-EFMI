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
    internal void RunIsolatedSmoke(string outputDirectory)
    {
        string output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Preserve an existing smoke-test output directory.");
        Directory.CreateDirectory(output);
        package = InstallablePackage.FromAssembly(Assembly.GetExecutingAssembly(),
            Environment.ProcessPath ?? throw new IOException("No executable path."));
        if (!package.Available) throw new InvalidDataException("Smoke test requires embedded installation resources.");
        string efmi = Path.Combine(output, "FixtureEFMI");
        Directory.CreateDirectory(Path.Combine(efmi, "Mods"));
        File.WriteAllText(Path.Combine(efmi, "d3dx.ini"),
            "[Include]\ninclude_recursive = Mods\nexclude_recursive = DISABLED*\n");
        File.WriteAllText(Path.Combine(efmi, "d3d11.dll"), "isolated-fixture-not-a-loader");
        InstallationResult result = package.Install(package.Preview(efmi), requireStopped: false);
        int installedFiles = package.Preview(efmi).FileCount;
        LoadRuntime(result.RuntimeDirectory);
        SelectEfmi(efmi);
        string outfit = Path.Combine(efmi, "Mods", "SampleOutfit");
        Directory.CreateDirectory(outfit);
        string source = Path.Combine(outfit, "body.ini");
        File.WriteAllText(source,
            "namespace = Example\n[Constants]\n" +
            "; Persisted state is hosted by qaqm_state_0123456789ab.ini\n" +
            "global $variant = $\\QAQM\\Persist\\Bridge_0123456789ab\\variant\n" +
            "[TextureOverrideBody]\nhash = 01234567\ndrawindexed = 12, 3, 0\n");
        File.WriteAllText(source + ".qaqm-persistbak",
            "namespace = Example\n[Constants]\nglobal persist $variant = 1\n");
        SelectOutfit(outfit);
        QaqmRecoveryPlan plan = InspectState();
        if (!plan.CanApply || stateStore is null) throw new InvalidDataException("Sample state recovery not available.");
        stateStore.Apply(plan.Fingerprint, requireStopped: false);
        QaqmRecoveryPlan recovered = InspectState();
        if (recovered.Issues.Count != 0 || recovered.MissingHosts.Count != 0)
            throw new InvalidDataException("Sample recovery not recognized.");
        InspectOutfit();
        if (outfitStore is null || outfitInspection?.SupportedDraws.Count != 1)
            throw new InvalidDataException("Sample native indexed draw not recognized.");
        outfitStore.Apply(outfitInspection.Fingerprint, requireStopped: false);
        if (!outfitStore.Inspect().AlreadyAdapted) throw new InvalidDataException("Adaptation did not persist.");
        outfitStore.Restore(requireStopped: false);
        InspectOutfit();
        int screenshots = 0;
        FrameworkElement content = (FrameworkElement)Content;
        Content = null;
        Border surface = new() { Background = Background, Child = content };
        foreach ((int width, int height, string size) in new[] { (1000, 740, "desktop"), (780, 600, "compact") })
        foreach (TabItem tab in new[] { InstallationTab, TouchTab, OutfitTab })
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
            liveInstallationModified = false, inGameVerified = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
