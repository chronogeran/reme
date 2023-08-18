namespace RemeSnes.Hardware
{
    /// <summary>
    /// Represents the S-PPU, or both the PPU 1 and 2, together with associated RAM.
    /// </summary>
    internal class Ppu
    {
        public byte[] RenderedFrameData = new byte[256 * 224 * 2]; // 2 bytes per pixel for now
        public byte[] Vram = new byte[0x10000];
        public byte[] CGRam = new byte[0x200]; // 256 colors, 2 bytes per color, 5 bits per color channel (rgb)
        public byte[] OAM = new byte[544];

        // Tilemap
        // 

        // OAM
        // 128 sprites, 4 bytes per sprite

        private ushort GetOAMX(int index)
        {
            return (ushort)(OAM[index * 4] | (((OAM[512 + index / 4] >> ((index % 4) * 2 + 1)) & 1) << 8));
        }
        private byte GetOAMY(int index)
        {
            return OAM[index * 4 + 1];
        }
        private ushort GetOAMCharacter(int index)
        {
            return (ushort)(OAM[index * 4 + 2] | ((OAM[index * 4 + 3] & 1) << 8));
        }
        private byte GetOAMPalette(int index)
        {
            return (byte)((OAM[index * 4 + 3] >> 1) & 0b111);
        }
        private byte GetOAMPriority(int index)
        {
            return (byte)((OAM[index * 4 + 3] >> 4) & 0b11);
        }
        private bool GetOAMHFlip(int index)
        {
            return (OAM[index * 4 + 3] & 0x40) != 0;
        }
        private bool GetOAMVFlip(int index)
        {
            return (OAM[index * 4 + 3] & 0x80) != 0;
        }
    }
}
