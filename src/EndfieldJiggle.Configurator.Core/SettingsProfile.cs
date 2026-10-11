using System.Text.Json;

namespace EndfieldJiggle.Configurator.Core;

public static class SettingsProfile
{
    private sealed record Document(int Schema, string Product, TouchSettings Settings);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Export(TouchSettings settings)
    {
        settings.Validate();
        return JsonSerializer.Serialize(new Document(1, "EndfieldJiggle", settings), Options);
    }

    public static TouchSettings Import(string json)
    {
        if (json.Length > 64 * 1024)
            throw new InvalidDataException("配置文件过大。");
        Document document = JsonSerializer.Deserialize<Document>(json, Options)
            ?? throw new InvalidDataException("配置文件为空。");
        if (document.Schema != 1 || document.Product != "EndfieldJiggle" || document.Settings is null)
            throw new InvalidDataException("不是受支持的 EndfieldJiggle 配置。");
        document.Settings.Validate();
        return document.Settings;
    }
}
