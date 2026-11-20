using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyleaf.Controls.WPF;

/// <summary>Versioned UI preferences. The application owns file access and error handling.</summary>
public static class FlyleafUIConfigSerializer
{
    const int CurrentVersion = 1;
    static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Serialize(FlyleafUIConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.Serialize(new Document { SchemaVersion = CurrentVersion, UIConfig = config }, options);
    }

    public static FlyleafUIConfig Deserialize(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, options)
            ?? throw new JsonException("UI configuration must be an object.");
        if (document.SchemaVersion != CurrentVersion)
            throw new JsonException($"Unsupported UI configuration version: {document.SchemaVersion}.");
        return document.UIConfig ?? throw new JsonException("UIConfig is required.");
    }

    public sealed class Document
    {
        public int SchemaVersion { get; set; }
        public FlyleafUIConfig? UIConfig { get; set; }
    }
}
