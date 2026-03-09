using System.Text.Json;

namespace TubeBurn.Infrastructure;

/// <summary>
/// Tracks what was transcoded and at what bitrate, so the pipeline can skip
/// re-transcoding when the cached output already matches the current settings.
/// Stored as manifest.json alongside the transcoded files.
/// </summary>
public sealed class TranscodeManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Dictionary<string, TranscodeManifestEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public sealed class TranscodeManifestEntry
    {
        public string Url { get; set; } = string.Empty;
        public int BitrateKbps { get; set; }
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// Check whether a cached transcode can be reused for the given file, URL, and bitrate.
    /// </summary>
    public bool IsCacheValid(string transcodedPath, string url, int bitrateKbps)
    {
        if (!File.Exists(transcodedPath))
            return false;

        var key = Path.GetFileName(transcodedPath);
        return Entries.TryGetValue(key, out var entry)
            && string.Equals(entry.Url, url, StringComparison.OrdinalIgnoreCase)
            && entry.BitrateKbps == bitrateKbps;
    }

    public void RecordEntry(string transcodedPath, string url, int bitrateKbps)
    {
        var key = Path.GetFileName(transcodedPath);
        Entries[key] = new TranscodeManifestEntry
        {
            Url = url,
            BitrateKbps = bitrateKbps,
            FileSizeBytes = File.Exists(transcodedPath) ? new FileInfo(transcodedPath).Length : 0,
        };
    }

    public static TranscodeManifest Load(string transcodeDirectory)
    {
        var path = Path.Combine(transcodeDirectory, "manifest.json");
        if (!File.Exists(path))
            return new TranscodeManifest();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<TranscodeManifest>(json) ?? new TranscodeManifest();
        }
        catch
        {
            return new TranscodeManifest();
        }
    }

    public void Save(string transcodeDirectory)
    {
        Directory.CreateDirectory(transcodeDirectory);
        var path = Path.Combine(transcodeDirectory, "manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
