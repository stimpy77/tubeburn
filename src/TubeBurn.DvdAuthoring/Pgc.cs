using System.Buffers.Binary;
using TubeBurn.Domain;

namespace TubeBurn.DvdAuthoring;

public sealed record CompiledPgc(
    string ChannelName,
    IReadOnlyList<byte[]> PreCommands,
    IReadOnlyList<byte[]> PostCommands,
    int ProgramCount);

public sealed class DvdPgcCompiler
{
    private readonly DvdCommandCodec _codec = new();

    public IReadOnlyList<CompiledPgc> Compile(TubeBurnProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var pgcs = new List<CompiledPgc>();

        foreach (var channel in project.Channels)
        {
            var preCommands = new List<byte[]>();
            var postCommands = new List<byte[]>();

            foreach (var video in channel.Videos.Select((video, index) => (video, index)))
            {
                preCommands.Add(_codec.Encode(new JumpToTitleCommand((byte)(video.index + 1))));
            }

            postCommands.Add(
                project.Channels.Count == 1
                    ? _codec.Encode(new LinkPreviousProgramCommand())
                    : _codec.Encode(new JumpToTitlesetCommand((byte)(pgcs.Count + 1))));

            pgcs.Add(new CompiledPgc(channel.DisplayName, preCommands, postCommands, channel.Videos.Count));
        }

        return pgcs;
    }

    public byte[] SerializeIndex(IReadOnlyList<CompiledPgc> pgcs)
    {
        ArgumentNullException.ThrowIfNull(pgcs);

        var buffer = new byte[4 + (pgcs.Count * 4)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(0, 2), (ushort)pgcs.Count);

        for (var index = 0; index < pgcs.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4 + (index * 4), 2), (ushort)pgcs[index].ProgramCount);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6 + (index * 4), 2), (ushort)pgcs[index].PreCommands.Count);
        }

        return buffer;
    }
}
