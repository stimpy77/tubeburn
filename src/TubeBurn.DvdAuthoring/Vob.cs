using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed record VobSegmentPlan(
    string ChannelName,
    int SegmentNumber,
    long SegmentSizeBytes,
    TimeSpan Duration);

public sealed class DvdVobPlanner
{
    public const long MaxVobSizeBytes = 1_000_000_000;

    public IReadOnlyList<VobSegmentPlan> Plan(TubeBurnProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var segments = new List<VobSegmentPlan>();

        foreach (var channel in project.Channels)
        {
            var segmentNumber = 1;

            foreach (var video in channel.Videos)
            {
                var remainingSize = Math.Max(video.EstimatedSizeBytes, 1);
                var remainingDuration = video.Duration;

                while (remainingSize > 0)
                {
                    var segmentSize = Math.Min(remainingSize, MaxVobSizeBytes);
                    var ratio = (double)segmentSize / Math.Max(video.EstimatedSizeBytes, 1);
                    var segmentDuration = TimeSpan.FromSeconds(Math.Max(1, video.Duration.TotalSeconds * ratio));

                    segments.Add(new VobSegmentPlan(channel.DisplayName, segmentNumber++, segmentSize, segmentDuration));

                    remainingSize -= segmentSize;
                    remainingDuration -= segmentDuration;

                    if (remainingDuration <= TimeSpan.Zero)
                    {
                        break;
                    }
                }
            }
        }

        return segments;
    }
}
