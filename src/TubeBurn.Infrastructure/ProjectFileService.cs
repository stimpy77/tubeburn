using System.Text.Json;
using TubeBurn.Domain;

namespace TubeBurn.Infrastructure;

public sealed class ProjectFileService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public async Task SaveAsync(TubeBurnProject project, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(project, SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<TubeBurnProject> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<TubeBurnProject>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Project file was empty or invalid.");
    }
}
