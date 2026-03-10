using System.Buffers.Binary;

namespace TubeBurn.DvdAuthoring;

public abstract record DvdCommand;

public sealed record JumpToTitleCommand(byte TitleNumber) : DvdCommand;

public sealed record JumpToTitlesetCommand(byte TitlesetNumber) : DvdCommand;

public sealed record LinkPreviousProgramCommand() : DvdCommand;

public sealed record SetGeneralParameterCommand(byte Register, ushort Value) : DvdCommand;

public sealed record LinkPgcnCommand(ushort Pgcn) : DvdCommand;

public sealed record CallSsVtsmCommand(byte ResumeCell) : DvdCommand;

public sealed record CallSsVmgmCommand(byte ResumeCell) : DvdCommand;

public sealed record JumpSsVmgmCommand() : DvdCommand;

public sealed record JumpSsVtsmCommand(byte Vts) : DvdCommand;

public sealed record JumpVtsTtCommand(byte Title) : DvdCommand;

public sealed record ExitCommand() : DvdCommand;

public sealed record SetHighlightButtonCommand(ushort ButtonNumber) : DvdCommand;

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
                // JumpTT: bits 51-48 = 2, title at vm_getbits(22,7) = byte[5] & 0x7F
                buffer[0] = 0x30;
                buffer[1] = 0x02;
                buffer[5] = jumpToTitle.TitleNumber;
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
                buffer[0] = 0x71;
                buffer[3] = setGeneralParameter.Register;
                BinaryPrimitives.WriteUInt16BigEndian(buffer[4..6], setGeneralParameter.Value);
                break;

            case LinkPgcnCommand linkPgcn:
                buffer[0] = 0x20;
                buffer[1] = 0x04;
                buffer[7] = (byte)linkPgcn.Pgcn;
                break;

            case CallSsVtsmCommand callVtsm:
                // CallSS VTSM root menu, rsm_cell
                // Reference: dvdcompile.c:1129
                buffer[0] = 0x30;
                buffer[1] = 0x08;
                buffer[4] = callVtsm.ResumeCell;
                buffer[5] = 0x83; // VTSM root = 0x80 | 3
                break;

            case CallSsVmgmCommand callVmgm:
                // CallSS VMGM root menu, rsm_cell
                // Reference: dvdcompile.c:1110
                buffer[0] = 0x30;
                buffer[1] = 0x08;
                buffer[4] = callVmgm.ResumeCell;
                buffer[5] = 0x43; // VMGM root = 0x40 | 3
                break;

            case JumpSsVtsmCommand jumpVtsm:
                // JumpSS VTSM vts root menu
                // dvdnav vm.c interprets byte[4] (data1) as: 0 = "current VTS" (requires
                // already being in VTSMenu domain), non-zero = specific VTS number (1-indexed).
                // dvdauthor's dvdcompile.c:753 uses 0-indexed (i1-1), but that's a latent bug
                // that never fires because dvdauthor never emits JumpSS VTSM from FP_PGC for VTS 1.
                // We use 1-indexed so data1 is always non-zero, allowing transitions from
                // FirstPlay/VMGM/VTSMenu domains.
                buffer[0] = 0x30;
                buffer[1] = 0x06;
                buffer[3] = 0x01;
                buffer[4] = jumpVtsm.Vts; // 1-indexed VTS number
                buffer[5] = 0x83; // VTSM root = 0x80 | 3
                break;

            case JumpSsVmgmCommand:
                // JumpSS VMGM root menu — works from any domain (VTSM, VTSTitle, etc.)
                buffer[0] = 0x30;
                buffer[1] = 0x06;
                buffer[5] = 0x43; // VMGM root = 0x40 | 3
                break;

            case JumpVtsTtCommand jumpVtsTt:
                // JumpVTS_TT: bits 51-48 = 3, title at vm_getbits(22,7) = byte[5] & 0x7F
                buffer[0] = 0x30;
                buffer[1] = 0x03;
                buffer[5] = jumpVtsTt.Title;
                break;

            case ExitCommand:
                buffer[0] = 0x30;
                buffer[1] = 0x01;
                break;

            case SetHighlightButtonCommand setHl:
                // SetSTN: set highlight button number
                // 56 00 00 00 00 BB 00 00 (BB = button << 10, big-endian at bytes 4-5)
                buffer[0] = 0x56;
                BinaryPrimitives.WriteUInt16BigEndian(buffer[4..6], (ushort)(setHl.ButtonNumber << 10));
                break;

            default:
                throw new NotSupportedException($"Unsupported DVD command: {command.GetType().Name}");
        }

        return buffer.ToArray();
    }
}
