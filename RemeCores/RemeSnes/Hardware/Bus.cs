using System;
using Utils;

namespace RemeSnes.Hardware
{
    internal class Bus
    {
        private Wram _wram;
        private Rom _rom;
        private Sram _sram;

        // Hardware Registers
        private byte[] HardwareRegistersLow = new byte[0x84];
        private byte[] HardwareRegistersHigh = new byte[0x20];
        private byte[] DmaRegisters = new byte[0x80];
        // TODO handle triggers from writes to hardware registers

        public Bus(Wram wram, Sram sram, Rom rom)
        {
            _wram = wram;
            _rom = rom;
            _sram = sram;
        }

        private byte[] GetMemory(byte bank, ushort address, out int index)
        {
            if (bank == 0x7e || bank == 0x7f)
            {
                index = address + ((bank - 0x7e) << 16);
                return _wram.Data;
            }
            if (bank < 0x40 || (bank >= 0x80 && bank < 0xc0))
            {
                if (address < 0x2000)
                {
                    index = address;
                    return _wram.Data;
                }
                if (address >= 0x2100 && address < 0x2200)
                {
                    index = address - 0x2100;
                    return HardwareRegistersLow;
                }
                if (address >= 0x3000 && address < 0x4000)
                {
                    // TODO handle expansions
                }
                if (address >= 0x4200 && address < 0x4500)
                {
                    index = address - 0x4200;
                    return HardwareRegistersHigh;
                }
                if (address >= 0x8000)
                {
                    if (bank >= 0x80)
                        bank -= 0x80;

                    if (_rom.MapType == RomMapType.LoRom)
                    {
                        index = address - (bank % 2 == 0 ? 0x8000 : 0) + ((bank / 2) << 16);
                        return _rom.Data;
                    }
                    else if (_rom.MapType == RomMapType.HiRom)
                    {
                        index = address + (bank << 16);
                        return _rom.Data;
                    }
                }
            }
            if ((bank >= 0x40 && bank <= 0x7d) || (bank >= 0xc0 && bank <= 0xfd))
            {
                if (bank >= 0xc0)
                    bank -= 0x80;

                if (_rom.MapType == RomMapType.HiRom)
                {
                    index = address + ((bank - 0x40) << 16);
                    return _rom.Data;
                }

                if (address < 0x8000)
                {
                    if (bank >= 0x70)
                    {
                        index = address + ((bank - 0x70) << 16);
                        return _sram.Data; // TODO map continuously
                    }
                }
                else
                {
                    if (_rom.MapType == RomMapType.LoRom)
                    {
                        index = address - (bank % 2 == 0 ? 0x8000 : 0) + ((bank / 2) << 16);
                        return _rom.Data;
                    }
                    else if (_rom.MapType == RomMapType.HiRom)
                    {
                        index = address + ((bank - 0x40) << 16);
                        return _rom.Data;
                    }
                }
            }
            if (bank == 0xfe || bank == 0xff)
            {
                if (_rom.MapType == RomMapType.HiRom)
                {
                    index = address + ((bank - 0xc0) << 16);
                    return _rom.Data;
                }

                if (address < 0x8000)
                {
                    index = address + ((bank - 0xf0) << 16);
                    return _sram.Data; // TODO map continuously
                }
                else
                {
                    index = address - (bank % 2 == 0 ? 0x8000 : 0) + (0x3f << 16);
                    return _rom.Data;
                }
            }

            throw new Exception($"Address not handled: {address:x6}");
        }

        public byte ReadByte(byte bank, ushort address)
        {
            return GetMemory(bank, address, out int index)[index];
        }

        public ushort ReadShort(byte bank, ushort address)
        {
            return GetMemory(bank, address, out int index).ReadShort(index);
        }

        public uint ReadLong(byte bank, ushort address)
        {
            return GetMemory(bank, address, out int index).ReadLong(index);
        }

        public byte ReadByte(uint longAddress)
        {
            return ReadByte((byte)(longAddress >> 16), (ushort)longAddress);
        }

        public ushort ReadShort(uint longAddress)
        {
            return ReadShort((byte)(longAddress >> 16), (ushort)longAddress);
        }

        public uint ReadLong(uint longAddress)
        {
            return ReadLong((byte)(longAddress >> 16), (ushort)longAddress);
        }

        public void WriteByte(byte bank, ushort address, byte value)
        {
            GetMemory(bank, address, out int index)[index] = value;
        }

        public void WriteShort(byte bank, ushort address, ushort value)
        {
            GetMemory(bank, address, out int index).WriteShort(index, value);
        }

        public void WriteByte(uint longAddress, byte value)
        {
            WriteByte((byte)(longAddress >> 16), (ushort)longAddress, value);
        }

        public void WriteShort(uint longAddress, ushort value)
        {
            WriteShort((byte)(longAddress >> 16), (ushort)longAddress, value);
        }

        // Called after the write occurs.
        private byte SignedMultiplyLow;
        private byte SignedMultiplyHigh;
        private bool WriteToSignedMultiplyLowNext = true;
        private void HandleHardwareRegisterWrite(byte bank, ushort address)
        {
            if (bank < 0x40 || (bank >= 0x80 && bank < 0xc0))
            {
                if (address >= 0x2100 && address < 0x2200)
                {
                    var offset = (HardwareRegisterLowOffset)(address - 0x2100);

                    // TODO check if we're in Mode 7
                    // Signed multiplication
                    if (offset == HardwareRegisterLowOffset.Mode7MatrixParameterAOrMultiplicand)
                    {
                        if (WriteToSignedMultiplyLowNext)
                            SignedMultiplyLow = HardwareRegistersLow[(int)offset];
                        else
                            SignedMultiplyHigh = HardwareRegistersLow[(int)offset];
                    }
                    else if (offset == HardwareRegisterLowOffset.Mode7MatrixParameterBOrMultiplier)
                    {
                        var multiplicand = (short)(SignedMultiplyLow | (SignedMultiplyHigh << 8));
                        var result = multiplicand * HardwareRegistersLow[(int)offset];
                        HardwareRegistersLow.WriteLong((int)HardwareRegisterLowOffset.MultiplicationResultLow, result);
                    }
                }
                if (address >= 0x4200 && address < 0x4500)
                {
                    var offset = (HardwareRegisterHighOffset)(address - 0x4200);

                    // TODO delay results
                    // Multiplication
                    if (offset == HardwareRegisterHighOffset.Multiplier)
                    {
                        var multResult = HardwareRegistersHigh[(int)HardwareRegisterHighOffset.Multiplicand]
                            * HardwareRegistersHigh[(int)HardwareRegisterHighOffset.Multiplier];
                        HardwareRegistersHigh.WriteShort((int)HardwareRegisterHighOffset.ProductOrRemainderLow, (ushort)multResult);
                    }
                    // Division
                    else if (offset == HardwareRegisterHighOffset.Divisor)
                    {
                        var dividend = HardwareRegistersHigh.ReadShort((int)HardwareRegisterHighOffset.DividendLow);
                        var divisor = HardwareRegistersHigh.ReadByte((int)HardwareRegisterHighOffset.Divisor);
                        HardwareRegistersHigh.WriteShort((int)HardwareRegisterHighOffset.DivideResultLow,
                            (ushort)(dividend / divisor));
                        HardwareRegistersHigh.WriteShort((int)HardwareRegisterHighOffset.ProductOrRemainderLow,
                            (ushort)(dividend % divisor));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Offset from 0x2100
    /// </summary>
    public enum HardwareRegisterLowOffset
    {
        ScreenDisplay = 0x00,
        OAMSizeAndDataArea = 0x01,
        OAMAddress = 0x02,
        OAMDataWrite = 0x04,
        BGModeAndTileSizeSetting = 0x05,
        MosaicSizeAndBGEnable = 0x06,
        BG1AddressAndSize = 0x07,
        BG2AddressAndSize = 0x08,
        BG3AddressAndSize = 0x09,
        BG4AddressAndSize = 0x0a,
        BG12TileDataDesignation = 0x0b,
        BG34TileDataDesignation = 0x0c,
        BG1HorizontalScrollOffset = 0x0d,
        BG1VerticalScrollOffset = 0x0e,
        BG2HorizontalScrollOffset = 0x0f,
        BG2VerticalScrollOffset = 0x10,
        BG3HorizontalScrollOffset = 0x11,
        BG3VerticalScrollOffset = 0x12,
        BG4HorizontalScrollOffset = 0x13,
        BG4VerticalScrollOffset = 0x14,
        VRAMAddressIncrement = 0x15,
        VRAMAddress = 0x16,
        VRAMData = 0x18,
        InitialMode7Setting = 0x1a,
        Mode7MatrixParameterAOrMultiplicand = 0x1b,
        Mode7MatrixParameterBOrMultiplier = 0x1c,
        Mode7MatrixParameterC = 0x1d,
        Mode7MatrixParameterD = 0x1e,
        Mode7CenterX = 0x1f,
        Mode7CenterY = 0x20,
        CGRamWriteAddress,
        CGRamWriteData,
        BG12WindowMaskSettings,
        BG34WindowMaskSettings,
        OBJAndColorWindowSettings,
        Window1Left,
        Window1Right,
        Window2Left,
        Window2Right,
        BGWindowLogicSettings,
        ColorAndOBJWindowLogicSettings,
        BackgroundAndObjectEnableMain,
        BackgroundAndObjectEnableSub,
        WindowMaskMain,
        WindowMaskSub,
        ColorAdditionInitialSettings,
        AddSubtractSelectEnable,
        FixedColorData,
        ScreenInitialSettings,
        MultiplicationResultLow,
        MultiplicationResultMid,
        MultiplicationResultHigh,
        SoftwareLatchHVCounter,
        OAMDataRead,
        VRAMDataReadLow,
        VRAMDataReadHigh,
        CGRamDataRead,
        HCounter,
        VCounter,
        PpuStatus1,
        PpuStatus2,
        Apu1,
        Apu2,
        Apu3,
        Apu4,
        IndirectWorkRamAccessPort = 0x80,
        IndirectWorkRamAccessAddressLow,
        IndirectWorkRamAccessAddressMid,
        IndirectWorkRamAccessAddressHigh,
    }

    /// <summary>
    /// Offset from 0x4200
    /// </summary>
    public enum HardwareRegisterHighOffset
    {
        NmiVHCountJoypadEnable = 0x00,
        ProgrammableIoPortOutput,
        Multiplicand,
        Multiplier,
        DividendLow,
        DividendHigh,
        Divisor,
        HCountTimer,
        HCountTimerMSB,
        VCountTimer,
        VCountTimerMSB,
        DmaChannelEnable,
        HdmaChannelEnable,
        CycleSpeed,
        NmiFlagAndCpuVersionNumber = 0x10,
        IrqFlagByHVCountTimer,
        HVBlankFlagsAndJoypadStatus,
        ProgrammableIoPortInput,
        DivideResultLow,
        DivideResultHigh,
        ProductOrRemainderLow,
        ProductOrRemainderHigh,
        Joypad1Low,
        Joypad2Low,
        Joypad3Low,
        Joypad4Low,
        Joypad1High,
        Joypad2High,
        Joypad3High,
        Joypad4High,
    }
}
