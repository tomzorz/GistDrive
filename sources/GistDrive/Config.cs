using System.Text.Json;
using System.Text.Json.Serialization;

namespace GistDrive;

/// <summary>
/// What deleting a gist shortcut in File Explorer does on GitHub (REQ-DEL-1).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeletionSetting>))]
public enum DeletionSetting
{
    /// <summary>
    /// The gist stays on GitHub and the gist shortcut comes back.
    /// </summary>
    [JsonStringEnumMemberName("off")]
    Off,

    /// <summary>
    /// The gist file goes too, after the undo window.
    /// </summary>
    [JsonStringEnumMemberName("on")]
    On,
}

/// <summary>
/// Settings from config.json next to the executable (REQ-CFG-1). GistDrive writes the file
/// with the defaults on first run.
/// </summary>
public sealed record Config(
    string RootFolder,
    double UndoWindowSeconds,
    double SecretUploadDelaySeconds,
    DeletionSetting DeletionSetting,
    double RetryPeriodMinutes)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// The defaults from DESIGN.md.
    /// </summary>
    public static Config Default { get; } = new(@"%USERPROFILE%\GistDrive", 30, 0, DeletionSetting.Off, 5);

    [JsonIgnore]
    public string ResolvedRootFolder => Path.GetFullPath(Environment.ExpandEnvironmentVariables(RootFolder));

    [JsonIgnore]
    public TimeSpan UndoWindow => TimeSpan.FromSeconds(Math.Max(0, UndoWindowSeconds));

    [JsonIgnore]
    public TimeSpan SecretUploadDelay => TimeSpan.FromSeconds(Math.Max(0, SecretUploadDelaySeconds));

    [JsonIgnore]
    public TimeSpan RetryPeriod => TimeSpan.FromMinutes(Math.Max(0, RetryPeriodMinutes));

    /// <summary>
    /// Loads config.json from the directory, writing the defaults first when the file does
    /// not exist yet.
    /// </summary>
    public static Config LoadOrCreate(string directory)
    {
        var path = Path.Combine(directory, "config.json");
        if (File.Exists(path))
        {
            var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions);
            if (loaded is not null) return loaded;
        }

        File.WriteAllText(path, JsonSerializer.Serialize(Default, JsonOptions));
        return Default;
    }
}
