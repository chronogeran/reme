namespace RemeSnes.Hardware
{
    /// <summary>
    /// Represents the S-PPU, or both the PPU 1 and 2, together with associated RAM.
    /// </summary>
    internal class Ppu
    {
        public byte[] RenderedFrameData = new byte[256 * 224 * 2]; // 2 bytes per pixel for now
        public byte[] Vram = new byte[0x10000]; // Tiles and Tile maps
        public byte[] CGRam = new byte[0x200]; // 256 colors, 2 bytes per color, 5 bits per color channel (rgb)
        public byte[] OAM = new byte[544]; // Sprite data

        // TODO decide whether bus sets properties/data on this or whether we can access the hardware registers, or we own some of the hardware registers
        public int BackgroundMode;

        // TODO trigger HBlank, VBlank
        // https://www.raphnet.net/divers/retro_challenge_2019_03/qsnesdoc.html#Reg2107

        private Bus _bus;

        public void SetBus(Bus bus)
        {
            _bus = bus;
        }

        private int GetBGTilemapAddress(int planeIndex)
        {
            return (_bus.HardwareRegistersLow[(int)HardwareRegisterLowOffset.BG1AddressAndSize + planeIndex] & 0b11111100) << 9;
        }
        private int GetBGTilemapSize(int planeIndex)
        {
            return _bus.HardwareRegistersLow[(int)HardwareRegisterLowOffset.BG1AddressAndSize + planeIndex] & 0b11;
        }

        public void RenderFrame()
        {
            // For now this will happen all at once, not worrying about timing

            switch (BackgroundMode)
            {
                case 0:
                    // 4 layers, 4 colors each
                    break;
                case 1:
                    // 2 layers w/ 16 colors, 1 layer w/ 4 colors
                    break;
                case 2:
                    // 2 layers w/ 16 colors, column scrolling
                    break;
                case 3:
                    // 1 layer w/ 128 colors, 1 layer w/ 16 colors
                    break;
                case 4:
                    // 1 layer w/ 128 colors, 1 layer w/ 4 colors, column scrolling
                    break;
                case 5:
                    // 1 layer w/ 16 colors, 1 layer w/ 4 colors, extended resolution
                    break;
                case 6:
                    // 1 layer w/ 32 colors, column scrolling, extended resolution
                    break;
                case 7:
                    // 3D!
                    break;
            }
        }

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
