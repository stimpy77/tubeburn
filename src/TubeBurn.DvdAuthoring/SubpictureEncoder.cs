namespace TubeBurn.DvdAuthoring;

/// <summary>
/// Encodes a 2-bit-per-pixel bitmap into a DVD SPU (SubPicture Unit) packet.
/// Based on the DVD-Video specification and dvdauthor's subgen-encode.c.
/// </summary>
public static class SubpictureEncoder
{
    // DVD SPU control commands
    private const byte CMD_FSTA_DSP = 0x00;
    private const byte CMD_SET_COLOR = 0x03;
    private const byte CMD_SET_CONTR = 0x04;
    private const byte CMD_SET_DAREA = 0x05;
    private const byte CMD_SET_DSPXA = 0x06;
    private const byte CMD_END = 0xFF;

    /// <summary>
    /// Encodes a 2-bit bitmap into a complete DVD SPU packet.
    /// </summary>
    /// <param name="pixels">2-bit pixel values (0-3), row-major, width*height elements.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="x0">Display area left coordinate.</param>
    /// <param name="y0">Display area top coordinate.</param>
    /// <param name="clutIndices">4 CLUT palette indices (nibbles for colors 3,2,1,0).</param>
    /// <param name="alphaValues">4 alpha values (nibbles for colors 3,2,1,0).</param>
    public static byte[] Encode(
        byte[] pixels, int width, int height,
        int x0, int y0,
        byte[] clutIndices, byte[] alphaValues)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(clutIndices);
        ArgumentNullException.ThrowIfNull(alphaValues);

        if (pixels.Length != width * height)
            throw new ArgumentException($"Pixel array length {pixels.Length} does not match {width}x{height}");
        if (clutIndices.Length != 4)
            throw new ArgumentException("clutIndices must have 4 entries");
        if (alphaValues.Length != 4)
            throw new ArgumentException("alphaValues must have 4 entries");

        // Max SPU size is 64KB. Allocate generously; trim at end.
        var buffer = new byte[65536];
        var writer = new NibbleWriter(buffer);

        // Skip header (4 bytes): 2B packet size + 2B control sequence offset
        writer.Position = 4;

        // Encode even (top) field rows: y=0,2,4,...
        var offset0 = writer.Position;
        for (var y = 0; y < height; y += 2)
            EncodeRow(writer, pixels, y, width);

        // Encode odd (bottom) field rows: y=1,3,5,...
        var offset1 = writer.Position;
        for (var y = 1; y < height; y += 2)
            EncodeRow(writer, pixels, y, width);

        // Control sequence
        var controlOffset = writer.Position;

        // Write control sequence offset into header
        buffer[2] = (byte)(controlOffset >> 8);
        buffer[3] = (byte)controlOffset;

        // SP_DCSQ_STM: delay = 0 (display immediately)
        writer.WriteByte(0x00);
        writer.WriteByte(0x00);

        // SP_NXT_DCSQ_SA: points to self (last block)
        writer.WriteByte((byte)(controlOffset >> 8));
        writer.WriteByte((byte)controlOffset);

        // DCSQ commands must follow standard order:
        // SET_COLOR → SET_CONTR → SET_DAREA → SET_DSPXA → FSTA_DSP → END
        // (display area and pixel pointers must be set before forcing display)

        // CMD: SET_COLOR (3 bytes: cmd + 2 nibble-pairs)
        writer.WriteByte(CMD_SET_COLOR);
        writer.WriteByte((byte)((clutIndices[3] << 4) | clutIndices[2]));
        writer.WriteByte((byte)((clutIndices[1] << 4) | clutIndices[0]));

        // CMD: SET_CONTR (3 bytes: cmd + 2 nibble-pairs for alpha)
        writer.WriteByte(CMD_SET_CONTR);
        writer.WriteByte((byte)((alphaValues[3] << 4) | alphaValues[2]));
        writer.WriteByte((byte)((alphaValues[1] << 4) | alphaValues[0]));

        // CMD: SET_DAREA (7 bytes: cmd + 6 bytes for coordinates)
        writer.WriteByte(CMD_SET_DAREA);
        var x1 = x0 + width - 1;
        var y1 = y0 + height - 1;
        // x start (12 bits) + x end (12 bits) = 3 bytes
        writer.WriteByte((byte)(x0 >> 4));
        writer.WriteByte((byte)(((x0 & 0x0F) << 4) | ((x1 >> 8) & 0x0F)));
        writer.WriteByte((byte)x1);
        // y start (12 bits) + y end (12 bits) = 3 bytes
        writer.WriteByte((byte)(y0 >> 4));
        writer.WriteByte((byte)(((y0 & 0x0F) << 4) | ((y1 >> 8) & 0x0F)));
        writer.WriteByte((byte)y1);

        // CMD: SET_DSPXA (5 bytes: cmd + 2B offset0 + 2B offset1)
        writer.WriteByte(CMD_SET_DSPXA);
        writer.WriteByte((byte)(offset0 >> 8));
        writer.WriteByte((byte)offset0);
        writer.WriteByte((byte)(offset1 >> 8));
        writer.WriteByte((byte)offset1);

        // CMD: FSTA_DSP (forced start display — must come after area/pointers are set)
        writer.WriteByte(CMD_FSTA_DSP);

        // CMD: END
        writer.WriteByte(CMD_END);

        // Make size even
        if ((writer.Position & 1) != 0)
            writer.WriteByte(CMD_END);

        // Write packet size into header
        var packetSize = writer.Position;
        buffer[0] = (byte)(packetSize >> 8);
        buffer[1] = (byte)packetSize;

        var result = new byte[packetSize];
        Array.Copy(buffer, result, packetSize);
        return result;
    }

    private static void EncodeRow(NibbleWriter writer, byte[] pixels, int y, int width)
    {
        var rowStart = y * width;

        var x = 0;
        while (x < width)
        {
            var color = pixels[rowStart + x];
            var count = 1;
            // DVD SPU RLE: code = (count<<2)|color, variable-length nibble encoding.
            // Decoder reads nibbles: if first nibble >= 4 → 1-nibble code (count 1-3);
            // else reads 2nd; if combined >= 16 → 2-nibble code (count 4-15);
            // else reads 3rd; if combined >= 64 → 3-nibble code (count 16-63);
            // else reads 4th → 4-nibble code (count 64-255). Runs > 255 are split.
            while (x + count < width && pixels[rowStart + x + count] == color && count < 255)
                count++;

            var code = (count << 2) | color;

            if (count >= 64)
            {
                // 16 bits: 4 nibbles (0 0 N N  N N N N  N N C C)
                writer.WriteNibble(0);
                writer.WriteNibble((code >> 8) & 0x0F);
                writer.WriteNibble((code >> 4) & 0x0F);
                writer.WriteNibble(code & 0x0F);
            }
            else if (count >= 16)
            {
                // 12 bits: 3 nibbles (0 0 0 0  N N N N  N N C C)
                writer.WriteNibble(0);
                writer.WriteNibble((code >> 4) & 0x0F);
                writer.WriteNibble(code & 0x0F);
            }
            else if (count >= 4)
            {
                // 8 bits: 2 nibbles (0 0 N N  N N C C)
                writer.WriteNibble((code >> 4) & 0x0F);
                writer.WriteNibble(code & 0x0F);
            }
            else
            {
                // 4 bits: 1 nibble (N N C C)
                writer.WriteNibble(code & 0x0F);
            }

            x += count;
        }

        // End-of-line: byte-align
        writer.FlushNibble();
    }

    /// <summary>
    /// Writes nibbles and bytes into a buffer, tracking nibble alignment.
    /// </summary>
    internal sealed class NibbleWriter(byte[] buffer)
    {
        private bool _highNibble = true;

        public int Position { get; set; }

        public void WriteNibble(int value)
        {
            if (_highNibble)
            {
                buffer[Position] = (byte)((value & 0x0F) << 4);
                _highNibble = false;
            }
            else
            {
                buffer[Position] |= (byte)(value & 0x0F);
                Position++;
                _highNibble = true;
            }
        }

        public void WriteByte(int value)
        {
            FlushNibble();
            buffer[Position++] = (byte)value;
        }

        public void FlushNibble()
        {
            if (!_highNibble)
            {
                Position++;
                _highNibble = true;
            }
        }
    }
}
