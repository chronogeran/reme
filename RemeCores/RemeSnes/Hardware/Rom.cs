using System;
using Utils;

namespace RemeSnes.Hardware
{
    internal class Rom
    {
        public byte[] Data;
        public RomMapType MapType;

        public ushort ResetVector { get { return Data.ReadShort(0x7ffc + HeaderOffset); } }
        public ushort COPVector { get { return Data.ReadShort(0x7fe4 + HeaderOffset); } }
        public ushort BRKVector { get { return Data.ReadShort(0x7fe6 + HeaderOffset); } }
        public ushort NMIVector { get { return Data.ReadShort(0x7fea + HeaderOffset); } }
        public ushort IRQVector { get { return Data.ReadShort(0x7fee + HeaderOffset); } }


        private int HeaderOffset { get { return MapType == RomMapType.LoRom ? 0 : 0x8000; } }

        public void Initialize(byte[] romFile)
        {
            Data = GetRomData(romFile);
            MapType = DetectRomType(Data);
        }

        private static byte[] GetRomData(byte[] romFile)
        {
            if (HasSmcHeader(romFile))
            {
                var data = new byte[romFile.Length - 0x200];
                Array.Copy(romFile, 0x200, data, 0, data.Length);
                return data;
            }
            else
            {
                return romFile;
            }
        }

        private static RomMapType DetectRomType(byte[] romData)
        {
            var modeByte = romData[0x7fd5];
            if ((modeByte & 0x20) != 0 && (modeByte & 1) == 0)
                return RomMapType.LoRom;
            modeByte = romData[0xffd5];
            if ((modeByte & 0x20) != 0 && (modeByte & 1) == 1)
                return RomMapType.HiRom;
            throw new Exception("Unable to detect rom mapping type");
        }

        private static bool HasSmcHeader(byte[] romFile)
        {
            return romFile.Length % 0x400 == 0x200;
        }
    }

    public enum RomMapType
    {
        LoRom,
        HiRom,
        ExHiRom,
        SA1Rom,
        SDD1Rom,
    }
}
