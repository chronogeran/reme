using Utils;
using RemeSnes.Hardware.Audio;
using System;

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
        private Cpu _cpu;

        // TODO maybe move hardware registers to their respective locations, and just route to them from here.
        private byte[] DmaRegisters = new byte[0x80];

        public Bus(Wram wram, Sram sram, Rom rom, Ppu ppu, Apu apu, Cpu cpu)
        {
            _wram = wram;
            _rom = rom;
            _sram = sram;
            _ppu = ppu;
            _apu = apu;
            _cpu = cpu;
            Reset();
        }

        public void Reset()
        {
            WriteToSignedMultiplyLowNext = true;
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
                switch (joypadIndex)
                {
                    case 0:
                        Joypad1 = buttons; break;
                    case 1:
                        Joypad2 = buttons; break;
                    case 2:
                        Joypad3 = buttons; break;
                    case 3:
                        Joypad4 = buttons; break;
                }
        }

        #region Signals
        public void SendVBlank()
        {
            NmiFlagAndCpuVersionNumber |= 0x80;
            if (NmiEnable)
                _cpu.TriggerVBlank();
        }
        #endregion

        #region Hardware Registers
        // https://en.wikibooks.org/wiki/Super_NES_Programming/SNES_Hardware_Registers
        private ushort VramAddress;
        private byte VRAMAddressIncrement;
        private byte NmiVHCountJoypadEnable;
        private byte NmiFlagAndCpuVersionNumber;

        private byte SignedMultiplyLow;
        private byte SignedMultiplyHigh;
        private int SignedMultiplyResult;
        private bool WriteToSignedMultiplyLowNext;

        private ushort CGRamAccessAddress;
        private ushort OAMAccessAddress;

        private byte Multiplicand;
        private byte Multiplier;
        private ushort Dividend;
        private byte Divisor;
        private ushort DivideResult;
        private ushort ProductOrRemainder;

        private ushort Joypad1;
        private ushort Joypad2;
        private ushort Joypad3;
        private ushort Joypad4;

        // Hardware register convenience properties
        private bool JoypadEnable { get { return (NmiVHCountJoypadEnable & 1) != 0; } }
        public bool NmiEnable { get { return (NmiVHCountJoypadEnable & 0x80) != 0; } }
        // TODO clear joypad data on joypad disable?

        private byte ReadHardwareRegister(ushort address)
        {
            if (address >= 0x2100 && address < 0x2200)
            {
                var offset = (HardwareRegisterLowOffset)(address - 0x2100);

                switch (offset)
                {
                    case HardwareRegisterLowOffset.CGRamDataRead:
                        return _ppu.CGRam[++CGRamAccessAddress];
                    case HardwareRegisterLowOffset.OAMDataRead:
                        return _ppu.OAM[++OAMAccessAddress];
                    case HardwareRegisterLowOffset.VRAMDataReadLow:
                        {
                            var result = _ppu.Vram[VramAddress];
                            // TODO order of operations
                            if ((VRAMAddressIncrement & 0x80) == 0)
                                AutoIncrementVramAddress();
                            return result;
                        }
                    case HardwareRegisterLowOffset.VRAMDataReadHigh:
                        {
                            var result = _ppu.Vram[(ushort)(VramAddress + 1)];
                            // TODO order of operations
                            if ((VRAMAddressIncrement & 0x80) != 0)
                                AutoIncrementVramAddress();
                            return result;
                        }
                    case HardwareRegisterLowOffset.Apu1:
                        return _apu.Port0;
                    case HardwareRegisterLowOffset.Apu2:
                        return _apu.Port1;
                    case HardwareRegisterLowOffset.Apu3:
                        return _apu.Port2;
                    case HardwareRegisterLowOffset.Apu4:
                        return _apu.Port3;
                }
            }
            else if (address >= 0x4200 && address < 0x4500)
            {
                var offset = (HardwareRegisterHighOffset)(address - 0x4200);

                switch (offset)
                {
                    case HardwareRegisterHighOffset.NmiVHCountJoypadEnable:
                        return NmiVHCountJoypadEnable;
                    case HardwareRegisterHighOffset.NmiFlagAndCpuVersionNumber:
                        {
                            var b = NmiFlagAndCpuVersionNumber;
                            // Clear NMI flag on read
                            NmiFlagAndCpuVersionNumber &= 0x7f;
                            return b;
                        }
                    case HardwareRegisterHighOffset.HVBlankFlagsAndJoypadStatus:
                        {
                            byte b = 0;
                            if (_ppu.InVBlank())
                                b |= 0x80;
                            // TODO H-Blank
                            // TODO Auto-Joypad Read status
                            return b;
                        }
                    case HardwareRegisterHighOffset.Joypad1Low:
                        return (byte)Joypad1;
                    case HardwareRegisterHighOffset.Joypad1High:
                        return Joypad1.GetHighByte();
                    case HardwareRegisterHighOffset.Joypad2Low:
                        return (byte)Joypad2;
                    case HardwareRegisterHighOffset.Joypad2High:
                        return Joypad2.GetHighByte();
                    case HardwareRegisterHighOffset.Joypad3Low:
                        return (byte)Joypad3;
                    case HardwareRegisterHighOffset.Joypad3High:
                        return Joypad3.GetHighByte();
                    case HardwareRegisterHighOffset.Joypad4Low:
                        return (byte)Joypad4;
                    case HardwareRegisterHighOffset.Joypad4High:
                        return Joypad4.GetHighByte();
                    case HardwareRegisterHighOffset.DivideResultLow:
                        return (byte)DivideResult;
                    case HardwareRegisterHighOffset.DivideResultHigh:
                        return DivideResult.GetHighByte();
                    case HardwareRegisterHighOffset.ProductOrRemainderLow:
                        return (byte)ProductOrRemainder;
                    case HardwareRegisterHighOffset.ProductOrRemainderHigh:
                        return ProductOrRemainder.GetHighByte();
                }
            }

            throw new Exception($"Unhandled hardware register {address:x4}");
        }

        private void WriteHardwareRegister(ushort address, byte value)
        {
            if (address >= 0x2100 && address < 0x2200)
            {
                var offset = (HardwareRegisterLowOffset)(address - 0x2100);

                // TODO check if we're in Mode 7
                // Signed multiplication
                if (offset == HardwareRegisterLowOffset.Mode7MatrixParameterAOrMultiplicand)
                {
                    if (WriteToSignedMultiplyLowNext)
                        SignedMultiplyLow = value;
                    else
                        SignedMultiplyHigh = value;
                    WriteToSignedMultiplyLowNext = !WriteToSignedMultiplyLowNext;
                }
                else if (offset == HardwareRegisterLowOffset.Mode7MatrixParameterBOrMultiplier)
                {
                    var multiplicand = (short)(SignedMultiplyLow | (SignedMultiplyHigh << 8));
                    SignedMultiplyResult = multiplicand * value;
                }
                else if (offset == HardwareRegisterLowOffset.CGRamWriteData)
                {
                    _ppu.WriteCGRam(CGRamAccessAddress++, value);
                }
                else if (offset == HardwareRegisterLowOffset.CGRamWriteAddress)
                {
                    CGRamAccessAddress = (ushort)(value * 2);
                }
                else if (offset == HardwareRegisterLowOffset.OAMAddressLow)
                {
                    OAMAccessAddress.SetLowByte(value);
                }
                else if (offset == HardwareRegisterLowOffset.OAMAddressHigh)
                {
                    // TODO handle priority rotation bit
                    OAMAccessAddress.SetHighByte((byte)(value & 0x1));
                }
                else if (offset == HardwareRegisterLowOffset.OAMDataWrite)
                {
                    _ppu.WriteOAM(OAMAccessAddress, value);
                }
                else if (offset == HardwareRegisterLowOffset.VRAMAddress)
                {
                    // TODO implement "dummy read"
                    VramAddress.SetLowByte(value);
                }
                else if (offset == HardwareRegisterLowOffset.VRAMAddressHigh)
                {
                    // TODO implement "dummy read"
                    VramAddress.SetHighByte(value);
                }
                else if (offset == HardwareRegisterLowOffset.VRAMDataWriteLow)
                {
                    _ppu.WriteVram((ushort)(VramAddress * 2), value);
                    // Increment address if setting set
                    // TODO make sure the order of operations here is correct
                    if ((VRAMAddressIncrement & 0x80) == 0)
                        AutoIncrementVramAddress();
                }
                else if (offset == HardwareRegisterLowOffset.VRAMDataWriteHigh)
                {
                    _ppu.WriteVram((ushort)(VramAddress * 2 + 1), value);
                    // Increment address if setting set
                    // TODO make sure the order of operations here is correct
                    if ((VRAMAddressIncrement & 0x80) != 0)
                        AutoIncrementVramAddress();
                }
                else if (offset == HardwareRegisterLowOffset.Apu1)
                    _apu.Port0 = value;
                else if (offset == HardwareRegisterLowOffset.Apu2)
                    _apu.Port1 = value;
                else if (offset == HardwareRegisterLowOffset.Apu3)
                    _apu.Port2 = value;
                else if (offset == HardwareRegisterLowOffset.Apu4)
                    _apu.Port3 = value;
            }
            if (address >= 0x4200 && address < 0x4500)
            {
                var offset = (HardwareRegisterHighOffset)(address - 0x4200);

                switch (offset)
                {
                    case HardwareRegisterHighOffset.NmiVHCountJoypadEnable:
                        NmiVHCountJoypadEnable = value;
                        Console.WriteLine($"NMI enabled: {NmiEnable}");
                        break;
                }

                // TODO delay results (there are likely situations in which the CPU uses the write for a next operation to cover the wait time for the previous operation)
                // Multiplication
                if (offset == HardwareRegisterHighOffset.Multiplicand)
                {
                    Multiplicand = value;
                }
                else if (offset == HardwareRegisterHighOffset.Multiplier)
                {
                    Multiplier = value;
                    ProductOrRemainder = (ushort)(Multiplicand * Multiplier);
                }
                // Division
                else if (offset == HardwareRegisterHighOffset.DividendLow)
                    Dividend.SetLowByte(value);
                else if (offset == HardwareRegisterHighOffset.DividendHigh)
                    Dividend.SetHighByte(value);
                else if (offset == HardwareRegisterHighOffset.Divisor)
                {
                    Divisor = value;
                    DivideResult = (ushort)(Dividend / Divisor);
                    ProductOrRemainder = (ushort)(Dividend % Divisor);
                }
                else if (offset == HardwareRegisterHighOffset.DmaChannelEnable)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((value & 1) != 0)
                        {
                            DoDMA(i);
                        }
                        value >>= 1;
                    }
                }
                else if (offset == HardwareRegisterHighOffset.HdmaChannelEnable)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if ((value & 1) != 0)
                        {
                            DoHDMA(i);
                        }
                        value >>= 1;
                    }
                }
            }
        }

        private void AutoIncrementVramAddress()
        {
            var incrementSizeSelector = VRAMAddressIncrement & 0b11;
            if (incrementSizeSelector == 0)
                VramAddress += 1;
            else if (incrementSizeSelector == 1)
                VramAddress += 32;
            else if (incrementSizeSelector == 2)
                VramAddress += 64;
            else if (incrementSizeSelector == 3)
                VramAddress += 128;
        }
        #endregion

        internal ushort GetNMIVector(bool emulationMode)
        {
            return _rom.NMIVector;
        }

        internal ushort GetBRKVector(bool emulationMode)
        {
            return _rom.BRKVector;
        }

        internal ushort GetCOPVector(bool emulationMode)
        {
            return _rom.COPVector;
        }

        #region Addressing
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
                if (address >= 0x3000 && address < 0x4000)
                {
                    // TODO handle expansions
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
            if (bank < 0x40 || (bank >= 0x80 && bank < 0xc0))
            {
                if (address >= 0x2100 && address < 0x2200)
                    return ReadHardwareRegister(address);
                if (address >= 0x4200 && address < 0x4500)
                    return ReadHardwareRegister(address);
            }

            return GetMemory(bank, address, out var index)[index];
        }

        public void WriteByte(byte bank, ushort address, byte value)
        {
            if (bank < 0x40 || (bank >= 0x80 && bank < 0xc0))
            {
                if (address >= 0x2100 && address < 0x2200)
                {
                    WriteHardwareRegister(address, value);
                    return;
                }
                if (address >= 0x4200 && address < 0x4500)
                {
                    WriteHardwareRegister(address, value);
                    return;
                }
            }

            GetMemory(bank, address, out int index)[index] = value;
        }

        public ushort ReadShort(byte bank, ushort address)
        {
            var low = ReadByte(bank, address);
            var high = ReadByte(bank, (ushort)(address + 1));
            return (ushort)(low | (high << 8));
        }

        public uint ReadLong(byte bank, ushort address)
        {
            var low = ReadByte(bank, address);
            var mid = ReadByte(bank, (ushort)(address + 1));
            var high = ReadByte(bank, (ushort)(address + 2));
            return (uint)(low | (mid << 8) | (high << 16));
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

        public void WriteShort(byte bank, ushort address, ushort value)
        {
            WriteByte(bank, address, (byte)value);
            WriteByte(bank, (ushort)(address + 1), (byte)(value >> 8));
        }

        public void WriteByte(uint longAddress, byte value)
        {
            WriteByte((byte)(longAddress >> 16), (ushort)longAddress, value);
        }

        public void WriteShort(uint longAddress, ushort value)
        {
            WriteShort((byte)(longAddress >> 16), (ushort)longAddress, value);
        }
        #endregion

        // https://en.wikibooks.org/wiki/Super_NES_Programming/DMA_tutorial
        private void DoDMA(int channel)
        {
            // TODO
        }

        private void DoHDMA(int channel)
        {
            // TODO
        }
    }

    /// <summary>
    /// Offset from 0x2100
    /// </summary>
    public enum HardwareRegisterLowOffset
    {
        ScreenDisplay = 0x00,
        OAMSizeAndDataArea = 0x01,
        OAMAddressLow = 0x02,
        OAMAddressHigh,
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
        VRAMAddressHigh,
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
