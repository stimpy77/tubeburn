using System.Reflection;
using TubeBurn.App.ViewModels;

namespace TubeBurn.Tests;

/// <summary>
/// Tests for YouTube URL parsing in CreateMediaBaseName.
/// Uses reflection since the method is private.
/// </summary>
public sealed class UrlParsingTests
{
    private static readonly MethodInfo CreateMediaBaseNameMethod =
        typeof(MainWindowViewModel).GetMethod("CreateMediaBaseName",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CreateMediaBaseName method not found");

    private static string CallCreateMediaBaseName(string url)
    {
        var uri = new Uri(url);
        return (string)CreateMediaBaseNameMethod.Invoke(null, [uri, url])!;
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=QaJ4IhV_9WM")]
    [InlineData("https://youtube.com/watch?v=QaJ4IhV_9WM")]
    public void WatchUrl_extracts_video_id(string url)
    {
        var result = CallCreateMediaBaseName(url);
        Assert.StartsWith("yt-", result);
        Assert.Contains("qaj4ihv", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://youtu.be/o_tjGjsEOgg")]
    public void ShortUrl_extracts_video_id(string url)
    {
        var result = CallCreateMediaBaseName(url);
        Assert.StartsWith("yt-", result);
        Assert.Contains("o_tjgjseogg", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://www.youtube.com/shorts/NFZz1QY3-hY")]
    [InlineData("https://youtube.com/shorts/NFZz1QY3-hY")]
    public void ShortsUrl_extracts_video_id(string url)
    {
        var result = CallCreateMediaBaseName(url);
        Assert.StartsWith("yt-", result);
        Assert.Contains("nfzz1qy3", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void All_three_url_formats_produce_yt_prefix()
    {
        var urls = new[]
        {
            "https://www.youtube.com/watch?v=QaJ4IhV_9WM",
            "https://youtu.be/o_tjGjsEOgg",
            "https://www.youtube.com/shorts/NFZz1QY3-hY",
        };

        foreach (var url in urls)
        {
            var result = CallCreateMediaBaseName(url);
            Assert.StartsWith("yt-", result);
        }
    }

    [Fact]
    public void Different_video_ids_produce_different_base_names()
    {
        var urls = new[]
        {
            "https://www.youtube.com/watch?v=QaJ4IhV_9WM",
            "https://www.youtube.com/watch?v=c6LtVe03h7Q",
            "https://youtu.be/o_tjGjsEOgg",
            "https://www.youtube.com/shorts/NFZz1QY3-hY",
        };

        var results = urls.Select(CallCreateMediaBaseName).ToHashSet();
        Assert.Equal(urls.Length, results.Count);
    }
}
