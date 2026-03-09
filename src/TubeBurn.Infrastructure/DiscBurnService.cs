using TubeBurn.Domain;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TubeBurn.Infrastructure;

public enum DiscBurnOutcome
{
    Succeeded,
    Failed,
    Skipped,
}

public sealed record DiscBurnResult(
    DiscBurnOutcome Outcome,
    string Summary,
    string? CommandPreview = null);

public sealed class DiscBurnService
{
    private readonly IExternalToolRunner _toolRunner;

    public DiscBurnService(IExternalToolRunner? toolRunner = null)
    {
        _toolRunner = toolRunner ?? new ProcessExternalToolRunner();
    }

    public async Task<DiscBurnResult> BurnAsync(
        string workingDirectory,
        ProjectSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(settings);

        if (string.Equals(Environment.GetEnvironmentVariable("TB_DISABLE_BURN"), "1", StringComparison.Ordinal))
        {
            return new DiscBurnResult(DiscBurnOutcome.Succeeded, "Burn execution disabled by TB_DISABLE_BURN=1 (test/development mode).");
        }

        var isoPath = Path.Combine(workingDirectory, "tubeburn.iso");
        var videoTsPath = Path.Combine(workingDirectory, "VIDEO_TS");
        var preferredDevice = string.IsNullOrWhiteSpace(settings.BurnDevice)
            ? Environment.GetEnvironmentVariable("TB_BURN_DEVICE")
            : settings.BurnDevice;
        var requestedSpeed = Math.Clamp(settings.WriteSpeed <= 0 ? 1 : settings.WriteSpeed, 1, 16);
        if (!File.Exists(isoPath))
        {
            if (!Directory.Exists(videoTsPath))
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, "Neither ISO nor VIDEO_TS authoring output exists; burn cannot start.");
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var allowImgBurnFallback = string.Equals(
                Environment.GetEnvironmentVariable("TB_ENABLE_IMGBURN_FALLBACK"),
                "1",
                StringComparison.Ordinal);
            // Try once, and if it fails with a "not ready" style error, wait 10s
            // and auto-retry once before surfacing the failure to the user.
            var nativeBurnAttempt = await TryBurnWithImapiAsync(videoTsPath, preferredDevice, requestedSpeed, cancellationToken);
            if (nativeBurnAttempt.Outcome == DiscBurnOutcome.Succeeded)
            {
                return nativeBurnAttempt;
            }

            // Auto-retry once after a short delay — the drive often just needs
            // a moment to settle after initial disc insertion.
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            var retryAttempt = await TryBurnWithImapiAsync(videoTsPath, preferredDevice, requestedSpeed, cancellationToken);
            if (retryAttempt.Outcome == DiscBurnOutcome.Succeeded)
            {
                return retryAttempt;
            }

            var nativeFailureSummary = $"Native {requestedSpeed}x: {nativeBurnAttempt.Summary} | Auto-retry: {retryAttempt.Summary}";

            if (!allowImgBurnFallback)
            {
                return new DiscBurnResult(
                    DiscBurnOutcome.Failed,
                    $"Windows native burn failed. {nativeFailureSummary} ImgBurn fallback is disabled; set TB_ENABLE_IMGBURN_FALLBACK=1 only for emergency fallback.",
                    null);
            }

            var imgBurnResolution = ExternalToolPathResolver.Resolve("ImgBurn", settings.ImgBurnToolPath);
            var imgBurn = imgBurnResolution.ResolvedPath;
            if (imgBurn is null)
            {
                return new DiscBurnResult(
                    DiscBurnOutcome.Failed,
                    $"Windows native burn failed. {nativeFailureSummary} ImgBurn failover unavailable: {imgBurnResolution.Message}",
                    "ImgBurn /MODE WRITE /SRC <iso> /SPEED <Nx> /START");
            }

            if (!File.Exists(isoPath))
            {
                return new DiscBurnResult(
                    DiscBurnOutcome.Failed,
                    $"Windows native burn failed. {nativeFailureSummary} ImgBurn failover unavailable: no ISO exists for ImgBurn fallback.",
                    "ImgBurn /MODE WRITE /SRC <iso> /SPEED <Nx> /START");
            }

            var imgBurnArgs = new List<string>
            {
                "/MODE",
                "WRITE",
                "/SRC",
                isoPath,
                "/SPEED",
                $"{requestedSpeed}x",
                "/START",
                "/CLOSE",
                "SUCCESS",
            };

            var imgBurnCommandPreview = $"ImgBurn {string.Join(' ', imgBurnArgs)}";
            var imgBurnRun = await _toolRunner.RunAsync(imgBurn, imgBurnArgs, workingDirectory, cancellationToken);
            if (imgBurnRun.ExitCode == 0)
            {
                return new DiscBurnResult(
                    DiscBurnOutcome.Succeeded,
                    $"Disc burn completed through ImgBurn failover at {requestedSpeed}x.",
                    imgBurnCommandPreview);
            }

            return new DiscBurnResult(
                DiscBurnOutcome.Failed,
                $"Windows burn failed. {nativeFailureSummary} | ImgBurn {requestedSpeed}x: failed (exit {imgBurnRun.ExitCode}).",
                imgBurnCommandPreview);
        }

        var growisofsResolution = ExternalToolPathResolver.Resolve("growisofs", settings.GrowisofsToolPath);
        var growisofs = growisofsResolution.ResolvedPath;
        if (growisofs is null)
        {
            return new DiscBurnResult(
                DiscBurnOutcome.Failed,
                $"{growisofsResolution.Message} Browse to growisofs binary or install growisofs/cdrecord backend.",
                "growisofs -dvd-compat -Z <device>=<iso>");
        }

        var device = preferredDevice;
        if (string.IsNullOrWhiteSpace(device))
        {
            return new DiscBurnResult(
                DiscBurnOutcome.Failed,
                "No burn device configured. Choose one in Project Settings or set TB_BURN_DEVICE (example: /dev/dvd).",
                "growisofs -dvd-compat -Z <device>=<iso>");
        }

        var linuxArgs = new List<string>
        {
            "-dvd-compat",
            $"-speed={requestedSpeed}",
            "-Z",
            $"{device}={isoPath}",
        };

        var linuxPreview = $"growisofs {string.Join(' ', linuxArgs)}";
        var linuxRun = await _toolRunner.RunAsync(growisofs, linuxArgs, workingDirectory, cancellationToken);
        if (linuxRun.ExitCode == 0)
        {
            return new DiscBurnResult(DiscBurnOutcome.Succeeded, $"Disc burn completed through growisofs at {requestedSpeed}x.", linuxPreview);
        }

        return new DiscBurnResult(
            DiscBurnOutcome.Failed,
            $"growisofs {requestedSpeed}x failed (exit {linuxRun.ExitCode}).",
            linuxPreview);
    }

    [SupportedOSPlatform("windows")]
    private static DiscBurnResult TryBurnWithImapi(string videoTsPath, string? preferredDevice, int writeSpeed)
    {
        if (!Directory.Exists(videoTsPath))
        {
            return new DiscBurnResult(DiscBurnOutcome.Failed, "VIDEO_TS directory is missing for native burn attempt.");
        }

            var discMasterType = Type.GetTypeFromProgID("IMAPI2.MsftDiscMaster2");
            var recorderType = Type.GetTypeFromProgID("IMAPI2.MsftDiscRecorder2");
            var fsImageType = Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage");
            var dataWriterType = Type.GetTypeFromProgID("IMAPI2.MsftDiscFormat2Data");

            if (discMasterType is null || recorderType is null || fsImageType is null || dataWriterType is null)
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, "IMAPI2 components are unavailable on this system.");
            }

            dynamic discMaster = Activator.CreateInstance(discMasterType)
                ?? throw new InvalidOperationException("Failed to initialize IMAPI2 disc master.");
            if (!(bool)discMaster.IsSupportedEnvironment)
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, "IMAPI2 reports an unsupported burn environment.");
            }

            var recorderCount = (int)discMaster.Count;
            if (recorderCount == 0)
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, "No optical recorder detected for native burn.");
            }

            var normalizedPreferredDevice = NormalizeDeviceToken(preferredDevice);
            string? selectedRecorderId = null;
            string? selectedDevicePath = null;

            for (var index = 0; index < recorderCount; index++)
            {
                var candidateRecorderId = (string)discMaster[index];
                dynamic probeRecorder = Activator.CreateInstance(recorderType)
                    ?? throw new InvalidOperationException("Failed to initialize IMAPI2 recorder.");
                probeRecorder.InitializeDiscRecorder(candidateRecorderId);
                var volumePathNames = ReadVolumePathNames((object)probeRecorder).ToList();

                if (selectedRecorderId is null)
                {
                    selectedRecorderId = candidateRecorderId;
                    selectedDevicePath = volumePathNames.FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(normalizedPreferredDevice))
                {
                    continue;
                }

                var matchedPath = volumePathNames.FirstOrDefault(path =>
                    string.Equals(NormalizeDeviceToken(path), normalizedPreferredDevice, StringComparison.OrdinalIgnoreCase));
                if (matchedPath is null)
                {
                    continue;
                }

                selectedRecorderId = candidateRecorderId;
                selectedDevicePath = matchedPath;
                break;
            }

            if (selectedRecorderId is null)
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, "No usable optical recorder detected for native burn.");
            }

            if (!string.IsNullOrWhiteSpace(normalizedPreferredDevice) && string.IsNullOrWhiteSpace(selectedDevicePath))
            {
                return new DiscBurnResult(
                    DiscBurnOutcome.Failed,
                    $"Selected burn drive '{preferredDevice}' is not available.",
                    null);
            }

            dynamic recorder = Activator.CreateInstance(recorderType)
                ?? throw new InvalidOperationException("Failed to initialize IMAPI2 recorder.");
            recorder.InitializeDiscRecorder(selectedRecorderId);

            // Increase the SCSI command timeout so that disc finalization (lead-out,
            // session close) does not abort prematurely.  The default is far too low
            // for DVD media.  ImgBurn uses similarly generous values.  1800 s (30 min)
            // covers even worst-case DVD-9 finalization at 1x.
            try { recorder.CommandTimeout = 1800; } catch { /* best-effort */ }

            // Suppress Media Change Notification so that Windows AutoPlay does not
            // detect the blank disc and launch ImgBurn (or any other default handler)
            // while we are mid-burn.  Re-enabled in the finally block.
            var mcnDisabled = false;
            try { recorder.DisableMcn(); mcnDisabled = true; } catch { /* best-effort */ }

            // Acquire exclusive access so Explorer, shell extensions, and antivirus
            // cannot probe the drive mid-burn (a common cause of command timeouts).
            var exclusiveAccess = false;
            try { recorder.AcquireExclusiveAccess(false, "TubeBurn"); exclusiveAccess = true; } catch { /* best-effort */ }

            object? comDataWriter = null;
            object? comImageResult = null;
            object? comFsImage = null;
            try
            {
                dynamic fsImage = Activator.CreateInstance(fsImageType)
                    ?? throw new InvalidOperationException("Failed to initialize IMAPI2 file system image.");
                comFsImage = fsImage;
                fsImage.ChooseImageDefaults(recorder);
                fsImage.FileSystemsToCreate = 5; // ISO9660 + UDF bridge for broader Video-DVD compatibility.
                // DVD-Video spec mandates UDF 1.02.  IMAPI2 defaults to a newer UDF
                // revision that libdvdread/VLC and many standalone players reject.
                try { fsImage.UDFRevision = 0x102; } catch { /* best-effort */ }
                fsImage.VolumeName = "DVD_VIDEO";
                dynamic root = fsImage.Root;
                // Explicit /VIDEO_TS and /AUDIO_TS layout for DVD-Video compliance.
                // AddDirectory is void in IMAPI2 COM, so index into root to get the node.
                root.AddDirectory("VIDEO_TS");
                root.AddDirectory("AUDIO_TS");
                dynamic videoTsNode = root["VIDEO_TS"];
                videoTsNode.AddTree(videoTsPath, false);
                dynamic imageResult = fsImage.CreateResultImage();
                comImageResult = imageResult;

                dynamic dataWriter = Activator.CreateInstance(dataWriterType)
                    ?? throw new InvalidOperationException("Failed to initialize IMAPI2 data writer.");
                comDataWriter = dataWriter;
                dataWriter.Recorder = recorder;
                dataWriter.ClientName = "TubeBurn";
                dataWriter.ForceMediaToBeClosed = true;
                // Wait for the drive to become ready.  Slow drives may still be
                // spinning up, seeking, or reading the disc TOC when we reach this
                // point.  Poll IsCurrentMediaSupported with back-off so the initial
                // write commands don't hit a "device not ready" SCSI error.
                WaitForDriveReady(dataWriter, recorder, maxWaitSeconds: 90);

                var mediaSupport = TryDescribeCurrentMediaSupport(dataWriter, recorder);
                if (mediaSupport is not null)
                {
                    return new DiscBurnResult(DiscBurnOutcome.Failed, mediaSupport);
                }
                TrySetImapiWriteSpeed(dataWriter, writeSpeed);
                dataWriter.Write(imageResult.ImageStream);

                var selectedDeviceLabel = string.IsNullOrWhiteSpace(selectedDevicePath) ? "auto-selected drive" : selectedDevicePath;
                return new DiscBurnResult(
                    DiscBurnOutcome.Succeeded,
                    $"Disc burn completed through Windows IMAPI2 native path at {writeSpeed}x ({selectedDeviceLabel}).",
                    $"IMAPI2 native burn {writeSpeed}x ({selectedDeviceLabel})");
            }
            catch (COMException comEx)
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, $"Windows native burn failed (COM {comEx.ErrorCode:X8}): {comEx.Message}");
            }
            catch (Exception ex)
            {
                return new DiscBurnResult(DiscBurnOutcome.Failed, $"Windows native burn failed: {ex.Message}");
            }
            finally
            {
                // Release COM objects so the drive handle is freed and other
                // applications (VLC, Explorer) can access the disc immediately.
                if (comDataWriter is not null)
                    try { Marshal.ReleaseComObject(comDataWriter); } catch { }
                if (comImageResult is not null)
                    try { Marshal.ReleaseComObject(comImageResult); } catch { }
                if (comFsImage is not null)
                    try { Marshal.ReleaseComObject(comFsImage); } catch { }
                if (exclusiveAccess)
                    try { recorder.ReleaseExclusiveAccess(); } catch { }
                if (mcnDisabled)
                    try { recorder.EnableMcn(); } catch { }
                try { Marshal.ReleaseComObject((object)recorder); } catch { }
            }
    }

    [SupportedOSPlatform("windows")]
    private static Task<DiscBurnResult> TryBurnWithImapiAsync(string videoTsPath, string? preferredDevice, int writeSpeed, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<DiscBurnResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                var result = TryBurnWithImapi(videoTsPath, preferredDevice, writeSpeed);
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

    private static void TrySetImapiWriteSpeed(dynamic dataWriter, int writeSpeed)
    {
        if (writeSpeed <= 0)
        {
            return;
        }

        var bytesPerSecond = (long)writeSpeed * 1_385_000L;

        try
        {
            dataWriter.RequestedWriteSpeed = bytesPerSecond;
            return;
        }
        catch
        {
            // Best-effort fallback to alternate IMAPI properties across versions.
        }

        try
        {
            dataWriter.WriteSpeed = bytesPerSecond;
            return;
        }
        catch
        {
            // Best-effort fallback to Speed property if available.
        }

        try
        {
            dataWriter.Speed = bytesPerSecond;
        }
        catch
        {
            // Ignore when speed property is unavailable.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void EjectAndWaitForReinsert(dynamic recorder, int maxWaitSeconds)
    {
        // Eject the tray so the drive fully resets its session state.
        try { recorder.EjectMedia(); } catch { return; /* if eject fails, skip the cycle */ }

        // Poll until media is detected again (user pushes the tray back in).
        // For spring-loaded/slot-load drives there's no CloseTray — the user
        // must physically close it.
        var elapsed = 0;
        while (elapsed < maxWaitSeconds)
        {
            Thread.Sleep(2000);
            elapsed += 2;
            try
            {
                // VolumePathNames is non-empty when the OS has re-mounted the media.
                var paths = ReadVolumePathNames((object)recorder).ToList();
                if (paths.Count > 0)
                    return;
            }
            catch
            {
                // Drive still resetting — keep waiting.
            }
        }
        // Timed out — proceed anyway; WaitForDriveReady will catch if still not ready.
    }

    [SupportedOSPlatform("windows")]
    private static void WaitForDriveReady(dynamic dataWriter, dynamic recorder, int maxWaitSeconds)
    {
        // Some drives (especially older/slower ones) need time after
        // InitializeDiscRecorder before the media is reported as ready.
        // Poll with increasing back-off: 1s, 2s, 3s, …
        var elapsed = 0;
        var interval = 1;
        while (elapsed < maxWaitSeconds)
        {
            try
            {
                bool ready = dataWriter.IsCurrentMediaSupported(recorder);
                if (ready)
                    return;
            }
            catch
            {
                // Drive may throw while still initialising — treat as not-ready.
            }

            Thread.Sleep(interval * 1000);
            elapsed += interval;
            interval = Math.Min(interval + 1, 5);
        }
        // Proceed anyway after timeout — the subsequent media-support check
        // will produce a clear error if the drive truly isn't ready.
    }

    private static string? TryDescribeCurrentMediaSupport(dynamic dataWriter, dynamic recorder)
    {
        bool? isSupported = null;
        try
        {
            isSupported = dataWriter.IsCurrentMediaSupported(recorder);
        }
        catch
        {
            return null;
        }

        if (isSupported != false)
        {
            return null;
        }

        var mediaType = ReadDynamicValueAsString(recorder, "CurrentPhysicalMediaType");
        var mediaState = ReadDynamicValueAsString(recorder, "MediaHeuristicallyBlank");
        var supportedTypes = ReadSupportedMediaTypes(dataWriter);
        var mediaTypeText = string.IsNullOrWhiteSpace(mediaType) ? "unknown" : mediaType;
        var mediaStateText = string.IsNullOrWhiteSpace(mediaState) ? "unknown" : mediaState;
        var supportedTypesText = supportedTypes.Count == 0
            ? "unavailable"
            : string.Join(", ", supportedTypes);

        return $"Current disc media is not supported by the selected recorder. MediaType={mediaTypeText}, HeuristicallyBlank={mediaStateText}, SupportedMediaTypes={supportedTypesText}.";
    }

    private static IReadOnlyList<string> ReadSupportedMediaTypes(dynamic dataWriter)
    {
        var results = new List<string>();
        object? value;
        try
        {
            value = dataWriter.SupportedMediaTypes;
        }
        catch
        {
            return results;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                {
                    results.Add(item.ToString() ?? string.Empty);
                }
            }
        }

        return results.Where(static entry => !string.IsNullOrWhiteSpace(entry)).ToList();
    }

    private static string? ReadDynamicValueAsString(dynamic target, string memberName)
    {
        try
        {
            object? value = memberName switch
            {
                "CurrentPhysicalMediaType" => target.CurrentPhysicalMediaType,
                "MediaHeuristicallyBlank" => target.MediaHeuristicallyBlank,
                _ => null,
            };
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadVolumePathNames(object recorder)
    {
        dynamic dynamicRecorder = recorder;
        object? rawVolumeNames;
        try
        {
            rawVolumeNames = dynamicRecorder.VolumePathNames;
        }
        catch
        {
            yield break;
        }

        if (rawVolumeNames is string singlePath && !string.IsNullOrWhiteSpace(singlePath))
        {
            yield return singlePath;
            yield break;
        }

        if (rawVolumeNames is not IEnumerable volumeNames)
        {
            yield break;
        }

        foreach (var path in volumeNames)
        {
            if (path is string volumePath && !string.IsNullOrWhiteSpace(volumePath))
            {
                yield return volumePath;
            }
        }
    }

    private static string NormalizeDeviceToken(string? device)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return string.Empty;
        }

        return device.Trim().TrimEnd('\\', '/').ToUpperInvariant();
    }
}
