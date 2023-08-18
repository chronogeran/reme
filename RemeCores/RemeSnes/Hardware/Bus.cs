using System;
using Utils;

namespace RemeSnes.Hardware
{
    /// <summary>
    /// Represents the main A bus, B bus, and data bus, handling routing based on the requested address.
    /// </summary>
    internal class Bus
    {
        private Wram _wram;
        private Rom _rom;
        private Sram _sram;
        private Ppu _ppu;
        private Apu _apu;

        // https://en.wikibooks.org/wiki/Super_NES_Programming/SNES_Hardware_Registers

        // Hardware Registers
        // TODO maybe move hardware registers to their respective locations, and just route to them from here.
        internal byte[] HardwareRegistersLow = new byte[0x84];
        private byte[] HardwareRegistersHigh = new byte[0x20];
        private byte[] DmaRegisters = new byte[0x80];

        // Hardware register convenience properties
        private bool JoypadEnable { get { return (HardwareRegistersHigh[(int)HardwareRegisterHighOffset.NmiVHCountJoypadEnable] & 1) != 0; } }
        // TODO clear joypad data on joypad disable?

        public Bus(Wram wram, Sram sram, Rom rom, Ppu ppu, Apu apu)
        {
            _wram = wram;
            _rom = rom;
            _sram = sram;
            _ppu = ppu;
            _apu = apu;
        }

        /// <summary>
        /// Sets input values for a SNES controller.
        /// </summary>
        /// <param name="joypadIndex">Controller index from 0 to 3.</param>
        /// <param name="buttons">Bits from least to most significant: right, left, down, up, Start, Select, Y, B, -, -, -, -, R, L, X, A.
        /// (Low 4 bits of high byte are not used.)
        /// </param>
        public void SetJoypadData(int joypadIndex, ushort buttons)
        {
            if (JoypadEnable)
                HardwareRegistersHigh.WriteShort((int)HardwareRegisterHighOffset.Joypad1Low + joypadIndex * 2, buttons);
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
                    // TODO LoRom vs HiRom logic could be handled elsewhere like a Cartridge class or in the Rom class
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
            var result = GetMemory(bank, address, out int index)[index];
            HandleHardwareRegisterRead(bank, address);
            return result;
        }

        public ushort ReadShort(byte bank, ushort address)
        {
            var result = GetMemory(bank, address, out int index).ReadShort(index);
            // TODO improve this architecture
            HandleHardwareRegisterRead(bank, address);
            HandleHardwareRegisterRead(bank, (ushort)(address + 1));
            return result;
        }

        public uint ReadLong(byte bank, ushort address)
        {
            var result = GetMemory(bank, address, out int index).ReadLong(index);
            HandleHardwareRegisterRead(bank, address);
            HandleHardwareRegisterRead(bank, (ushort)(address + 1));
            HandleHardwareRegisterRead(bank, (ushort)(address + 2));
            return result;
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
            HandleHardwareRegisterWrite(bank, address);
        }

        public void WriteShort(byte bank, ushort address, ushort value)
        {
            GetMemory(bank, address, out int index).WriteShort(index, value);
            HandleHardwareRegisterWrite(bank, address);
            HandleHardwareRegisterWrite(bank, (ushort)(address + 1));
        }

        public void WriteByte(uint longAddress, byte value)
        {
            WriteByte((byte)(longAddress >> 16), (ushort)longAddress, value);
        }

        public void WriteShort(uint longAddress, ushort value)
        {
            WriteShort((byte)(longAddress >> 16), (ushort)longAddress, value);
        }

        private byte SignedMultiplyLow;
        private byte SignedMultiplyHigh;
        private bool WriteToSignedMultiplyLowNext = true;

        private int CGRamAccessAddress;
        private int OAMAccessAddress;
        // Called after the write occurs.
        private void HandleHardwareRegisterWrite(byte bank, ushort address)
        {
            // TODO hardware registers may need to be handled sequentially rather than with WriteShort
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
                    else if (offset == HardwareRegisterLowOffset.CGRamWriteData)
                    {
                        _ppu.CGRam[CGRamAccessAddress++] = HardwareRegistersLow[(int)HardwareRegisterLowOffset.CGRamWriteData];
                        HardwareRegistersLow[(int)HardwareRegisterLowOffset.CGRamDataRead] = _ppu.CGRam[CGRamAccessAddress];
                    }
                    else if (offset == HardwareRegisterLowOffset.CGRamWriteAddress)
                    {
                        CGRamAccessAddress = HardwareRegistersLow[(int)offset] * 2;
                        HardwareRegistersLow[(int)HardwareRegisterLowOffset.CGRamDataRead] = _ppu.CGRam[CGRamAccessAddress];
                    }
                    else if (offset == HardwareRegisterLowOffset.OAMAddress)
                    {
                        // TODO determine if I need to handle 2103 separately as well
                        // TODO handle priority rotation bit
                        OAMAccessAddress = HardwareRegistersLow[(int)offset] & 0x1ff;
                        HardwareRegistersLow[(int)HardwareRegisterLowOffset.OAMDataRead] = _ppu.OAM[OAMAccessAddress];
                    }
                    else if (offset == HardwareRegisterLowOffset.OAMDataWrite)
                    {
                        // TODO determine if OAMAccessAddress has to be stored back in the register or not
                        _ppu.OAM[OAMAccessAddress++] = HardwareRegistersLow[(int)offset];
                        HardwareRegistersLow[(int)HardwareRegisterLowOffset.OAMDataRead] = _ppu.OAM[OAMAccessAddress];
                    }
                    else if (offset == HardwareRegisterLowOffset.VRAMAddress)
                    {
                        // TODO implement "dummy read"

                    }
                    else if (offset == HardwareRegisterLowOffset.VRAMDataWriteLow)
                    {
                        var vramAddress = HardwareRegistersLow.ReadShort((int)HardwareRegisterLowOffset.VRAMAddress);
                        _ppu.Vram[vramAddress * 2] = HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMDataWriteLow];
                        // Increment address if setting set
                        // TODO make sure the order of operations here is correct
                        if ((HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMAddressIncrement] & 0x80) == 0)
                            AutoIncrementVramAddress();
                    }
                    else if (offset == HardwareRegisterLowOffset.VRAMDataWriteHigh)
                    {
                        var vramAddress = HardwareRegistersLow.ReadShort((int)HardwareRegisterLowOffset.VRAMAddress);
                        _ppu.Vram[vramAddress * 2 + 1] = HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMDataWriteHigh];
                        // Increment address if setting set
                        // TODO make sure the order of operations here is correct
                        if ((HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMAddressIncrement] & 0x80) != 0)
                            AutoIncrementVramAddress();
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
                    else if (offset == HardwareRegisterHighOffset.DmaChannelEnable)
                    {
                        var channels = HardwareRegistersHigh[(int)offset];
                        for (int i = 0; i < 8; i++)
                        {
                            if ((channels & 1) != 0)
                            {
                                DoDMA(i);
                            }
                            channels >>= 1;
                        }
                    }
                    else if (offset == HardwareRegisterHighOffset.HdmaChannelEnable)
                    {
                        var channels = HardwareRegistersHigh[(int)offset];
                        for (int i = 0; i < 8; i++)
                        {
                            if ((channels & 1) != 0)
                            {
                                DoHDMA(i);
                            }
                            channels >>= 1;
                        }
                    }
                }
            }
        }

        private void AutoIncrementVramAddress()
        {
            var vramAddress = HardwareRegistersLow.ReadShort((int)HardwareRegisterLowOffset.VRAMAddress);
            var incrementSizeSelector = HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMAddressIncrement] & 0b11;
            if (incrementSizeSelector == 0)
                vramAddress += 1;
            else if (incrementSizeSelector == 1)
                vramAddress += 32;
            else if (incrementSizeSelector == 2)
                vramAddress += 64;
            else if (incrementSizeSelector == 3)
                vramAddress += 128;
            HardwareRegistersLow.WriteShort((int)HardwareRegisterLowOffset.VRAMAddress, vramAddress);
        }

        // https://en.wikibooks.org/wiki/Super_NES_Programming/DMA_tutorial
        private void DoDMA(int channel)
        {
            // TODO
        }

        private void DoHDMA(int channel)
        {
            // TODO
        }

        // Called after the read occurs.
        private void HandleHardwareRegisterRead(byte bank, ushort address)
        {
            if (bank < 0x40 || (bank >= 0x80 && bank < 0xc0))
            {
                if (address >= 0x2100 && address < 0x2200)
                {
                    var offset = (HardwareRegisterLowOffset)(address - 0x2100);

                    if (offset == HardwareRegisterLowOffset.CGRamDataRead)
                    {
                        HardwareRegistersLow[(int)HardwareRegisterLowOffset.CGRamDataRead] = _ppu.CGRam[++CGRamAccessAddress];
                    }
                    else if (offset == HardwareRegisterLowOffset.OAMDataRead)
                    {
                        HardwareRegistersLow[(int)HardwareRegisterLowOffset.OAMDataRead] = _ppu.OAM[++OAMAccessAddress];
                    }
                    else if (offset == HardwareRegisterLowOffset.VRAMDataReadLow)
                    {
                        // TODO order of operations
                        if ((HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMAddressIncrement] & 0x80) == 0)
                            AutoIncrementVramAddress();
                    }
                    else if (offset == HardwareRegisterLowOffset.VRAMDataReadHigh)
                    {
                        // TODO order of operations
                        if ((HardwareRegistersLow[(int)HardwareRegisterLowOffset.VRAMAddressIncrement] & 0x80) != 0)
                            AutoIncrementVramAddress();
                    }
                }
                if (address >= 0x4200 && address < 0x4500)
                {
                    var offset = (HardwareRegisterHighOffset)(address - 0x4200);
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
        VRAMDataWriteLow = 0x18,
        VRAMDataWriteHigh,
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
        Joypad1High,
        Joypad2Low,
        Joypad2High,
        Joypad3Low,
        Joypad3High,
        Joypad4Low,
        Joypad4High,
    }
}
