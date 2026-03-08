using System.Diagnostics;
using FlaUI.Core.Conditions;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using Xunit.Sdk;

namespace TubeBurn.DesktopUiTests;

[CollectionDefinition("Desktop UI", DisableParallelization = true)]
public sealed class DesktopUiCollectionDefinition;

[Collection("Desktop UI")]
public sealed class DesktopWorkflowSmokeTests
{
    [Fact]
    public void EndToEnd_phase1_workflow_via_real_ui_clicks()
    {
        if (!IsEnabled())
        {
            return;
        }

        var appExePath = ResolveAppExecutable();
        if (!File.Exists(appExePath))
        {
            return;
        }

        using var app = Application.Launch(appExePath);
        using var automation = new UIA3Automation();
        var window = WaitForMainWindow(app, automation);
        var cf = automation.ConditionFactory;

        var uniqueUrl = $"https://example.com/ui-test-{Guid.NewGuid():N}";
        var pendingUrlsTextBox = window.FindFirstDescendant(cf.ByAutomationId("PendingUrlsTextBox"))?.AsTextBox()
                                 ?? throw new XunitException("PendingUrlsTextBox not found.");
        pendingUrlsTextBox.Text = uniqueUrl;

        Click(window, cf, "AddUrlsButton");
        WaitUntil(() => CollectText(window, cf).Any(text => text.Contains(uniqueUrl, StringComparison.OrdinalIgnoreCase)), "Queue did not show added URL.");

        Click(window, cf, "DiscoverToolsButton");
        WaitUntil(
            () => CollectText(window, cf).Any(text => string.Equals(text, "Available", StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(text, "Missing", StringComparison.OrdinalIgnoreCase)),
            "Tool discovery statuses were not rendered.");

        Click(window, cf, "BuildAndBurnButton");
        var workingDirectoryLine = WaitForText(
            window,
            cf,
            text => text.Contains("Working directory:", StringComparison.OrdinalIgnoreCase),
            "Build did not report a working directory.");

        var workingDirectory = workingDirectoryLine.Split("Working directory:", StringSplitOptions.TrimEntries).Last();
        Assert.True(Directory.Exists(workingDirectory), "Working directory was not created.");
        Assert.True(File.Exists(Path.Combine(workingDirectory, "project-state.json")), "project-state.json was not created.");
        Assert.True(File.Exists(Path.Combine(workingDirectory, "project.xml")), "project.xml was not created.");

        app.Close();
    }

    [Fact]
    public void Save_and_Load_project_dialogs_roundtrip_queue_via_ui()
    {
        if (!IsDialogAutomationEnabled())
        {
            return;
        }

        var appExePath = ResolveAppExecutable();
        if (!File.Exists(appExePath))
        {
            return;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"tubeburn-ui-{Guid.NewGuid():N}.json");

        using var app = Application.Launch(appExePath);
        using var automation = new UIA3Automation();
        var window = WaitForMainWindow(app, automation);
        var cf = automation.ConditionFactory;

        var uniqueUrl = $"https://example.com/save-load-{Guid.NewGuid():N}";
        var pendingUrlsTextBox = window.FindFirstDescendant(cf.ByAutomationId("PendingUrlsTextBox"))?.AsTextBox()
                                 ?? throw new XunitException("PendingUrlsTextBox not found.");
        pendingUrlsTextBox.Text = uniqueUrl;

        Click(window, cf, "AddUrlsButton");
        WaitUntil(() => CollectText(window, cf).Any(text => text.Contains(uniqueUrl, StringComparison.OrdinalIgnoreCase)), "Queue did not show added URL before save.");

        Click(window, cf, "SaveProjectButton");
        HandleFileDialog(automation, isSaveDialog: true, fullPath: tempFile);
        WaitUntil(() => File.Exists(tempFile), "Project file was not saved from Save Project dialog.");

        Click(window, cf, "ClearQueueButton");
        WaitUntil(() => !CollectText(window, cf).Any(text => text.Contains(uniqueUrl, StringComparison.OrdinalIgnoreCase)), "Queue still showed saved URL after clear.");

        Click(window, cf, "LoadProjectButton");
        HandleFileDialog(automation, isSaveDialog: false, fullPath: tempFile);
        WaitUntil(() => CollectText(window, cf).Any(text => text.Contains(uniqueUrl, StringComparison.OrdinalIgnoreCase)), "Queue did not restore URL after loading project.");

        app.Close();

        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }
    }

    private static bool IsEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable("TB_RUN_DESKTOP_UI_TESTS");
        return string.Equals(enabled, "1", StringComparison.Ordinal);
    }

    private static bool IsDialogAutomationEnabled()
    {
        if (!IsEnabled())
        {
            return false;
        }

        var enabled = Environment.GetEnvironmentVariable("TB_RUN_DESKTOP_DIALOG_TESTS");
        return string.Equals(enabled, "1", StringComparison.Ordinal);
    }

    private static string ResolveAppExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TB_APP_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TubeBurn.slnx")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            return string.Empty;
        }

        return Path.Combine(current.FullName, "src", "TubeBurn.App", "bin", "Debug", "net10.0", "TubeBurn.App.exe");
    }

    private static Window WaitForMainWindow(Application app, UIA3Automation automation)
    {
        return WaitUntil(
            () =>
            {
                try
                {
                    var window = app.GetMainWindow(automation);
                    return window?.Title?.Contains("TubeBurn", StringComparison.OrdinalIgnoreCase) == true ? window : null;
                }
                catch
                {
                    return null;
                }
            },
            "TubeBurn main window did not appear within timeout.")!;
    }

    private static void Click(Window window, ConditionFactory conditionFactory, string automationId)
    {
        var button = window.FindFirstDescendant(conditionFactory.ByAutomationId(automationId))?.AsButton()
                     ?? throw new XunitException($"Button with automation id '{automationId}' not found.");

        button.Invoke();
    }

    private static List<string> CollectText(Window window, ConditionFactory conditionFactory) =>
        window.FindAllDescendants(conditionFactory.ByControlType(ControlType.Text))
            .Select(element => element.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    private static string WaitForText(
        Window window,
        ConditionFactory conditionFactory,
        Func<string, bool> predicate,
        string failureMessage)
    {
        return WaitUntil(
            () => CollectText(window, conditionFactory).FirstOrDefault(predicate),
            failureMessage)!;
    }

    private static T? WaitUntil<T>(Func<T?> probe, string failureMessage, int timeoutMs = 30000, int intervalMs = 200)
        where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var value = probe();
            if (value is not null)
            {
                return value;
            }

            Thread.Sleep(intervalMs);
        }

        throw new XunitException(failureMessage);
    }

    private static void WaitUntil(Func<bool> probe, string failureMessage, int timeoutMs = 15000, int intervalMs = 200)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (probe())
            {
                return;
            }

            Thread.Sleep(intervalMs);
        }

        throw new XunitException(failureMessage);
    }

    private static void HandleFileDialog(UIA3Automation automation, bool isSaveDialog, string fullPath)
    {
        var cf = automation.ConditionFactory;
        var desktop = automation.GetDesktop();

        var dialog = WaitUntil(
            () =>
            {
                var windows = desktop.FindAllChildren(cf.ByControlType(ControlType.Window));
                return windows
                    .Select(window => window.AsWindow())
                    .FirstOrDefault(window =>
                    {
                        var title = window.Title ?? string.Empty;
                        return isSaveDialog
                            ? title.Contains("Save", StringComparison.OrdinalIgnoreCase)
                            : title.Contains("Open", StringComparison.OrdinalIgnoreCase);
                    });
            },
            isSaveDialog ? "Save file dialog did not appear." : "Open file dialog did not appear.",
            timeoutMs: 15000)!;

        dialog.Focus();

        // Common file dialog usually has at least one Edit control for file name.
        var edit = WaitUntil(
            () => dialog.FindAllDescendants(cf.ByControlType(ControlType.Edit)).LastOrDefault()?.AsTextBox(),
            "File dialog did not expose an editable file name field.",
            timeoutMs: 10000)!;

        edit.Focus();
        edit.Text = fullPath;

        var actionText = isSaveDialog ? "Save" : "Open";
        var actionButton = dialog.FindAllDescendants(cf.ByControlType(ControlType.Button))
            .Select(element => element.AsButton())
            .FirstOrDefault(button =>
            {
                var text = button.Name ?? string.Empty;
                return text.Contains(actionText, StringComparison.OrdinalIgnoreCase);
            });

        if (actionButton is not null)
        {
            actionButton.Invoke();
        }
        else
        {
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
        }
    }
}
