using EndfieldJiggle.Configurator.Core;

internal static class CompatibilityChecks
{
    public static void Run(string fixture, string runtime)
    {
        string parent = Path.Combine(fixture, "Compatibility");
        string Create(string name, string ini)
        {
            string path = Path.Combine(parent, name);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "mod.ini"), ini);
            return path;
        }

        string cosmetic = Create("CosmeticWarnings", """
            namespace = ArbitraryOutfit
            [TextureOverrideMesh]
            run = CustomShaderReadBones
            Resource\MaterialBus\Diffuse = ref ResourceAppearance
            drawindexedinstanced = 9, INSTANCE_COUNT, 6, 0, FIRST_INSTANCE
            drawindexed = 3, 0, 0
            [CustomShaderReadBones]
            vs = helper.hlsl
            draw = 4096, 0
            [CustomShaderMenu]
            vs = helper.hlsl
            ps-t100 = ResourceMenuImage
            draw = 4, 0
            [ResourceAppearance]
            filename = missing-variant.dds
            [ResourceMenuImage]
            filename = missing-menu.png
            """);
        File.WriteAllText(Path.Combine(cosmetic, "helper.hlsl"), "// analysis fixture\n");
        string cosmeticIni = Path.Combine(cosmetic, "mod.ini");
        byte[] original = File.ReadAllBytes(cosmeticIni);
        OutfitAdapterStore store = new(cosmetic, runtime);
        OutfitAdapterInspection inspection = store.Inspect();
        Check(inspection.SupportedDraws.Count == 2 && inspection.AuxiliaryDraws.Count == 2 &&
            inspection.UnsupportedDraws.Count == 0, "native meshes remain eligible beside scoped bone and menu helpers");
        Check(inspection.MissingRequiredResources.Count == 0 && inspection.MissingTextureResources.Count == 2,
            "material-bus and menu images are appearance warnings without outfit-specific names");
        Reject<InvalidDataException>(() => store.Apply(inspection.Fingerprint, requireStopped: false),
            "missing appearances require an explicit acknowledgement");
        Check(File.ReadAllBytes(cosmeticIni).SequenceEqual(original), "unacknowledged warnings do not change source bytes");
        store.Apply(inspection.Fingerprint, requireStopped: false, allowMissingTextures: true);
        string adapted = File.ReadAllText(cosmeticIni);
        Check(adapted.Contains("filename = missing-variant.dds", StringComparison.Ordinal) &&
            adapted.Contains("filename = missing-menu.png", StringComparison.Ordinal) &&
            !adapted.Contains("fallback", StringComparison.OrdinalIgnoreCase),
            "acknowledged adaptation preserves missing texture references instead of substituting artwork");
        OutfitAdapterInspection after = store.Inspect();
        Check(after.AlreadyAdapted && after.AuxiliaryDraws.Count == 2 && after.MissingTextureResources.Count == 2,
            "adapted outfits retain auxiliary classification and missing texture diagnostics");
        Check(!adapted.Contains("[CustomShaderReadBones]\n; EJ-OUTFIT", StringComparison.Ordinal) &&
            !adapted.Contains("[CustomShaderMenu]\n; EJ-OUTFIT", StringComparison.Ordinal) &&
            after.SupportedDraws.Count == 2, "only native indexed draws receive bridge wrappers");
        Reject<InvalidOperationException>(() => store.Apply(after.Fingerprint, requireStopped: false,
            allowMissingTextures: true), "warning acknowledgement does not allow double adaptation");
        store.Restore(requireStopped: false);
        Check(File.ReadAllBytes(cosmeticIni).SequenceEqual(original), "warning-aware adaptation restores exact author bytes");

        OutfitAdapterInspection beforeAsset = store.Inspect();
        File.WriteAllText(Path.Combine(cosmetic, "missing-variant.dds"), "new resource");
        Reject<IOException>(() => store.Apply(beforeAsset.Fingerprint, requireStopped: false, allowMissingTextures: true),
            "a newly restored resource invalidates the inspected dependency fingerprint");

        string aliases = Create("QualifiedAliases", """
            namespace = Consumer
            [TextureOverrideMesh]
            ib = Resource\Producer\Index
            drawindexed = 3, 0, 0
            """);
        File.WriteAllText(Path.Combine(aliases, "producer.ini"), """
            namespace = Producer
            [ResourceIndex]
            [CommandListBind]
            ResourceIndex = ref Resource\Storage\Payload
            """);
        File.WriteAllText(Path.Combine(aliases, "storage.ini"), """
            namespace = Storage
            [ResourcePayload]
            filename = missing-index.dds
            """);
        File.WriteAllText(Path.Combine(aliases, "other.ini"), """
            namespace = Unrelated
            [ResourcePayload]
            filename = unused-image.dds
            """);
        OutfitAdapterStore aliasStore = new(aliases, runtime);
        OutfitAdapterInspection aliasInspection = aliasStore.Inspect();
        Check(aliasInspection.MissingRequiredResources.Count == 1 &&
            aliasInspection.MissingTextureResources.Count == 0 &&
            aliasInspection.MissingUnreferencedResources.Count == 1,
            "qualified resource aliases propagate geometry use without merging unrelated namespaces");
        Reject<InvalidDataException>(() => aliasStore.Apply(aliasInspection.Fingerprint, requireStopped: false,
            allowMissingTextures: true), "texture acknowledgement cannot bypass a missing aliased index resource");

        foreach (string binding in new[] { "ib", "vb0", "vs-t0", "vs-cb1", "cs-t2", "ps-u7" })
        {
            string path = Create("Critical-" + binding, $"""
                [TextureOverrideMesh]
                {binding} = ResourceGeometry
                drawindexed = 3, 0, 0
                [ResourceGeometry]
                filename = missing-data.dds
                """);
            OutfitAdapterStore critical = new(path, runtime);
            OutfitAdapterInspection result = critical.Inspect();
            Check(result.MissingRequiredResources.Count == 1 && result.MissingTextureResources.Count == 0,
                "image extensions do not downgrade geometry or compute dependencies: " + binding);
            Reject<InvalidDataException>(() => critical.Apply(result.Fingerprint, requireStopped: false,
                allowMissingTextures: true), "missing critical binding remains blocked: " + binding);
        }

        foreach ((string name, string type, string filename) in new[]
                 { ("TypedBuffer", "type = StructuredBuffer", "missing.png"),
                   ("UnknownData", "", "missing.buf") })
        {
            string path = Create(name, $"""
                [TextureOverrideMesh]
                ps-t0 = ResourceData
                drawindexed = 3, 0, 0
                [ResourceData]
                {type}
                filename = {filename}
                """);
            OutfitAdapterInspection result = new OutfitAdapterStore(path, runtime).Inspect();
            Check(result.MissingRequiredResources.Count == 1 && result.MissingTextureResources.Count == 0,
                "non-image data remains required even on a pixel binding: " + name);
        }

        string stages = Create("ScopedStageCalls", """
            namespace = SharedCalls
            [TextureOverrideMesh]
            run = CustomShaderExtract
            run = CommandListShared
            drawindexed = 6, 0, 0
            [CustomShaderExtract]
            vs = helper.hlsl
            run = CommandListShared
            draw = 4096, 0
            [CommandListShared]
            ps-t0 = null
            drawindexed = 3, 0, 0
            """);
        File.WriteAllText(Path.Combine(stages, "helper.hlsl"), "// analysis fixture\n");
        OutfitAdapterInspection scoped = new OutfitAdapterStore(stages, runtime).Inspect();
        Check(scoped.SupportedDraws.Count == 1 && scoped.SupportedDraws[0].Section == "TextureOverrideMesh" &&
            scoped.AuxiliaryDraws.Count == 1 &&
            scoped.UnsupportedDraws.Single().Section == "CommandListShared",
            "scoped stages exclude shared helper draws without poisoning the caller's later native draw");

        string nested = Create("NestedScopedStages", """
            namespace = NestedStages
            [TextureOverrideMesh]
            run = CustomShaderOuter
            drawindexed = 3, 0, 0
            [CustomShaderOuter]
            vs = helper.hlsl
            run = CustomShader\NestedStages\Inner
            [CustomShaderInner]
            drawindexed = 3, 0, 0
            """);
        File.WriteAllText(Path.Combine(nested, "helper.hlsl"), "// analysis fixture\n");
        OutfitAdapterInspection nestedInspection = new OutfitAdapterStore(nested, runtime).Inspect();
        Check(nestedInspection.SupportedDraws.Count == 1 && nestedInspection.AuxiliaryDraws.Count == 1 &&
            nestedInspection.AuxiliaryDraws[0].Section == "CustomShaderInner",
            "nested qualified CustomShader calls inherit temporary stages without wrapping helper meshes");

        string shaders = Create("MissingShader", """
            [TextureOverrideMesh]
            run = CustomShaderExtract
            drawindexed = 3, 0, 0
            [CustomShaderExtract]
            vs = missing-helper.hlsl
            draw = 4096, 0
            """);
        OutfitAdapterInspection shaderInspection = new OutfitAdapterStore(shaders, runtime).Inspect();
        Check(shaderInspection.MissingRequiredResources.Count == 1 && shaderInspection.MissingTextureResources.Count == 0,
            "missing helper shaders remain blocking dependencies");

        string drift = Create("DeletedGeometry", """
            [TextureOverrideMesh]
            ib = ResourceIndex
            drawindexed = 3, 0, 0
            [ResourceIndex]
            filename = index.buf
            """);
        string geometryFile = Path.Combine(drift, "index.buf");
        File.WriteAllBytes(geometryFile, new byte[6]);
        OutfitAdapterStore driftStore = new(drift, runtime);
        OutfitAdapterInspection beforeDeletion = driftStore.Inspect();
        File.Delete(geometryFile);
        Reject<IOException>(() => driftStore.Apply(beforeDeletion.Fingerprint, requireStopped: false,
            allowMissingTextures: true), "deleted geometry invalidates the inspection before any backup or write");
        Check(!Directory.Exists(Path.Combine(drift, "DISABLED_EJTouchBackups")),
            "dependency drift fails before starting a write transaction");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private static void Reject<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Console.WriteLine("PASS: " + description); return; }
        throw new InvalidOperationException("FAIL: expected " + typeof(T).Name + ": " + description);
    }
}
