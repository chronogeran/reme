using System.Diagnostics;
using Utils;

namespace RemeSnes.Hardware.Audio
{
    internal class Spc700
    {
        public byte[] Psram = new byte[0x10000]; // Holds code and data for the SPC700

        private Dsp _dsp;

        private static readonly byte[] IPL_ROM = new byte[]
        {
            0xCD, 0xEF, 0xBD, 0xE8, 0x00, 0xC6, 0x1D, 0xD0, 0xFC, 0x8F, 0xAA, 0xF4, 0x8F, 0xBB, 0xF5, 0x78,
            0xCC, 0xF4, 0xD0, 0xFB, 0x2F, 0x19, 0xEB, 0xF4, 0xD0, 0xFC, 0x7E, 0xF4, 0xD0, 0x0B, 0xE4, 0xF5,
            0xCB, 0xF4, 0xD7, 0x00, 0xFC, 0xD0, 0xF3, 0xAB, 0x01, 0x10, 0xEF, 0x7E, 0xF4, 0x10, 0xEB, 0xBA,
            0xF6, 0xDA, 0x00, 0xBA, 0xF4, 0xC4, 0xF4, 0xDD, 0x5D, 0xD0, 0xDB, 0x1F, 0x00, 0x00, 0xC0, 0xFF,
        };

        public Spc700()
        {
            _cpuThread = new Thread(ThreadLoop) { Name = "SPC-700 Thread" };
            _cpuThread.Start();
        }

        public void SetDsp(Dsp dsp) { _dsp = dsp; }

        public void Reset()
        {
            if (_running)
            {
                _shuttingDown = true;
                _cpuThread.Join();
                _running = false;
                _shuttingDown = false;
                _cpuThread = new Thread(ThreadLoop);
                _cpuThread.Start();
            }
            Array.Clear(Psram);
            _iplRegionReadable = true;
            Accumulator = 0;
            X = 0;
            Y = 0;
            ProgramStatus = 0;
            ProgramCounter = 0xffc0;
        }

        internal void EmulateFrame()
        {
            _emulateSignal.Set();
        }
        private Thread _cpuThread;
        private ManualResetEvent _emulateSignal = new(false);

        private volatile bool _shuttingDown = false;
        private volatile bool _running = false;
        private void ThreadLoop()
        {
            while (!_shuttingDown)
            {
                _emulateSignal.WaitOne();
                _emulateSignal.Reset();
                _running = true;
                // Begin frame

                // If we want to simulate at 60Hz, we should run about 17038 clock cycles
                // per iteration. If each iteration through this loop is 16 cycles, that's 1065 iterations.
                int iterations = 0;
                while (iterations < 1065)
                {
                    iterations++;
                    IncrementTimers();
                    // for now a very rough 3 instructions per 16 cycles
                    for (int i = 0; i < 3; i++)
                        RunOneInstruction();
                }
            }
        }

        // https://wiki.superfamicom.org/spc700-reference
        // https://en.wikibooks.org/wiki/Super_NES_Programming/SPC700_reference
        // https://emudev.de/q00-snes/spc700-the-audio-processor/
        // SPC700 registers
        private byte Accumulator;
        private byte X;
        private byte Y;
        private byte Stack;
        private ushort ProgramCounter;
        private byte ProgramStatus;

        // Status Flags
        private bool Carry { get { return (ProgramStatus & 0x1) != 0; } set { if (value) ProgramStatus |= 0x1; else ProgramStatus &= 0xfe; } }
        private bool Zero { get { return (ProgramStatus & 0x2) != 0; } set { if (value) ProgramStatus |= 0x2; else ProgramStatus &= 0xfd; } }
        private bool InterruptEnabled { get { return (ProgramStatus & 0x4) != 0; } set { if (value) ProgramStatus |= 4; else ProgramStatus &= 0xfb; } }
        private bool HalfCarry { get { return (ProgramStatus & 0x8) != 0; } set { if (value) ProgramStatus |= 8; else ProgramStatus &= 0xf7; } }
        private bool Break { get { return (ProgramStatus & 0x10) != 0; } set { if (value) ProgramStatus |= 0x10; else ProgramStatus &= 0xef; } }
        private bool DirectPage { get { return (ProgramStatus & 0x20) != 0; } set { if (value) ProgramStatus |= 0x20; else ProgramStatus &= 0xdf; } }
        private bool Overflow { get { return (ProgramStatus & 0x40) != 0; } set { if (value) ProgramStatus |= 0x40; else ProgramStatus &= 0xbf; } }
        private bool Negative { get { return (ProgramStatus & 0x80) != 0; } set { if (value) ProgramStatus |= 0x80; else ProgramStatus &= 0x7f; } }

        private ushort DirectPageOffset(byte offset) { return (ushort)(offset + (DirectPage ? 0x100 : 0)); }
        private ushort XOffset { get { return DirectPageOffset(X); } }
        private ushort YOffset { get { return DirectPageOffset(Y); } }

        // Hardware Registers

        // Timers
        // Setting TimerX sets the due time of the timer (8 bits)
        // Internal timer tick is only incremented when timer is enabled
        // CounterX increments every time the timer hits its due time (4 bits)
        // CounterX resets to zero on read
        // Internal timer is reset on each Counter increment
        // The timer should be stopped before setting the Timer registers (you can do it while running, but it will not catch an earlier setting)
        //private byte Timer0 { set { _timer0 = value; } } // 8KHz (128 clock cycles @1.024 MHz)
        //private byte Timer1 { set { _timer1 = value; } } // 8KHz
        //private byte Timer2 { set { _timer2 = value; } } // 64KHz (16 clock cycles @1.024 MHz)
        private struct Timer
        {
            private bool Enabled;
            private byte InternalTick;
            public byte Target;
            private byte Counter; // Number of times Target has been reached

            public void SetEnabled(bool enabled)
            {
                Enabled = enabled;
                if (Enabled)
                    Reset();
            }

            public void Reset()
            {
                InternalTick = 0;
                Counter = 0;
            }

            public byte ReadCounter()
            {
                var c = Counter;
                Counter = 0;
                return c;
            }

            public void Tick()
            {
                if (Enabled)
                {
                    InternalTick++;
                    if (InternalTick == Target)
                    {
                        Counter++;
                        if (Counter > 0xf)
                            Counter = 0;
                        InternalTick = 0;
                    }
                }
            }
        }
        private Timer[] _timers = new Timer[3];

        private byte _timer2CyclesDone;
        /// <summary>
        /// A single call to this method represents the passing of 16 clock cycles.
        /// </summary>
        private void IncrementTimers()
        {
            _timers[2].Tick();
            _timer2CyclesDone++;
            if (_timer2CyclesDone == 8)
            {
                _timer2CyclesDone = 0;
                _timers[0].Tick();
                _timers[1].Tick();
            }
        }

        // External entry points for I/O ports
        public byte Port0 { get { return Port0Out; } set { Port0In = value; Console.WriteLine($"SPC 0 was told {value:x2}"); } }
        public byte Port1 { get { return Port1Out; } set { Port1In = value; Console.WriteLine($"SPC 1 was told {value:x2}"); } }
        public byte Port2 { get { return Port2Out; } set { Port2In = value; Console.WriteLine($"SPC 2 was told {value:x2}"); } }
        public byte Port3 { get { return Port3Out; } set { Port3In = value; Console.WriteLine($"SPC 3 was told {value:x2}"); } }

        private byte Port0Out;
        private byte Port0In;
        private byte Port1Out;
        private byte Port1In;
        private byte Port2Out;
        private byte Port2In;
        private byte Port3Out;
        private byte Port3In;

        private byte DspAddress;
        private byte RegisterF8;
        private byte RegisterF9;
        private bool _iplRegionReadable;

        /// <summary>
        /// Reads one byte from ProgramCounter, increments ProgramCounter.
        /// </summary>
        private byte ReadCodeByte()
        {
            return ReadByte(ProgramCounter++);
        }

        /// <summary>
        /// Reads one byte from ProgramCounter, increments ProgramCounter, returns effective address.
        /// </summary>
        private ushort GetDirectPageOffsetParameter()
        {
            return DirectPageOffset(ReadCodeByte());
        }

        /// <summary>
        /// Also increments ProgramCounter.
        /// </summary>
        private ushort GetCodeParamEffectiveAddress(AddressingType addressingType)
        {
            switch (addressingType)
            {
                case AddressingType.Immediate:
                    return ProgramCounter++;
                case AddressingType.Absolute:
                    ProgramCounter += 2;
                    return Psram.ReadShort(ProgramCounter - 2);
                case AddressingType.AbsoluteBit:
                    ProgramCounter += 2;
                    return Psram.ReadShort(ProgramCounter - 2);
                case AddressingType.AbsoluteIndexedX:
                    ProgramCounter += 2;
                    return (ushort)(Psram.ReadShort(ProgramCounter - 2) + X);
                case AddressingType.AbsoluteIndexedXIndirect:
                    ProgramCounter += 2;
                    return ReadShort((ushort)(Psram.ReadShort(ProgramCounter - 2) + X));
                case AddressingType.AbsoluteIndexedY:
                    ProgramCounter += 2;
                    return (ushort)(Psram.ReadShort(ProgramCounter - 2) + Y);
                case AddressingType.X:
                    return XOffset;
                case AddressingType.Y:
                    return YOffset;
                case AddressingType.DirectPage:
                    return GetDirectPageOffsetParameter();
                case AddressingType.DirectPageIndexedX:
                    return (ushort)(GetDirectPageOffsetParameter() + X);
                case AddressingType.DirectPageIndexedY:
                    return (ushort)(GetDirectPageOffsetParameter() + Y);
                case AddressingType.DirectPageIndirectIndexedY:
                    return (ushort)(Psram.ReadShort(GetDirectPageOffsetParameter()) + Y);
                case AddressingType.DirectPageIndexedXIndirect:
                    return Psram.ReadShort(GetDirectPageOffsetParameter() + X);
                default:
                    throw new Exception("Unhandled addressing type " + addressingType);
            }
        }

        /// <summary>
        /// Increments PC if necessary.
        /// </summary>
        private byte ReadByte(AddressingType addressingType)
        {
            return ReadByte(GetCodeParamEffectiveAddress(addressingType));
        }

        /// <summary>
        /// Increments PC if necessary.
        /// </summary>
        private void WriteByte(AddressingType addressingType, byte b)
        {
            WriteByte(GetCodeParamEffectiveAddress(addressingType), b);
        }

        /// <summary>
        /// Reads a byte from the specified address, handling mapping to hardware registers.
        /// (Does not increment PC.)
        /// </summary>
        private byte ReadByte(ushort address)
        {
            if (address < 0xf0 || address > 0xff)
            {
                if (address >= 0xffc0 && _iplRegionReadable)
                    return IPL_ROM[address - 0xffc0];
                else
                    return Psram[address];
            }
            else
            {
                return address switch
                {
                    0xf2 => DspAddress,
                    0xf3 => _dsp.Read(DspAddress),
                    0xf4 => Port0In,
                    0xf5 => Port1In,
                    0xf6 => Port2In,
                    0xf7 => Port3In,
                    0xf8 => RegisterF8,
                    0xf9 => RegisterF9,
                    0xfa or 0xfb or 0xfc => 0,
                    0xfd => _timers[0].ReadCounter(),
                    0xfe => _timers[1].ReadCounter(),
                    0xff => _timers[2].ReadCounter(),
                    _ => throw new Exception("Unhandled register read address: " + address),
                };
            }
        }

        private ushort ReadShort(ushort address)
        {
            return (ushort)(ReadByte(address) | ReadByte((ushort)(address + 1)) << 8);
        }

        private ushort ReadShort(AddressingType addressingType)
        {
            return ReadShort(GetCodeParamEffectiveAddress(addressingType));
        }

        private void WriteByte(ushort address, byte b)
        {
            if (address < 0xf0 || address > 0xff)
                Psram[address] = b;
            else
            {
                switch (address)
                {
                    case 0xf1:
                        HandleControlWrite(b);
                        break;
                    case 0xf2:
                        DspAddress = b;
                        break;
                    case 0xf3:
                        _dsp.Write(DspAddress, b);
                        break;
                    case 0xf4:
                        Port0Out = b;
                        Console.WriteLine($"SPC 0 says {Port0Out:x2}");
                        break;
                    case 0xf5:
                        Port1Out = b;
                        Console.WriteLine($"SPC 1 says {Port1Out:x2}");
                        break;
                    case 0xf6:
                        Port2Out = b;
                        Console.WriteLine($"SPC 2 says {Port2Out:x2}");
                        break;
                    case 0xf7:
                        Port3Out = b;
                        Console.WriteLine($"SPC 3 says {Port3Out:x2}");
                        break;
                    case 0xf8:
                        RegisterF8 = b;
                        break;
                    case 0xf9:
                        RegisterF9 = b;
                        break;
                    case 0xfa:
                        _timers[0].Target = b;
                        break;
                    case 0xfb:
                        _timers[1].Target = b;
                        break;
                    case 0xfc:
                        _timers[2].Target = b;
                        break;

                    default:
                        throw new Exception("Unhandled register address: " + address);
                }
            }
        }

        private void WriteShort(ushort address, ushort s)
        {
            WriteByte(address++, (byte)s);
            WriteByte(address, (byte)(s >> 8));
        }

        private void WriteShort(AddressingType addressingType, ushort s)
        {
            WriteShort(GetCodeParamEffectiveAddress(addressingType), s);
        }

        // TODO add functions for addressing modes so each instruction can be short
        public void RunOneInstruction()
        {
            Console.WriteLine($"SPC running at {ProgramCounter:x4}");
            var opcode = ReadCodeByte();

            switch (opcode)
            {
                case 0: // NOP
                    break;
                case 0x0f: // BRK
                    PushShort(ProgramCounter);
                    PushByte(ProgramStatus);
                    ProgramCounter = ReadShort(0xffde);
                    break;
                case 0xef: // SLEEP
                    throw new Exception("Sleep encountered");
                    break;
                case 0xff: // STOP
                    throw new Exception("Stop encountered");
                    break;

                // ADC
                case 0x99:
                    WriteByte(AddressingType.X, DoAdd(ReadByte(AddressingType.X), ReadByte(AddressingType.Y)));
                    break;
                case 0x88:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.Immediate));
                    break;
                case 0x86:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.X));
                    break;
                case 0x97:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0x87:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.DirectPageIndexedXIndirect));
                    break;
                case 0x84:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x94:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    break;
                case 0x85:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.Absolute));
                    break;
                case 0x95:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x96:
                    Accumulator = DoAdd(Accumulator, ReadByte(AddressingType.AbsoluteIndexedY));
                    break;
                case 0x89:
                    {
                        var dp1 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dp2 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dp2, DoAdd(ReadByte(dp2), ReadByte(dp1)));
                    }
                    break;
                case 0x98:
                    {
                        var immediate = ReadByte(AddressingType.Immediate);
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpOffset, DoAdd(ReadByte(dpOffset), immediate));
                    }
                    break;
                case 0x7a: // ADDW
                    {
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var word = Accumulator | Y << 8;
                        var word2 = ReadShort(dpOffset);
                        var result = word + word2 + (Carry ? 1 : 0);
                        Carry = result > 0xffff;
                        Negative = (result & 0x8000) != 0;
                        Zero = (result & 0xffff) == 0;
                        Overflow = (~(word ^ word2) & (word2 ^ result) & 0x8000) != 0;
                        HalfCarry = (word >> 8 & 0x0F) + (word2 >> 8 & 0x0F) + ((word2 & 0xff) + (word & 0xff) > 0xff ? 1 : 0) > 0x0F;
                        Accumulator = (byte)result;
                        Y = (byte)(result >> 8);
                    }
                    break;

                // SBC
                case 0xb9:
                    WriteByte(AddressingType.X, DoSubtract(ReadByte(AddressingType.X), ReadByte(AddressingType.Y)));
                    break;
                case 0xa8:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.Immediate));
                    break;
                case 0xa6:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.X));
                    break;
                case 0xb7:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0xa7:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.DirectPageIndexedXIndirect));
                    break;
                case 0xa4:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.DirectPage));
                    break;
                case 0xb4:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    break;
                case 0xa5:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.Absolute));
                    break;
                case 0xb5:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.AbsoluteIndexedX));
                    break;
                case 0xb6:
                    Accumulator = DoSubtract(Accumulator, ReadByte(AddressingType.AbsoluteIndexedY));
                    break;
                case 0xa9:
                    {
                        var dp1 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dp2 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dp2, DoSubtract(ReadByte(dp2), ReadByte(dp1)));
                    }
                    break;
                case 0xb8:
                    {
                        var immediate = ReadByte(AddressingType.Immediate);
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpOffset, DoSubtract(ReadByte(dpOffset), immediate));
                    }
                    break;
                case 0x9a: // SUBW
                    {
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var word = Accumulator | Y << 8;
                        var word2 = ReadShort(dpOffset);
                        var result = word - word2 - (Carry ? 0 : 1);
                        Carry = word >= word2;
                        Negative = (result & 0x8000) != 0;
                        Zero = (result & 0xffff) == 0;
                        Overflow = ((word ^ word2) & (word ^ (ushort)result) & 0x8000) != 0;
                        HalfCarry = (word >> 8 & 0x0F) - (word2 >> 8 & 0x0F) - ((byte)word2 > (byte)word ? 1 : 0) <= 0x0F;
                        Accumulator = (byte)result;
                        Y = (byte)(result >> 8);
                    }
                    break;

                // CMP
                case 0x79:
                    DoCompare(ReadByte(AddressingType.X), ReadByte(AddressingType.Y));
                    break;
                case 0x68:
                    DoCompare(Accumulator, ReadByte(AddressingType.Immediate));
                    break;
                case 0x66:
                    DoCompare(Accumulator, ReadByte(AddressingType.X));
                    break;
                case 0x77:
                    DoCompare(Accumulator, ReadByte(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0x67:
                    DoCompare(Accumulator, ReadByte(AddressingType.DirectPageIndexedXIndirect));
                    break;
                case 0x64:
                    DoCompare(Accumulator, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x74:
                    DoCompare(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    break;
                case 0x65:
                    DoCompare(Accumulator, ReadByte(AddressingType.Absolute));
                    break;
                case 0x75:
                    DoCompare(Accumulator, ReadByte(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x76:
                    DoCompare(Accumulator, ReadByte(AddressingType.AbsoluteIndexedY));
                    break;
                case 0xc8:
                    DoCompare(X, ReadByte(AddressingType.Immediate));
                    break;
                case 0x3e:
                    DoCompare(X, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x1e:
                    DoCompare(X, ReadByte(AddressingType.Absolute));
                    break;
                case 0xad:
                    DoCompare(Y, ReadByte(AddressingType.Immediate));
                    break;
                case 0x7e:
                    DoCompare(Y, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x5e:
                    DoCompare(Y, ReadByte(AddressingType.Absolute));
                    break;
                case 0x69:
                    {
                        var dp1 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dp2 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        DoCompare(ReadByte(dp2), ReadByte(dp1));
                    }
                    break;
                case 0x78:
                    {
                        var immediate = ReadByte(AddressingType.Immediate);
                        DoCompare(ReadByte(AddressingType.DirectPage), immediate);
                    }
                    break;
                case 0x5a: // CMPW
                    {
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var word = Accumulator | Y << 8;
                        var word2 = ReadShort(dpOffset);
                        Carry = word >= word2;
                        Negative = word - word2 < 0;
                        Zero = word == word2;
                    }
                    break;

                // INC
                case 0xbc:
                    DoIncrement(ref Accumulator);
                    break;
                case 0x3d:
                    DoIncrement(ref X);
                    break;
                case 0xfc:
                    DoIncrement(ref Y);
                    break;
                case 0xab:
                    DoIncrement(AddressingType.DirectPage);
                    break;
                case 0xbb:
                    DoIncrement(AddressingType.DirectPageIndexedX);
                    break;
                case 0xac:
                    DoIncrement(AddressingType.Absolute);
                    break;
                case 0x3a: // INCW
                    DoIncrementWord(AddressingType.DirectPage);
                    break;
                // DEC
                case 0x9c:
                    DoDecrement(ref Accumulator);
                    break;
                case 0x1d:
                    DoDecrement(ref X);
                    break;
                case 0xdc:
                    DoDecrement(ref Y);
                    break;
                case 0x8b:
                    DoDecrement(AddressingType.DirectPage);
                    break;
                case 0x9b:
                    DoDecrement(AddressingType.DirectPageIndexedX);
                    break;
                case 0x8c:
                    DoDecrement(AddressingType.Absolute);
                    break;
                case 0x1a: // DECW
                    DoDecrementWord(AddressingType.DirectPage);
                    break;

                case 0xcf: // MUL
                    {
                        var result = Y * Accumulator;
                        Accumulator = (byte)result;
                        Y = (byte)(result >> 8);
                        SetNegativeAndZero(Y);
                    }
                    break;
                case 0x9e: // DIV
                    {
                        HalfCarry = (X & 0xf) <= (Y & 0xf);
                        var word = Accumulator | Y << 8;
                        var result = word / X;
                        Accumulator = (byte)result;
                        Y = (byte)(word % X);
                        SetNegativeAndZero(Accumulator);
                        Overflow = result > 0xff;
                    }
                    break;

                // AND
                case 0x39:
                    WriteByte(AddressingType.X, DoAnd(ReadByte(AddressingType.X), ReadByte(AddressingType.Y)));
                    break;
                case 0x28:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.Immediate));
                    break;
                case 0x26:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.X));
                    break;
                case 0x37:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0x27:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.DirectPageIndexedXIndirect));
                    break;
                case 0x24:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x34:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    break;
                case 0x25:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.Absolute));
                    break;
                case 0x35:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x36:
                    Accumulator = DoAnd(Accumulator, ReadByte(AddressingType.AbsoluteIndexedY));
                    break;
                case 0x29:
                    {
                        var dp1 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dp2 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dp2, DoAnd(ReadByte(dp2), ReadByte(dp1)));
                    }
                    break;
                case 0x38:
                    {
                        var immediate = ReadByte(AddressingType.Immediate);
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpOffset, DoAnd(ReadByte(dpOffset), immediate));
                    }
                    break;
                // AND1
                case 0x6a:
                    Carry &= !GetBit(GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit));
                    break;
                case 0x4a:
                    Carry &= GetBit(GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit));
                    break;
                // OR
                case 0x19:
                    WriteByte(AddressingType.X, DoOr(ReadByte(AddressingType.X), ReadByte(AddressingType.Y)));
                    break;
                case 0x08:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.Immediate));
                    break;
                case 0x06:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.X));
                    break;
                case 0x17:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0x07:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.DirectPageIndexedXIndirect));
                    break;
                case 0x04:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x14:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    break;
                case 0x05:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.Absolute));
                    break;
                case 0x15:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x16:
                    Accumulator = DoOr(Accumulator, ReadByte(AddressingType.AbsoluteIndexedY));
                    break;
                case 0x09:
                    {
                        var dp1 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dp2 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dp2, DoOr(ReadByte(dp2), ReadByte(dp1)));
                    }
                    break;
                case 0x18:
                    {
                        var immediate = ReadByte(AddressingType.Immediate);
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpOffset, DoOr(ReadByte(dpOffset), immediate));
                    }
                    break;
                // OR1
                case 0x2a:
                    Carry |= !GetBit(GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit));
                    break;
                case 0x0a:
                    Carry |= GetBit(GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit));
                    break;
                // EOR
                case 0x59:
                    WriteByte(AddressingType.X, DoExclusiveOr(ReadByte(AddressingType.X), ReadByte(AddressingType.Y)));
                    break;
                case 0x48:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.Immediate));
                    break;
                case 0x46:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.X));
                    break;
                case 0x57:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0x47:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.DirectPageIndexedXIndirect));
                    break;
                case 0x44:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.DirectPage));
                    break;
                case 0x54:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    break;
                case 0x45:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.Absolute));
                    break;
                case 0x55:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x56:
                    Accumulator = DoExclusiveOr(Accumulator, ReadByte(AddressingType.AbsoluteIndexedY));
                    break;
                case 0x49:
                    {
                        var dp1 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dp2 = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dp2, DoExclusiveOr(ReadByte(dp2), ReadByte(dp1)));
                    }
                    break;
                case 0x58:
                    {
                        var immediate = ReadByte(AddressingType.Immediate);
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpOffset, DoExclusiveOr(ReadByte(dpOffset), immediate));
                    }
                    break;
                case 0x8a: // EOR1
                    {
                        var bitAddress = GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit);
                        Carry ^= GetBit(bitAddress);
                    }
                    break;
                case 0xea: // NOT1
                    {
                        var bitAddress = GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit);
                        // Flip the specified bit
                        var addr = (ushort)(bitAddress & 0x1fff);
                        WriteByte(addr, (byte)(ReadByte(addr) ^ (byte)(1 << (bitAddress >> 13))));
                    }
                    break;
                // ASL
                case 0x1c:
                    DoLeftShift(ref Accumulator);
                    break;
                case 0x0b:
                    DoLeftShift(AddressingType.DirectPage);
                    break;
                case 0x1b:
                    DoLeftShift(AddressingType.DirectPageIndexedX);
                    break;
                case 0x0c:
                    DoLeftShift(AddressingType.Absolute);
                    break;
                // LSR
                case 0x5c:
                    DoRightShift(ref Accumulator);
                    break;
                case 0x4b:
                    DoRightShift(AddressingType.DirectPage);
                    break;
                case 0x5b:
                    DoRightShift(AddressingType.DirectPageIndexedX);
                    break;
                case 0x4c:
                    DoRightShift(AddressingType.Absolute);
                    break;
                // ROL
                case 0x3c:
                    DoRotateLeft(ref Accumulator);
                    break;
                case 0x2b:
                    DoRotateLeft(AddressingType.DirectPage);
                    break;
                case 0x3b:
                    DoRotateLeft(AddressingType.DirectPageIndexedX);
                    break;
                case 0x2c:
                    DoRotateLeft(AddressingType.Absolute);
                    break;
                // ROR
                case 0x7c:
                    DoRotateRight(ref Accumulator);
                    break;
                case 0x6b:
                    DoRotateRight(AddressingType.DirectPage);
                    break;
                case 0x7b:
                    DoRotateRight(AddressingType.DirectPageIndexedX);
                    break;
                case 0x6c:
                    DoRotateRight(AddressingType.Absolute);
                    break;

                case 0x4e: // TCLR1
                    {
                        var address = GetCodeParamEffectiveAddress(AddressingType.Absolute);
                        WriteByte(address, (byte)(ReadByte(address) & (byte)~Accumulator));
                    }
                    break;
                case 0x0e: // TSET1
                    {
                        var address = GetCodeParamEffectiveAddress(AddressingType.Absolute);
                        WriteByte(address, (byte)(ReadByte(address) | Accumulator));
                    }
                    break;

                case 0xdf: // DAA
                    {
                        var val = Accumulator;
                        if ((Accumulator & 0xf) > 9 || HalfCarry)
                        {
                            Accumulator += 6;
                            if (Accumulator < 6)
                                Carry = true;
                        }
                        if (val > 0x99 || Carry)
                        {
                            Accumulator += 0x60;
                            Carry = true;
                        }
                        SetNegativeAndZero(Accumulator);
                    }
                    break;
                case 0xbe: // DAS
                    {
                        var val = Accumulator;
                        if (!HalfCarry || (Accumulator & 0xf) > 9)
                        {
                            Accumulator -= 6;
                        }
                        if (!Carry || val > 0x99)
                        {
                            Accumulator -= 0x60;
                            Carry = false;
                        }
                        SetNegativeAndZero(Accumulator);
                    }
                    break;

                // MOV
                case 0xaf:
                    WriteByte(AddressingType.X, Accumulator);
                    X++;
                    break;
                case 0xc6:
                    WriteByte(AddressingType.X, Accumulator);
                    break;
                case 0xd7:
                    WriteByte(AddressingType.DirectPageIndirectIndexedY, Accumulator);
                    break;
                case 0xc7:
                    WriteByte(AddressingType.DirectPageIndexedXIndirect, Accumulator);
                    break;
                case 0xe8:
                    Accumulator = ReadByte(AddressingType.Immediate);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe6:
                    Accumulator = ReadByte(AddressingType.X);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xbf:
                    Accumulator = ReadByte(AddressingType.X);
                    SetNegativeAndZero(Accumulator);
                    X++;
                    break;
                case 0xf7:
                    Accumulator = ReadByte(AddressingType.DirectPageIndirectIndexedY);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe7:
                    Accumulator = ReadByte(AddressingType.DirectPageIndexedXIndirect);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0x7d:
                    Accumulator = X;
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xdd:
                    Accumulator = Y;
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe4:
                    Accumulator = ReadByte(AddressingType.DirectPage);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xf4:
                    Accumulator = ReadByte(AddressingType.DirectPageIndexedX);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe5:
                    Accumulator = ReadByte(AddressingType.Absolute);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xf5:
                    Accumulator = ReadByte(AddressingType.AbsoluteIndexedX);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xf6:
                    Accumulator = ReadByte(AddressingType.AbsoluteIndexedY);
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xbd:
                    Stack = X;
                    break;
                case 0xcd:
                    X = ReadByte(AddressingType.Immediate);
                    SetNegativeAndZero(X);
                    break;
                case 0x5d:
                    X = Accumulator;
                    SetNegativeAndZero(X);
                    break;
                case 0x9d:
                    X = Stack;
                    SetNegativeAndZero(X);
                    break;
                case 0xf8:
                    X = ReadByte(AddressingType.DirectPage);
                    SetNegativeAndZero(X);
                    break;
                case 0xf9:
                    X = ReadByte(AddressingType.DirectPageIndexedY);
                    SetNegativeAndZero(X);
                    break;
                case 0xe9:
                    X = ReadByte(AddressingType.Absolute);
                    SetNegativeAndZero(X);
                    break;
                case 0x8d:
                    Y = ReadByte(AddressingType.Immediate);
                    SetNegativeAndZero(Y);
                    break;
                case 0xfd:
                    Y = Accumulator;
                    SetNegativeAndZero(Y);
                    break;
                case 0xeb:
                    Y = ReadByte(AddressingType.DirectPage);
                    SetNegativeAndZero(Y);
                    break;
                case 0xfb:
                    Y = ReadByte(AddressingType.DirectPageIndexedX);
                    SetNegativeAndZero(Y);
                    break;
                case 0xec:
                    Y = ReadByte(AddressingType.Absolute);
                    SetNegativeAndZero(Y);
                    break;
                case 0xfa:
                    {
                        var dpSource = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        var dpDest = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpDest, ReadByte(dpSource));
                    }
                    break;
                case 0xd4:
                    WriteByte(AddressingType.DirectPageIndexedX, Accumulator);
                    break;
                case 0xdb:
                    WriteByte(AddressingType.DirectPageIndexedX, Y);
                    break;
                case 0xd9:
                    WriteByte(AddressingType.DirectPageIndexedY, X);
                    break;
                case 0x8f:
                    WriteByte(AddressingType.DirectPage, ReadByte(AddressingType.Immediate));
                    break;
                case 0xc4:
                    WriteByte(AddressingType.DirectPage, Accumulator);
                    break;
                case 0xd8:
                    WriteByte(AddressingType.DirectPage, X);
                    break;
                case 0xcb:
                    WriteByte(AddressingType.DirectPage, Y);
                    break;
                case 0xd5:
                    WriteByte(AddressingType.AbsoluteIndexedX, Accumulator);
                    break;
                case 0xd6:
                    WriteByte(AddressingType.AbsoluteIndexedY, Accumulator);
                    break;
                case 0xc5:
                    WriteByte(AddressingType.Absolute, Accumulator);
                    break;
                case 0xc9:
                    WriteByte(AddressingType.Absolute, X);
                    break;
                case 0xcc:
                    WriteByte(AddressingType.Absolute, Y);
                    break;
                // MOV1
                case 0xaa:
                    Carry = GetBit(GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit));
                    break;
                case 0xca:
                    {
                        var bitAddress = GetCodeParamEffectiveAddress(AddressingType.AbsoluteBit);
                        var byteAddress = (ushort)(bitAddress & 0x1fff);
                        if (Carry)
                            SetBit(byteAddress, bitAddress >> 13);
                        else
                            ClearBit(byteAddress, bitAddress >> 13);
                    }
                    break;
                // MOVW
                case 0xba:
                    {
                        var word = ReadShort(AddressingType.DirectPage);
                        Accumulator = (byte)word;
                        Y = (byte)(word >> 8);
                        SetNegativeAndZero(Accumulator); // is this correct?
                    }
                    break;
                case 0xda:
                    WriteShort(AddressingType.DirectPage, (ushort)(Accumulator | Y << 8));
                    break;

                // BBC
                case 0x13:
                    DoDirectPageBranchOnBit(0, false);
                    break;
                case 0x33:
                    DoDirectPageBranchOnBit(1, false);
                    break;
                case 0x53:
                    DoDirectPageBranchOnBit(2, false);
                    break;
                case 0x73:
                    DoDirectPageBranchOnBit(3, false);
                    break;
                case 0x93:
                    DoDirectPageBranchOnBit(4, false);
                    break;
                case 0xb3:
                    DoDirectPageBranchOnBit(5, false);
                    break;
                case 0xd3:
                    DoDirectPageBranchOnBit(6, false);
                    break;
                case 0xf3:
                    DoDirectPageBranchOnBit(7, false);
                    break;
                // BBS
                case 0x03:
                    DoDirectPageBranchOnBit(0, true);
                    break;
                case 0x23:
                    DoDirectPageBranchOnBit(1, true);
                    break;
                case 0x43:
                    DoDirectPageBranchOnBit(2, true);
                    break;
                case 0x63:
                    DoDirectPageBranchOnBit(3, true);
                    break;
                case 0x83:
                    DoDirectPageBranchOnBit(4, true);
                    break;
                case 0xa3:
                    DoDirectPageBranchOnBit(5, true);
                    break;
                case 0xc3:
                    DoDirectPageBranchOnBit(6, true);
                    break;
                case 0xe3:
                    DoDirectPageBranchOnBit(7, true);
                    break;
                case 0x90: // BCC
                    DoBranchInstruction(!Carry);
                    break;
                case 0xb0: // BCS
                    DoBranchInstruction(Carry);
                    break;
                case 0x10: // BPL
                    DoBranchInstruction(!Negative);
                    break;
                case 0x30: // BMI
                    DoBranchInstruction(Negative);
                    break;
                case 0xd0: // BNE
                    DoBranchInstruction(!Zero);
                    break;
                case 0xf0: // BEQ
                    DoBranchInstruction(Zero);
                    break;
                case 0x50: // BVC
                    DoBranchInstruction(!Overflow);
                    break;
                case 0x70: // BVS
                    DoBranchInstruction(Overflow);
                    break;
                case 0x2f: // BRA
                    DoBranchInstruction(true);
                    break;
                // CBNE
                case 0xde:
                    DoCompare(Accumulator, ReadByte(AddressingType.DirectPageIndexedX));
                    DoBranchInstruction(!Zero);
                    break;
                case 0x2e:
                    DoCompare(Accumulator, ReadByte(AddressingType.DirectPage));
                    DoBranchInstruction(!Zero);
                    break;
                // DBNZ
                case 0xfe:
                    Y--;
                    DoBranchInstruction(Y != 0);
                    break;
                case 0x6e:
                    {
                        var dpOffset = GetCodeParamEffectiveAddress(AddressingType.DirectPage);
                        WriteByte(dpOffset, (byte)(ReadByte(dpOffset) - 1));
                        DoBranchInstruction(ReadByte(dpOffset) != 0);
                    }
                    break;
                case 0x3f: // CALL
                    DoCall(GetCodeParamEffectiveAddress(AddressingType.Absolute));
                    break;
                case 0x4f: // PCALL
                    DoCall((ushort)(ReadByte(AddressingType.Immediate) + 0xff00));
                    break;
                // TCALL
                case 0x01:
                    DoTCall(0xffde);
                    break;
                case 0x11:
                    DoTCall(0xffdc);
                    break;
                case 0x21:
                    DoTCall(0xffda);
                    break;
                case 0x31:
                    DoTCall(0xffd8);
                    break;
                case 0x41:
                    DoTCall(0xffd6);
                    break;
                case 0x51:
                    DoTCall(0xffd4);
                    break;
                case 0x61:
                    DoTCall(0xffd2);
                    break;
                case 0x71:
                    DoTCall(0xffd0);
                    break;
                case 0x81:
                    DoTCall(0xffce);
                    break;
                case 0x91:
                    DoTCall(0xffcc);
                    break;
                case 0xa1:
                    DoTCall(0xffca);
                    break;
                case 0xb1:
                    DoTCall(0xffc8);
                    break;
                case 0xc1:
                    DoTCall(0xffc6);
                    break;
                case 0xd1:
                    DoTCall(0xffc4);
                    break;
                case 0xe1:
                    DoTCall(0xffc2);
                    break;
                case 0xf1:
                    DoTCall(0xffc0);
                    break;

                case 0x6f: // RET
                    ProgramCounter = PullShort();
                    break;
                case 0x7f: // RET1
                    ProgramStatus = PullByte();
                    ProgramCounter = PullShort();
                    break;
                // JMP
                case 0x1f:
                    ProgramCounter = GetCodeParamEffectiveAddress(AddressingType.AbsoluteIndexedXIndirect);
                    break;
                case 0x5f:
                    ProgramCounter = GetCodeParamEffectiveAddress(AddressingType.Absolute);
                    break;

                // CLR1
                case 0x12:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 0);
                    break;
                case 0x32:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 1);
                    break;
                case 0x52:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 2);
                    break;
                case 0x72:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 3);
                    break;
                case 0x92:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 4);
                    break;
                case 0xb2:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 5);
                    break;
                case 0xd2:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 6);
                    break;
                case 0xf2:
                    ClearBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 7);
                    break;
                // SET1
                case 0x02:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 0);
                    break;
                case 0x22:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 1);
                    break;
                case 0x42:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 2);
                    break;
                case 0x62:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 3);
                    break;
                case 0x82:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 4);
                    break;
                case 0xa2:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 5);
                    break;
                case 0xc2:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 6);
                    break;
                case 0xe2:
                    SetBit(GetCodeParamEffectiveAddress(AddressingType.DirectPage), 7);
                    break;
                case 0x60: // CLRC
                    Carry = false;
                    break;
                case 0x20: // CLRP
                    DirectPage = false;
                    break;
                case 0xe0: // CLRV
                    Overflow = false;
                    HalfCarry = false;
                    break;
                case 0x80: // SETC
                    Carry = true;
                    break;
                case 0x40: // SETP
                    DirectPage = true;
                    break;
                case 0xed: // NOTC
                    Carry = !Carry;
                    break;
                case 0xc0: // DI
                    InterruptEnabled = false;
                    break;
                case 0xa0: // EI
                    InterruptEnabled = true;
                    break;
                case 0x9f: // XCA
                    Accumulator = (byte)(Accumulator >> 4 | Accumulator << 4);
                    SetNegativeAndZero(Accumulator);
                    break;

                // POP
                case 0xae:
                    Accumulator = PullByte();
                    break;
                case 0x8e:
                    ProgramStatus = PullByte();
                    break;
                case 0xce:
                    X = PullByte();
                    break;
                case 0xee:
                    Y = PullByte();
                    break;
                // PUSH
                case 0x2d:
                    PushByte(Accumulator);
                    break;
                case 0x0d:
                    PushByte(ProgramStatus);
                    break;
                case 0x4d:
                    PushByte(X);
                    break;
                case 0x6d:
                    PushByte(Y);
                    break;
            }
        }

        private void DoCall(ushort address)
        {
            PushShort(ProgramCounter);
            ProgramCounter = address;
        }

        private void DoTCall(ushort lookupAddress)
        {
            DoCall(ReadShort(lookupAddress));
        }

        /// <summary>
        /// Argument is 13 bit address, with 3 high bits addressing the bit within the addressed byte.
        /// </summary>
        private bool GetBit(ushort bitAddress)
        {
            return (ReadByte((ushort)(bitAddress & 0x1fff)) >> (bitAddress >> 13) & 1) != 0;
        }

        private static bool GetBit(byte b, int bit)
        {
            return (b >> bit & 1) != 0;
        }

        private void ClearBit(ushort address, int bit)
        {
            WriteByte(address, (byte)(ReadByte(address) & (byte)~(1 << bit)));
        }

        private void SetBit(ushort address, int bit)
        {
            WriteByte(address, (byte)(ReadByte(address) | (byte)(1 << bit)));
        }

        private void PushByte(byte b)
        {
            Psram[Stack--] = b;
        }

        private void PushShort(ushort u)
        {
            PushByte((byte)(u >> 8));
            PushByte((byte)u);
        }

        private byte PullByte()
        {
            return Psram[++Stack];
        }

        private ushort PullShort()
        {
            var low = PullByte();
            return (ushort)(low | PullByte() << 8);
        }

        private void SetNegativeAndZero(byte b)
        {
            Zero = b == 0;
            Negative = (b & 0x80) != 0;
        }

        private void DoIncrement(ref byte a)
        {
            a++;
            SetNegativeAndZero(a);
        }

        private void DoIncrement(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var b = ReadByte(address);
            DoIncrement(ref b);
            WriteByte(address, b);
        }

        private void DoIncrementWord(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var val = ReadShort(address);
            val++;
            Zero = val == 0;
            Negative = (val & 0x8000) != 0;
            WriteShort(address, val);
        }

        private void DoDecrement(ref byte a)
        {
            a--;
            SetNegativeAndZero(a);
        }

        private void DoDecrement(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var b = ReadByte(address);
            DoDecrement(ref b);
            WriteByte(address, b);
        }

        private void DoDecrementWord(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var val = ReadShort(address);
            val--;
            Zero = val == 0;
            Negative = (val & 0x8000) != 0;
            WriteShort(address, val);
        }

        private byte DoAdd(byte a, byte b)
        {
            var result = a + b + (Carry ? 1 : 0);
            Carry = result > 0xff;
            Overflow = (~(a ^ b) & (a ^ result) & 0x80) != 0; // Don't understand this, found this code here: https://github.com/mamedev/mame/blob/master/src/devices/cpu/spc700/spc700.cpp
            HalfCarry = ((result & 0x0f) - ((a & 0xf) + (Carry ? 1 : 0)) & 0x10) != 0; // Same as above
            SetNegativeAndZero((byte)result);
            return (byte)result;
        }

        private byte DoSubtract(byte a, byte b)
        {
            var result = a - b - (Carry ? 0 : 1);
            // Overflow and HalfCarry are set as in Add(a, ~b)
            DoAdd(a, (byte)~b);
            Carry = a >= b;
            SetNegativeAndZero((byte)result);
            return (byte)result;
        }

        private void DoCompare(byte a, byte b)
        {
            var result = a - b;
            Negative = result < 0;
            Zero = a == b;
            Carry = a >= b;
        }

        private byte DoAnd(byte a, byte b)
        {
            var result = (byte)(a & b);
            SetNegativeAndZero(result);
            return result;
        }

        private byte DoOr(byte a, byte b)
        {
            var result = (byte)(a | b);
            SetNegativeAndZero(result);
            return result;
        }

        private byte DoExclusiveOr(byte a, byte b)
        {
            var result = (byte)(a ^ b);
            SetNegativeAndZero(result);
            return result;
        }

        private void DoLeftShift(ref byte b)
        {
            Carry = (b & 0x80) != 0;
            b <<= 1;
        }

        private void DoLeftShift(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var b = ReadByte(address);
            DoLeftShift(ref b);
            WriteByte(address, b);
        }

        private void DoRightShift(ref byte b)
        {
            Carry = (b & 0x1) != 0;
            b >>= 1;
        }

        private void DoRightShift(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var b = ReadByte(address);
            DoRightShift(ref b);
            WriteByte(address, b);
        }

        private void DoRotateLeft(ref byte b)
        {
            var carryBefore = Carry;
            Carry = (b & 0x1) != 0;
            b <<= 1;
            if (carryBefore)
                b |= 1;
        }

        private void DoRotateLeft(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var b = ReadByte(address);
            DoRotateLeft(ref b);
            WriteByte(address, b);
        }

        private void DoRotateRight(ref byte b)
        {
            var carryBefore = Carry;
            Carry = (b & 0x1) != 0;
            b >>= 1;
            if (carryBefore)
                b |= 0x80;
        }

        private void DoRotateRight(AddressingType addressingType)
        {
            var address = GetCodeParamEffectiveAddress(addressingType);
            var b = ReadByte(address);
            DoRotateRight(ref b);
            WriteByte(address, b);
        }

        private void DoDirectPageBranchOnBit(int bit, bool branchIfSet)
        {
            DoBranchInstruction(GetBit(ReadByte(AddressingType.DirectPage), bit) == branchIfSet);
        }

        private void DoBranchInstruction(bool condition)
        {
            var branchOffset = (sbyte)ReadByte(AddressingType.Immediate);
            if (condition)
                ProgramCounter = (ushort)(ProgramCounter + branchOffset);
        }

        private void HandleControlWrite(byte b)
        {
            _timers[0].SetEnabled((b & 0x1) != 0);
            _timers[1].SetEnabled((b & 0x2) != 0);
            _timers[2].SetEnabled((b & 0x4) != 0);
            if ((b & 0x10) != 0)
            {
                Port0In = 0;
                Port1In = 0;
            }
            if ((b & 0x20) != 0)
            {
                Port2In = 0;
                Port3In = 0;
            }
            _iplRegionReadable = (b & 0x80) != 0;
        }

        private enum AddressingType
        {
            Immediate,
            Absolute,
            AbsoluteIndexedX,
            AbsoluteIndexedXIndirect,
            AbsoluteIndexedY,
            DirectPage,
            X,
            Y,
            DirectPageIndexedX,
            DirectPageIndexedY,
            DirectPageIndexedXIndirect,
            DirectPageIndirectIndexedY,
            AbsoluteBit,
        }
    }
}
