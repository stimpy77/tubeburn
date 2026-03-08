using System.Buffers.Binary;

namespace TubeBurn.DvdAuthoring;

public abstract record DvdCommand;

public sealed record JumpToTitleCommand(byte TitleNumber) : DvdCommand;

public sealed record JumpToTitlesetCommand(byte TitlesetNumber) : DvdCommand;

public sealed record LinkPreviousProgramCommand() : DvdCommand;

public sealed record SetGeneralParameterCommand(byte Register, ushort Value) : DvdCommand;

public sealed class DvdCommandCodec
{
    public byte[] Encode(DvdCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Span<byte> buffer = stackalloc byte[8];
        buffer.Clear();

        switch (command)
        {
            case JumpToTitleCommand jumpToTitle:
                buffer[0] = 0x30;
                buffer[7] = jumpToTitle.TitleNumber;
                break;

            case JumpToTitlesetCommand jumpToTitleset:
                buffer[0] = 0x31;
                buffer[7] = jumpToTitleset.TitlesetNumber;
                break;

            case LinkPreviousProgramCommand:
                buffer[0] = 0x20;
                buffer[7] = 0x01;
                break;

            case SetGeneralParameterCommand setGeneralParameter:
                buffer[0] = 0x51;
                buffer[1] = setGeneralParameter.Register;
                BinaryPrimitives.WriteUInt16BigEndian(buffer[2..4], setGeneralParameter.Value);
                break;

            default:
                throw new NotSupportedException($"Unsupported DVD command: {command.GetType().Name}");
        }

        return buffer.ToArray();
    }
}
