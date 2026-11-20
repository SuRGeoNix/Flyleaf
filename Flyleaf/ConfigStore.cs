using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlyleafLib;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

/// <summary>Loads settings once and writes the changed application document on exit.</summary>
internal sealed class ConfigStore(string directory)
{
    sealed record Document(int SchemaVersion, AppConfig Application, EngineConfig Engine, PlayerProfiles.Document Profiles);

    sealed class PresentationConverter : JsonConverter<FlyleafUIConfig>
    {
        public override FlyleafUIConfig Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return FlyleafUIConfigSerializer.Deserialize(document.RootElement.GetRawText());
        }

        public override void Write(Utf8JsonWriter writer, FlyleafUIConfig value, JsonSerializerOptions options)
        {
            using var document = JsonDocument.Parse(FlyleafUIConfigSerializer.Serialize(value));
            document.WriteTo(writer);
        }
    }

    static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new PresentationConverter() }
    };

    string PathOf(string name) => Path.Combine(directory, $"Flyleaf.{name}.json");
    string ConfigPath => PathOf("Config");
    Document? loaded;
    bool importLegacy;
    byte[]? baseline;
    AppConfig application = new();
    EngineConfig engine = null!;
    public AppConfig Application => application;
    public EngineConfig Engine => engine;
    public PlayerProfiles Profiles { get; private set; } = null!;
    public string? LoadError { get; private set; }

    public void LoadBeforeEngine(Func<EngineConfig> createEngineDefaults)
    {
        engine = createEngineDefaults();
        loaded = Load(ConfigPath, ReadDocument);
        if (loaded != null)
        {
            application = loaded.Application;
            engine = loaded.Engine;
        }
        importLegacy = loaded == null && LoadError == null;
        if (importLegacy) application = Load(PathOf("AppConfig"), AppConfig.Deserialize) ?? new();
    }

    static Document ReadDocument(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, options);
        if (document?.SchemaVersion != 1 || document.Application == null || document.Engine == null || document.Profiles == null)
            throw new JsonException("Invalid application configuration.");
        return document;
    }

    public void LoadProfiles(Config factoryPlayer)
    {
        var ui = importLegacy ? Load(PathOf("UIConfig"), FlyleafUIConfigSerializer.Deserialize) : null;
        string legacyPath = PathOf("PlayerProfiles");
        var legacy = importLegacy
            ? Load(legacyPath, json => JsonSerializer.Deserialize<PlayerProfiles.LegacyDocument>(json)) : null;
        try { Profiles = new(factoryPlayer, loaded?.Profiles, legacy, ui); }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            RecordError(loaded == null ? legacyPath : ConfigPath, ex);
            Profiles = new(factoryPlayer);
        }
        if (loaded != null && LoadError == null) baseline = Snapshot();
    }

    public bool Save()
    {
        if (LoadError != null) return false;
        var data = Snapshot();
        if (baseline != null && data.AsSpan().SequenceEqual(baseline)) return false;
        SettingsFile.Write(ConfigPath, System.Text.Encoding.UTF8.GetString(data));
        baseline = data;
        return true;
    }

    byte[] Snapshot() => JsonSerializer.SerializeToUtf8Bytes(new Document(1, application, engine, Profiles.Current), options);

    T? Load<T>(string path, Func<string, T?> read) where T : class
    {
        try { return read(File.ReadAllText(path)) ?? throw new JsonException("Missing configuration."); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            RecordError(path, ex);
            return null;
        }
    }

    static bool IsSettingsError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException;
    void RecordError(string path, Exception ex) => LoadError += $"{path}\n{ex.Message}\n\n";
}
