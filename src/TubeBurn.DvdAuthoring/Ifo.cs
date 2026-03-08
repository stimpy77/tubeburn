using System.Buffers.Binary;
using System.Text;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed record IfoSummary(
    int TitlesetCount,
    int TitleCount,
    VideoStandard Standard,
    DiscMediaKind MediaKind);

public sealed class DvdIfoSerializer
{
    public IfoSummary CreateSummary(TubeBurnProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return new IfoSummary(
            project.Channels.Count,
            project.Videos.Count,
            project.Settings.Standard,
            project.Settings.MediaKind);
    }

    public byte[] Serialize(IfoSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var buffer = new byte[64];
        Encoding.ASCII.GetBytes("TBIFO001").CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), (ushort)summary.TitlesetCount);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10, 2), (ushort)summary.TitleCount);
        buffer[12] = summary.Standard == VideoStandard.Ntsc ? (byte)1 : (byte)2;
        buffer[13] = summary.MediaKind == DiscMediaKind.Dvd5 ? (byte)5 : (byte)9;
        return buffer;
    }
}
