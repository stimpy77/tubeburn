using System.Text.Json;

namespace TubeBurn.DvdAuthoring;

public sealed class GoldenFixtureComparer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public string SerializeCanonical<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    public bool Matches<T>(T value, string expectedContent)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(expectedContent);

        return string.Equals(Normalize(SerializeCanonical(value)), Normalize(expectedContent), StringComparison.Ordinal);
    }

    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
