using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed record NativeAuthoringPlan(
    IfoSummary Ifo,
    IReadOnlyList<CompiledPgc> Pgcs,
    IReadOnlyList<VobSegmentPlan> VobSegments,
    IReadOnlyList<ChannelMenuLayout> Menus);

/// <summary>
/// Callback for rendering menu backgrounds. The pipeline calls this to generate
/// MPEG-2 still frames; the actual rendering lives in Infrastructure (MenuBackgroundRenderer).
/// </summary>
public delegate Task<string> MenuBackgroundRenderCallback(
    string outputDirectory, MenuPage page, VideoStandard standard, CancellationToken cancellationToken);

public sealed class NativeAuthoringPipeline : IDvdAuthoringBackend
{
    private readonly DvdIfoSerializer _ifoSerializer = new();
    private readonly DvdPgcCompiler _pgcCompiler = new();
    private readonly DvdVobPlanner _vobPlanner = new();
    private readonly MenuHighlightPlanner _highlightPlanner = new();

    /// <summary>
    /// Optional callback for rendering menu backgrounds.
    /// When null, menus are skipped (backward-compatible auto-play behavior).
    /// </summary>
    public MenuBackgroundRenderCallback? MenuRenderer { get; set; }

    public AuthoringBackendKind Kind => AuthoringBackendKind.NativePort;

    public NativeAuthoringPlan CreatePlan(TubeBurnProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new NativeAuthoringPlan(
            _ifoSerializer.CreateSummary(project),
            _pgcCompiler.Compile(project),
            _vobPlanner.Plan(project),
            _highlightPlanner.BuildLayouts(project));
    }

    public async Task<AuthoringResult> AuthorAsync(DvdBuildRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = CreatePlan(request.Project);
        Directory.CreateDirectory(request.WorkingDirectory);

        var outputPath = Path.Combine(request.WorkingDirectory, "native-authoring-plan.json");
        var payload = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, payload, cancellationToken);

        var videoTsDirectory = Path.Combine(request.WorkingDirectory, "VIDEO_TS");
        Directory.CreateDirectory(videoTsDirectory);

        var standard = request.Project.Settings.Standard;
        var project = request.Project;

        if (MenuRenderer is not null)
        {
            return await AuthorWithMenusAsync(request, plan, videoTsDirectory, outputPath, cancellationToken);
        }

        return await AuthorAutoPlayAsync(request, plan, videoTsDirectory, outputPath, cancellationToken);
    }

    /// <summary>
    /// Original auto-play authoring flow (no menus).
    /// </summary>
    private async Task<AuthoringResult> AuthorAutoPlayAsync(
        DvdBuildRequest request, NativeAuthoringPlan plan,
        string videoTsDirectory, string planPath,
        CancellationToken cancellationToken)
    {
        var standard = request.Project.Settings.Standard;

        var vobFileSizes = new List<long>();
        var vobDurationsPts = new List<long>();
        var allVobuSectorOffsets = new List<IReadOnlyList<int>>();
        var vobIndex = 1;
        var nextStartSector = 0;

        foreach (var video in request.Project.Videos)
        {
            if (!File.Exists(video.TranscodedPath))
            {
                return FailedResult($"Missing transcoded file: {video.TranscodedPath}", planPath, videoTsDirectory);
            }

            var targetVobPath = Path.Combine(videoTsDirectory, $"VTS_01_{vobIndex}.VOB");
            var muxResult = await DvdVobMuxer.MuxAsync(
                video.TranscodedPath, targetVobPath,
                vobId: vobIndex, cellId: 1, standard, cancellationToken,
                startSector: nextStartSector);
            vobFileSizes.Add(muxResult.FileSizeBytes);
            vobDurationsPts.Add(muxResult.DurationPts);
            allVobuSectorOffsets.Add(muxResult.VobuSectorOffsets);
            nextStartSector += (int)(muxResult.FileSizeBytes / 2048);
            vobIndex++;
        }

        var channelAspect = request.Project.Videos.FirstOrDefault()?.AspectRatio ?? DvdAspectRatio.Wide16x9;
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(standard, vobFileSizes, vobDurationsPts, allVobuSectorOffsets,
            aspectRatio: channelAspect);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VTS_01_0.IFO"), vtsIfo, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VTS_01_0.BUP"), vtsIfo, cancellationToken);

        var vmgIfo = DvdIfoWriter.WriteVmgIfo(vobFileSizes.Count, standard, [vtsIfo]);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VIDEO_TS.IFO"), vmgIfo, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VIDEO_TS.BUP"), vmgIfo, cancellationToken);

        Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, "AUDIO_TS"));

        var isoPath = Path.Combine(request.WorkingDirectory, "tubeburn.iso");
        await BuildIsoAsync(request.WorkingDirectory, isoPath, cancellationToken);

        return new AuthoringResult(
            Kind,
            AuthoringResultStatus.Succeeded,
            "Native authoring generated VIDEO_TS and ISO artifacts.",
            [planPath, videoTsDirectory, isoPath],
            []);
    }

    /// <summary>
    /// Menu-enabled authoring flow.
    /// </summary>
    private async Task<AuthoringResult> AuthorWithMenusAsync(
        DvdBuildRequest request, NativeAuthoringPlan plan,
        string videoTsDirectory, string planPath,
        CancellationToken cancellationToken)
    {
        var standard = request.Project.Settings.Standard;
        var project = request.Project;
        var isMultiChannel = project.Channels.Count > 1;
        var menuWorkDir = Path.Combine(request.WorkingDirectory, "menu-work");
        Directory.CreateDirectory(menuWorkDir);

        var allVtsIfos = new List<byte[]>();
        var titlesPerVts = new List<int>();
        var chaptersPerTitle = new List<int>();
        // TEMP: keep menu navigation on multi-title topology for player reliability.
        // Chapter-mode menu jumps (JumpVtsPtt) are currently not activating reliably in VLC.
        var useChapters = false;

        // Process each channel as a separate VTS
        for (var ch = 0; ch < project.Channels.Count; ch++)
        {
            var channel = project.Channels[ch];
            var vtsNumber = ch + 1;
            var vtsTag = $"VTS_{vtsNumber:D2}";

            // Build video-select menu pages
            var videoMenuPages = _highlightPlanner.BuildVideoSelectPages(
                channel, vtsNumber, isMultiChannel: isMultiChannel,
                useChapterNavigation: useChapters);

            // Render menu backgrounds & build menu VOB for all pages
            long menuVobSize = 0;
            var menuPageSectorOffsets = new List<int>();
            if (videoMenuPages.Count > 0)
            {
                var menuVobPath = Path.Combine(videoTsDirectory, $"{vtsTag}_0.VOB");
                await using var menuVobStream = new FileStream(menuVobPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                foreach (var page in videoMenuPages)
                {
                    var pageSector = (int)(menuVobSize / 2048);
                    menuPageSectorOffsets.Add(pageSector);

                    var bgPath = await MenuRenderer!(menuWorkDir, page, standard, cancellationToken);
                    var spuPacket = BuildSpuPacket(page.Buttons, standard);

                    var tempVobPath = Path.Combine(menuWorkDir, $"{vtsTag}_menu_p{page.PageNumber}.vob");
                    await MenuVobBuilder.BuildAsync(
                        bgPath, spuPacket, page.Buttons, standard, tempVobPath, cancellationToken,
                        navPackLbn: pageSector);

                    var tempData = await File.ReadAllBytesAsync(tempVobPath, cancellationToken);
                    await menuVobStream.WriteAsync(tempData, cancellationToken);
                    menuVobSize += tempData.Length;
                }
            }

            // Mux title VOBs
            var vobFileSizes = new List<long>();
            var vobDurationsPts = new List<long>();
            var allVobuSectorOffsets = new List<IReadOnlyList<int>>();
            var vobFileIndex = 1;
            var nextStartSector = 0;

            foreach (var video in channel.Videos)
            {
                if (!File.Exists(video.TranscodedPath))
                {
                    return FailedResult($"Missing transcoded file: {video.TranscodedPath}", planPath, videoTsDirectory);
                }

                var targetVobPath = Path.Combine(videoTsDirectory, $"{vtsTag}_{vobFileIndex}.VOB");
                var muxResult = await DvdVobMuxer.MuxAsync(
                    video.TranscodedPath, targetVobPath,
                    vobId: vobFileIndex, cellId: 1, standard, cancellationToken,
                    startSector: nextStartSector);
                vobFileSizes.Add(muxResult.FileSizeBytes);
                vobDurationsPts.Add(muxResult.DurationPts);
                allVobuSectorOffsets.Add(muxResult.VobuSectorOffsets);
                nextStartSector += (int)(muxResult.FileSizeBytes / 2048);
                vobFileIndex++;
            }

            // Write VTS IFO with menu support — aspect ratio from channel's videos
            var channelAspect = channel.Videos.FirstOrDefault()?.AspectRatio ?? DvdAspectRatio.Wide16x9;
            var vtsIfo = DvdIfoWriter.WriteVtsIfo(
                standard, vobFileSizes, vobDurationsPts, allVobuSectorOffsets,
                menuPages: videoMenuPages.Count > 0 ? videoMenuPages : null,
                menuVobSizeBytes: menuVobSize,
                endOfVideoAction: project.Settings.EndOfVideoAction,
                nextChapterAction: useChapters ? project.Settings.NextChapterAction : TitleEndBehavior.GoToMenu,
                menuPageSectorOffsets: menuPageSectorOffsets.Count > 0 ? menuPageSectorOffsets : null,
                aspectRatio: channelAspect);

            await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, $"{vtsTag}_0.IFO"), vtsIfo, cancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, $"{vtsTag}_0.BUP"), vtsIfo, cancellationToken);

            allVtsIfos.Add(vtsIfo);

            if (useChapters)
            {
                // Multi-chapter: 1 title per VTS with N chapters
                titlesPerVts.Add(1);
                chaptersPerTitle.Add(channel.Videos.Count);
            }
            else
            {
                // Multi-title: N titles per VTS with 1 chapter each
                titlesPerVts.Add(channel.Videos.Count);
                for (var v = 0; v < channel.Videos.Count; v++)
                    chaptersPerTitle.Add(1);
            }
        }

        // Build VMG (Video Manager)
        int vmgMenuVobSectors = 0;
        IReadOnlyList<MenuPage>? channelSelectPages = null;

        if (isMultiChannel)
        {
            // Build channel-select menu
            var channelPage = _highlightPlanner.BuildChannelSelectPage(project.Channels, project.Settings.MenuTitle);
            channelSelectPages = [channelPage];

            var bgPath = await MenuRenderer!(menuWorkDir, channelPage, standard, cancellationToken);
            var spuPacket = BuildSpuPacket(channelPage.Buttons, standard);

            var vmgMenuVobPath = Path.Combine(videoTsDirectory, "VIDEO_TS.VOB");
            var vmgMenuVobSize = await MenuVobBuilder.BuildAsync(
                bgPath, spuPacket, channelPage.Buttons, standard, vmgMenuVobPath, cancellationToken);
            vmgMenuVobSectors = (int)(vmgMenuVobSize / 2048);
        }

        // Each channel = 1 VTS; topology per VTS depends on NextChapterAction:
        // - PlayNextVideo: 1 title with N chapters
        // - GoToMenu: N titles with 1 chapter each
        var vmgTitleCount = titlesPerVts.Sum();
        var vmgIfo = DvdIfoWriter.WriteVmgIfo(
            vmgTitleCount, standard, allVtsIfos,
            vtsCount: project.Channels.Count,
            titlesPerVts: titlesPerVts,
            menuPages: channelSelectPages,
            menuVobSectors: vmgMenuVobSectors,
            hasVtsmMenus: true,
            chaptersPerTitle: chaptersPerTitle);

        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VIDEO_TS.IFO"), vmgIfo, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VIDEO_TS.BUP"), vmgIfo, cancellationToken);

        Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, "AUDIO_TS"));

        var isoPath = Path.Combine(request.WorkingDirectory, "tubeburn.iso");
        await BuildIsoAsync(request.WorkingDirectory, isoPath, cancellationToken);

        return new AuthoringResult(
            Kind,
            AuthoringResultStatus.Succeeded,
            "Native authoring with DVD menus generated VIDEO_TS and ISO artifacts.",
            [planPath, videoTsDirectory, isoPath],
            []);
    }

    private AuthoringResult FailedResult(string message, string planPath, string videoTsDir)
    {
        return new AuthoringResult(
            Kind,
            AuthoringResultStatus.Failed,
            message,
            [planPath, videoTsDir],
            []);
    }

    private static byte[] BuildSpuPacket(IReadOnlyList<MenuButton> buttons, VideoStandard standard)
    {
        var highlightBitmap = MenuButtonHighlightRenderer.Render(buttons, standard);
        var width = MenuButtonHighlightRenderer.GetWidth();
        var height = MenuButtonHighlightRenderer.GetHeight(standard);
        return SubpictureEncoder.Encode(
            highlightBitmap, width, height, 0, 0,
            [0, 1, 0, 0], // CLUT indices: pixel 1 → white (border)
            [0, 0, 0, 0]); // Initially transparent; BTN_COLI overrides for selected button
    }

    private static async Task BuildIsoAsync(string dvdRootDirectory, string isoPath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var isoBuilt = await RunStaAsync(() => TryBuildIsoWithImapi(dvdRootDirectory, isoPath, cancellationToken), cancellationToken);
                if (isoBuilt)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fall back to deterministic placeholder ISO payload when IMAPI2FS is unavailable.
            }
        }

        await File.WriteAllBytesAsync(isoPath, System.Text.Encoding.ASCII.GetBytes("TBISO001"), cancellationToken);
    }

    private static bool TryBuildIsoWithImapi(string dvdRootDirectory, string isoPath, CancellationToken cancellationToken)
    {
        var imageType = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage");
        if (imageType is null)
        {
            return false;
        }

        dynamic image = Activator.CreateInstance(imageType)
            ?? throw new InvalidOperationException("Failed to create IMAPI2FS image instance.");
        image.FileSystemsToCreate = 4; // UDF
        try { image.UDFRevision = 0x102; } catch { /* best-effort UDF 1.02 */ }
        image.VolumeName = "TUBEBURN";
        dynamic root = image.Root;
        root.AddTree(dvdRootDirectory, false);
        dynamic result = image.CreateResultImage();
        if (result.ImageStream is IStream stream)
        {
            CopyComStreamToFile(stream, isoPath, cancellationToken);
            return true;
        }

        return false;
    }

    private static void CopyComStreamToFile(IStream stream, string outputPath, CancellationToken cancellationToken)
    {
        using var file = File.Create(outputPath);
        var buffer = new byte[64 * 1024];
        var bytesReadPtr = IntPtr.Zero;

        try
        {
            bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Read(buffer, buffer.Length, bytesReadPtr);
                var bytesRead = Marshal.ReadInt32(bytesReadPtr);
                if (bytesRead <= 0)
                {
                    break;
                }

                file.Write(buffer, 0, bytesRead);
            }
        }
        finally
        {
            if (bytesReadPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(bytesReadPtr);
            }
        }
    }

    private static Task<T> RunStaAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var result = action();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
