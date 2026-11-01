using System.Security.Cryptography;
using EndfieldJiggle.Configurator.Core;

internal static class FolderLayoutChecks
{
    internal static int Run(InstallablePackage package, string fixture, string sourceRuntime)
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++;
        }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        void CopyRuntime(string target)
        {
            foreach (string file in Directory.EnumerateFiles(sourceRuntime, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceRuntime, file);
                if (relative == "EndfieldJiggle.install.json") continue;
                string destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
            }
        }

        string[] layouts =
        [
            Path.Combine("Tools", "EndfieldJiggleEFMI-v0.2.5-drop-in-win64"),
            Path.Combine("功能", "触摸", "任意名称"),
            Path.Combine("Tools", "Mods", "Touch"),
            Path.Combine(Enumerable.Range(0, 14).Select(index => "Level" + index).Append("Touch").ToArray()),
        ];
        foreach ((string relative, int index) in layouts.Select((path, index) => (path, index)))
        {
            string efmi = Path.Combine(fixture, "FolderLayout" + index);
            string mods = Path.Combine(efmi, "Mods");
            string runtime = Path.Combine(mods, relative);
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(efmi, "d3dx.ini"), "[Include]\ninclude_recursive = Mods\n");
            File.WriteAllText(Path.Combine(efmi, "d3d11.dll"), "isolated-fixture");
            CopyRuntime(runtime);
            Check(RuntimeLocationLocator.Resolve(efmi) is { Nested: true } location &&
                location.RuntimeDirectory == runtime, "Arbitrary nested layout resolves from EFMI: " + relative);
            Check(RuntimeLocationLocator.Resolve(mods).RuntimeDirectory == runtime,
                "Arbitrary nested layout resolves from Mods: " + relative);
            Check(RuntimeLocationLocator.Resolve(runtime).RuntimeDirectory == runtime &&
                InstallablePackage.ResolveEfmiDirectory(Path.Combine(runtime, "Configurator")) == efmi,
                "Deep runtime resolves its actual EFMI ancestor: " + relative);
            Check(RuntimeLocationLocator.Discover(Path.Combine(runtime, "Configurator"), fixture)
                ?.RuntimeDirectory == runtime, "Portable configurator discovers deep runtime: " + relative);
            Check(ModLibraryStore.Scan(mods).Count == 0,
                "Renamed runtime is excluded from outfit library: " + relative);
            string outfit = Path.Combine(mods, "ExampleOutfit");
            Directory.CreateDirectory(outfit);
            string outfitIni = Path.Combine(outfit, "body.ini");
            string originalOutfit = "namespace = Example\n[TextureOverrideBody]\ndrawindexed = 3, 0, 0\n";
            File.WriteAllText(outfitIni, originalOutfit);
            OutfitAdapterStore adapter = new(outfit, runtime);
            Check(adapter.IsInEfmiModsTree && adapter.Inspect().SupportedDraws.Count == 1,
                "Adaptation uses actual EFMI Mods even below a category named Mods: " + relative);
            adapter.Apply(adapter.Inspect().Fingerprint, requireStopped: false);
            Check(adapter.Inspect().AlreadyAdapted, "Nested runtime adapts an outfit: " + relative);
            adapter.Restore(requireStopped: false);
            Check(File.ReadAllText(outfitIni) == originalOutfit,
                "Nested runtime restores the outfit exactly: " + relative);

            RuntimeSettingsStore store = new(runtime);
            TouchSettings changed = store.Read().Settings with { Physics = new(0.18m, 0.03m, 0.9m) };
            store.Apply(changed, store.Read().Fingerprint, requireStopped: false);
            var before = Directory.EnumerateFiles(runtime, "*.ini").ToDictionary(path => path,
                path => (Hash: Hash(path), Time: File.GetLastWriteTimeUtc(path)));
            InstallationPreview preview = package.Preview(Path.Combine(runtime, "Configurator"));
            Check(preview.IsUpgrade && preview.RuntimeDirectory == runtime,
                "Nested upgrade previews the actual runtime: " + relative);
            InstallationResult upgrade = package.Install(preview, requireStopped: false);
            Check(upgrade.SettingsPreserved && store.Read().Settings == changed &&
                RuntimeLocationLocator.Resolve(efmi).RuntimeDirectory == runtime &&
                !Directory.Exists(Path.Combine(mods, "EndfieldJiggleEFMI")),
                "Nested upgrade keeps settings and creates no second runtime: " + relative);
            InstallablePackage.RestoreInstallation(runtime, requireStopped: false);
            Check(before.All(pair => Hash(pair.Key) == pair.Value.Hash &&
                File.GetLastWriteTimeUtc(pair.Key) == pair.Value.Time) &&
                !InstallablePackage.HasRestorableInstallation(runtime),
                "Nested restore recovers original INI bytes and timestamps: " + relative);

            string disabled = Path.Combine(mods, "DISABLED_Archive", "Old", "Touch");
            CopyRuntime(disabled);
            Check(RuntimeLocationLocator.Resolve(efmi).RuntimeDirectory == runtime,
                "Disabled ancestor excludes its entire nested copy: " + relative);
            string disabledChild = Path.Combine(mods, "Archive", "DISABLED_Old", "Touch");
            CopyRuntime(disabledChild);
            Check(RuntimeLocationLocator.Resolve(efmi).RuntimeDirectory == runtime,
                "Disabled child excludes its nested copy: " + relative);
            string duplicate = Path.Combine(runtime, "Extra", "AnotherRuntime");
            CopyRuntime(duplicate);
            bool rejected = false;
            try { RuntimeLocationLocator.Resolve(efmi); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Nested duplicate cannot be hidden below a valid runtime: " + relative);
        }
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        bool cancelledResolve = false;
        try { RuntimeLocationLocator.Resolve(fixture, cancelled.Token); }
        catch (OperationCanceledException) { cancelledResolve = true; }
        Check(cancelledResolve, "Runtime discovery honors cancellation");
        return checks;
    }
}
