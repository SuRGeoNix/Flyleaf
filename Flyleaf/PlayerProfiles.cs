using System.Text.Json;
using System.Text.Json.Nodes;
using FlyleafLib;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

/// <summary>Application-owned profiles with shared presentation and per-player configuration.</summary>
internal sealed class PlayerProfiles
{
    internal sealed record Profile(Guid Id, string Name, JsonObject Configuration, FlyleafUIConfig Presentation);
    internal sealed record Document(Guid Selected, Guid? ResetSource, Profile[] Profiles);

    internal sealed class Session(Guid id, Config player, FlyleafUIConfig presentation)
    {
        public Guid Id { get; set; } = id;
        public Config Player { get; } = player;
        public FlyleafUIConfig Presentation { get; set; } = presentation;
        public bool PlayerSettingsPending { get; set; }
        internal JsonObject? PlayerBaseline;
    }

    readonly Profile factory;
    Document document;

    public PlayerProfiles(Config factoryPlayer, Document? saved = null)
    {
        factory = new(Guid.Empty, "Factory defaults", Snapshot(factoryPlayer), new());
        if (saved != null)
        {
            Validate(saved);
            document = saved;
            return;
        }

        var initial = new Profile(Guid.NewGuid(), "Default", (JsonObject)factory.Configuration.DeepClone(), factory.Presentation.Clone());
        document = new(initial.Id, null, [initial]);
        Validate(document);
    }

    public Document Current => document;
    public FlyleafUIConfig SelectedPresentation => Find(document.Selected).Presentation;
    public event EventHandler? MetadataChanged;

    public Session Open() => Open(document.Selected);

    public Session Open(Guid id)
    {
        var profile = Find(id);
        return new(id, ReadPlayer(profile), profile.Presentation);
    }

    public void Observe(Session session) => session.PlayerBaseline = Snapshot(session.Player);

    public void Capture(Session session)
    {
        var player = Snapshot(session.Player);
        var profile = Find(session.Id);
        var configuration = MergeEdits(profile.Configuration, session.PlayerBaseline, player);
        if (!ReferenceEquals(configuration, profile.Configuration))
            Update(profile with { Configuration = configuration });
        session.PlayerBaseline = player;
    }

    public void Select(Session session, Guid id)
    {
        if (session.Id == id) return;
        ApplyProfile(session, Find(id));
        document = document with { Selected = id };
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Create(Session session, string name)
    {
        var profile = CreateProfile(RequireName(name), factory);
        ApplyProfile(session, profile);
        document = document with { Selected = profile.Id, Profiles = [.. document.Profiles, profile] };
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Copy(Session session, string name)
    {
        name = RequireName(name);
        Capture(session);
        var profile = CreateProfile(name, Find(session.Id));
        document = document with { Selected = profile.Id, Profiles = [.. document.Profiles, profile] };
        session.Id = profile.Id;
        session.Presentation = profile.Presentation;
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(Session session, string name)
    {
        name = RequireName(name, session.Id);
        Update(Find(session.Id) with { Name = name });
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    public Guid? GetResetSource(Session session)
        => document.ResetSource == session.Id ? null : document.ResetSource;

    public Config GetResetPlayer(Session session)
        => ReadPlayer(GetResetSource(session) is { } id ? Find(id) : factory);

    public void SetResetSource(Guid? id)
    {
        if (id == document.ResetSource) return;
        if (id.HasValue) Find(id.Value);
        document = document with { ResetSource = id };
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(Session session, bool player)
    {
        var source = GetResetSource(session) is { } id ? Find(id) : factory;
        Capture(session);
        var current = Find(session.Id);
        if (player)
        {
            ApplyLive(session.Player, ReadPlayer(source));
            Update(current with { Configuration = (JsonObject)source.Configuration.DeepClone() });
            session.PlayerSettingsPending = true;
        }
        else
        {
            var reset = source.Presentation.Clone();
            var control = current.Presentation;
            control.Appearance = reset.Appearance;
            control.PlaybackBar = reset.PlaybackBar;
            control.Mouse = reset.Mouse;
            control.Queue = reset.Queue;
            control.Subtitles = reset.Subtitles;
            control.OSD = reset.OSD;
            control.SelectedPlugin = reset.SelectedPlugin;
        }
        Observe(session);
        MetadataChanged?.Invoke(this, EventArgs.Empty);
    }

    void ApplyProfile(Session session, Profile profile)
    {
        var player = ReadPlayer(profile);
        Capture(session);
        ApplyLive(session.Player, player);
        Observe(session);
        session.Id = profile.Id;
        session.Presentation = profile.Presentation;
        session.PlayerSettingsPending = true;
    }

    static Profile CreateProfile(string name, Profile source)
        => new(Guid.NewGuid(), name, (JsonObject)source.Configuration.DeepClone(), source.Presentation.Clone());

    string RequireName(string name, Guid? except = null)
    {
        name = name.Trim();
        if (!ValidName(name)) throw new ArgumentException("Enter a name of 1–64 characters. Factory defaults is reserved.");
        if (document.Profiles.Any(p => p.Id != except && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A profile with that name already exists.");
        return name;
    }

    static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64
        && !name.Any(char.IsControl) && !string.Equals(name, "Factory defaults", StringComparison.OrdinalIgnoreCase);

    void Validate(Document candidate)
    {
        if (candidate.Profiles == null || candidate.Profiles.Any(p => p == null || p.Id == Guid.Empty || !ValidName(p.Name)
                || p.Configuration == null || p.Presentation == null)
            || candidate.Profiles.Select(p => p.Id).Distinct().Count() != candidate.Profiles.Length
            || candidate.Profiles.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidate.Profiles.Length
            || !candidate.Profiles.Any(p => p.Id == candidate.Selected)
            || candidate.ResetSource is { } source && !candidate.Profiles.Any(p => p.Id == source))
            throw new JsonException("Invalid profile document.");

        var selected = candidate.Profiles.Single(p => p.Id == candidate.Selected);
        ReadPlayer(selected);
    }

    Profile Find(Guid id) => document.Profiles.SingleOrDefault(p => p.Id == id)
        ?? throw new JsonException("The selected profile does not exist.");

    void Update(Profile profile) =>
        document = document with { Profiles = document.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToArray() };

    static Config ReadPlayer(Profile profile) => Config.Deserialize(profile.Configuration.ToJsonString());
    static JsonObject Snapshot(Config config)
    {
        var snapshot = JsonNode.Parse(config.Serialize())!.AsObject();
        // Opening media populates each processor's filter dictionary. A neutral
        // entry is a runtime default, so it must not override an edit from another window.
        var video = snapshot[nameof(Config.Video)]!.AsObject();
        foreach (var name in new[] { nameof(Config.VideoConfig.FLFilters), nameof(Config.VideoConfig.D3Filters) })
        {
            var filters = video[name]!.AsObject();
            foreach (var (filter, settings) in filters.ToArray())
                if (settings is JsonObject options && JsonNode.DeepEquals(options["Value"], options["Default"]))
                    filters.Remove(filter);
        }
        return snapshot;
    }

    static JsonObject MergeEdits(JsonObject destination, JsonObject? before, JsonObject after)
    {
        if (before == null || JsonNode.DeepEquals(before, after)) return destination;

        var result = (JsonObject)destination.DeepClone();
        foreach (var (name, value) in after)
        {
            if (before.TryGetPropertyValue(name, out var previous) && JsonNode.DeepEquals(previous, value)) continue;
            if (previous is JsonObject oldObject && value is JsonObject newObject && result[name] is JsonObject targetObject)
                result[name] = MergeEdits(targetObject, oldObject, newObject);
            else
                result[name] = value?.DeepClone();
        }
        foreach (var (name, _) in before)
            if (!after.ContainsKey(name)) result.Remove(name);
        return result;
    }

    // Other player settings are loaded when constructing the next window.
    static void ApplyLive(Config current, Config incoming)
    {
        current.Video.ApplyFilterPreferences(incoming.Video);

        // Publish fresh dictionaries so a demuxer already opening can keep reading its snapshot.
        current.Demuxer.FormatOpt = CopyOptions(incoming.Demuxer.FormatOpt);
        current.Demuxer.AudioFormatOpt = CopyOptions(incoming.Demuxer.AudioFormatOpt);
        current.Demuxer.SubtitlesFormatOpt = CopyOptions(incoming.Demuxer.SubtitlesFormatOpt);

        var playback = current.Playback;
        var source = incoming.Playback;
        playback.AutoPlay = source.AutoPlay;
        playback.SeekAccurate = source.SeekAccurate;
        playback.SeekOffset = source.SeekOffset;
        playback.SeekOffset2 = source.SeekOffset2;
        playback.SeekOffset3 = source.SeekOffset3;
        playback.SpeedOffset = source.SpeedOffset;
        playback.SpeedOffset2 = source.SpeedOffset2;
        playback.FolderRecordings = source.FolderRecordings;
        playback.FolderSnapshots = source.FolderSnapshots;
        playback.SnapshotFormat = source.SnapshotFormat;
        current.Audio.DelayOffset = incoming.Audio.DelayOffset;
        current.Audio.DelayOffset2 = incoming.Audio.DelayOffset2;
        current.Audio.VolumeOffset = incoming.Audio.VolumeOffset;
        current.Subtitles.DelayOffset = incoming.Subtitles.DelayOffset;
        current.Subtitles.DelayOffset2 = incoming.Subtitles.DelayOffset2;

        static Dictionary<string, string>? CopyOptions(Dictionary<string, string>? options) =>
            options == null ? null : new(options, options.Comparer);
    }

    #region Migration form v1 to v2 config (Demuxer Audio/Subs Format Options)
    public void MigrateDemuxerFormatOptions(Config current)
    {
        Dictionary<string, string> oldOpts = Config.DemuxerConfig.DefaultFormatOpt();

        foreach (var profile in document.Profiles)
        {
            if (profile.Configuration[nameof(Config.Demuxer)] is not JsonObject demuxer)
                continue;

            MigrateOptions(demuxer, "AudioFormatOpt",
                oldOpts, current.Demuxer.AudioFormatOpt);

            MigrateOptions(demuxer, "SubtitlesFormatOpt",
                oldOpts, current.Demuxer.SubtitlesFormatOpt);
        }
    }

    static void MigrateOptions(JsonObject demuxer, string name, IReadOnlyDictionary<string, string> previous, IReadOnlyDictionary<string, string> current)
    {
        if (demuxer[name] is not JsonObject saved)
        {
            demuxer[name] = JsonSerializer.SerializeToNode(current);
            return;
        }

        foreach (var (key, value) in current)
        {
            if (!saved.TryGetPropertyValue(key, out var node))
            {
                saved[key] = value;
                continue;
            }

            if (previous.TryGetValue(key, out var oldValue) &&
                node is JsonValue json &&
                json.TryGetValue<string>(out var savedValue) &&
                savedValue == oldValue)
            {
                saved[key] = value;
            }
        }

        foreach (var (key, oldValue) in previous)
        {
            if (!current.ContainsKey(key) &&
                saved[key] is JsonValue json &&
                json.TryGetValue<string>(out var savedValue) &&
                savedValue == oldValue)
            {
                saved.Remove(key);
            }
        }
    }
    #endregion
}
