using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(TubeBurn.Tests.TestAppBuilder))]

namespace TubeBurn.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::TubeBurn.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
