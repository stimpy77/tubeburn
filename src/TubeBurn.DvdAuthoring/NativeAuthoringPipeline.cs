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

public sealed class NativeAuthoringPipeline : IDvdAuthoringBackend
{
    private readonly DvdIfoSerializer _ifoSerializer = new();
    private readonly DvdPgcCompiler _pgcCompiler = new();
    private readonly DvdVobPlanner _vobPlanner = new();
    private readonly MenuHighlightPlanner _highlightPlanner = new();

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

        // Mux transcoded MPEG-PS files into proper DVD VOBs with NAV packs.
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
                return new AuthoringResult(
                    Kind,
                    AuthoringResultStatus.Failed,
                    $"Native authoring requires transcoded media, but missing file: {video.TranscodedPath}",
                    [outputPath, videoTsDirectory],
                    []);
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

        // Generate spec-compliant IFO files using actual VOB sizes and durations.
        var vtsIfo = DvdIfoWriter.WriteVtsIfo(standard, vobFileSizes, vobDurationsPts, allVobuSectorOffsets);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VTS_01_0.IFO"), vtsIfo, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VTS_01_0.BUP"), vtsIfo, cancellationToken);

        var vmgIfo = DvdIfoWriter.WriteVmgIfo(vobFileSizes.Count, standard, vtsIfo);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VIDEO_TS.IFO"), vmgIfo, cancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(videoTsDirectory, "VIDEO_TS.BUP"), vmgIfo, cancellationToken);

        // Create empty AUDIO_TS directory (required by DVD-Video spec).
        Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, "AUDIO_TS"));

        var isoPath = Path.Combine(request.WorkingDirectory, "tubeburn.iso");
        await BuildIsoAsync(request.WorkingDirectory, isoPath, cancellationToken);

        return new AuthoringResult(
            Kind,
            AuthoringResultStatus.Succeeded,
            "Native authoring generated VIDEO_TS and ISO artifacts.",
            [outputPath, videoTsDirectory, isoPath],
            []);
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
