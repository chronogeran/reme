using System;
using System.Collections.Generic;
using System.Threading;
using static RemeSnes.RemeSnes;

namespace RemeSnes.Hardware
{
    /// <summary>
    /// Runs at 21.477273 MHz.
    /// </summary>
    internal class Cpu
    {
        // Processor Registers
        public ushort Accumulator;
        public ushort X;
        public ushort Y;
        public byte ProcessorStatus;
        public bool EmulationBit;
        public byte ProgramBank;
        public ushort ProgramCounter;
        public byte DataBank;
        public ushort DirectPage;
        public ushort Stack;

        // Emulation state
        ulong _cyclesRun;

        public const byte DIRECT_PAGE_BANK = 0x7e;

        // Status flags
        public bool Carry { get { return (ProcessorStatus & 1) != 0; } set { if (value) ProcessorStatus |= 1; else ProcessorStatus &= 0xfe; } }
        public bool Zero { get { return (ProcessorStatus & 2) != 0; } set { if (value) ProcessorStatus |= 2; else ProcessorStatus &= 0xfd; } }
        public bool IRQDisable { get { return (ProcessorStatus & 4) != 0; } set { if (value) ProcessorStatus |= 4; else ProcessorStatus &= 0xfb; } }
        public bool DecimalMode { get { return (ProcessorStatus & 8) != 0; } set { if (value) ProcessorStatus |= 8; else ProcessorStatus &= 0xf7; } }
        public bool Index8Bit { get { return (ProcessorStatus & 0x10) != 0; } set { if (value) ProcessorStatus |= 0x10; else ProcessorStatus &= 0xef; } }
        public bool Accumulator8bit { get { return (ProcessorStatus & 0x20) != 0; } set { if (value) ProcessorStatus |= 0x20; else ProcessorStatus &= 0xdf; } }
        public bool Overflow { get { return (ProcessorStatus & 0x40) != 0; } set { if (value) ProcessorStatus |= 0x40; else ProcessorStatus &= 0xbf; } }
        public bool Negative { get { return (ProcessorStatus & 0x80) != 0; } set { if (value) ProcessorStatus |= 0x80; else ProcessorStatus &= 0x7f; } }

        // TODO zero out high byte of index registers when switching size
        // TODO implement decimal mode
        private Bus _bus;

        public bool WaitingForVBlank { get; private set; }

        public Cpu()
        {
            //_cpuThread = new Thread(ThreadLoop) { Name = "CPU Thread" };
            //_cpuThread.Start();
        }

        public void SetBus(Bus bus)
        {
            _bus = bus;
        }

        internal void Run(uint cycles)
        {
            // For now a rough approximation
            var instructions = cycles / 4;

            if (_vblankOccurred)
            {
                // Handle NMI signal
                _vblankOccurred = false;
                WaitingForVBlank = false;
                PushByte(ProgramBank);
                PushShort(ProgramCounter);
                ProgramBank = 0;
                ProgramCounter = _bus.GetNMIVector(EmulationBit);
            }

            for (int i = 0; i < instructions; i++)
            {
                if (WaitingForVBlank) break;
                RunOneInstruction();
            }

            _cyclesRun += cycles;
        }

        public void EmulateFrame()
        {
            _emulateSignal.Set();
        }

        private bool _vblankOccurred;
        private Thread _cpuThread;
        private ManualResetEvent _emulateSignal = new ManualResetEvent(false);
        private ManualResetEvent _vblankSignal = new ManualResetEvent(false);
        private bool _shuttingDown = false;
        private void ThreadLoop()
        {
            while (!_shuttingDown)
            {
                _emulateSignal.WaitOne();
                _emulateSignal.Reset();
                // Begin frame

                while (!WaitingForVBlank && !_vblankSignal.WaitOne(0))
                {
                    RunOneInstruction();
                }

                if (_bus.NmiEnable)
                {
                    _vblankSignal.WaitOne();
                    _vblankSignal.Reset();
                    // VBlank processing
                    WaitingForVBlank = false;
                    PushByte(ProgramBank);
                    PushShort(ProgramCounter);
                    ProgramBank = 0;
                    ProgramCounter = _bus.GetNMIVector(EmulationBit);
                    RunNmiHandler();
                }
            }
        }

        private void RunNmiHandler()
        {
            while (RunOneInstruction() != 0x40) ;
        }

        public void Begin(ushort resetVector)
        {
            ProgramBank = 0;
            EmulationBit = true;
            ProgramCounter = resetVector;
        }

        public void TriggerVBlank()
        {
            _vblankSignal.Set();
        }

        public byte RunOneInstruction()
        {
            var opcode = _bus.ReadByte(ProgramBank, ProgramCounter);
            if (_breakpoints.ContainsKey((int)MakeAddress(ProgramBank, ProgramCounter)))
            {
                // TODO handle mirrored addresses (multiple addresses pointing to same location)
                var bp = _breakpoints[(int)MakeAddress(ProgramBank, ProgramCounter)];
                if ((bp.Flags & RemeSnes.BreakpointFlags.Execute) != 0)
                    Console.WriteLine($"CPU BP hit: {bp.Name} at {bp.Address:x6}");
            }
            ProgramCounter++;

            switch (opcode)
            {
                case 0xea: // NOP
                    break;

                case 0: // BRK
                    PushByte(ProgramBank);
                    ProgramCounter++;
                    PushShort(ProgramCounter);
                    PushByte(ProcessorStatus);
                    IRQDisable = true;
                    DecimalMode = false;
                    ProgramBank = 0;
                    ProgramCounter = _bus.GetBRKVector(EmulationBit);
                    break;
                case 0x2: // COP
                    PushByte(ProgramBank);
                    ProgramCounter++;
                    PushShort(ProgramCounter);
                    PushByte(ProcessorStatus);
                    ProgramBank = 0;
                    // TODO interrupt status flag?
                    ProgramCounter = _bus.GetCOPVector(EmulationBit);
                    // TODO decimal flag is cleared after the fact
                    break;
                case 0xdb: // STP
                    throw new Exception("Processor stopped");
                    break;
                case 0xcb: // WAI
                    WaitingForVBlank = true;
                    break;
                case 0x42: // WDM
                    throw new Exception("WDM encountered");
                    break;

                case 0x58: // CLI
                    IRQDisable = false;
                    break;
                case 0x78: // SEI
                    IRQDisable = true;
                    break;
                case 0x18: // CLC
                    Carry = false;
                    break;
                case 0x38: // SEC
                    Carry = true;
                    break;
                case 0xd8: // CLD
                    DecimalMode = false;
                    break;
                case 0xf8: // SED
                    DecimalMode = true;
                    break;
                case 0xb8: // CLV
                    Overflow = false;
                    break;

                case 0x48: // PHA
                    if (Accumulator8bit)
                        PushByte((byte)Accumulator);
                    else
                        PushShort(Accumulator);
                    break;
                case 0x08: // PHP
                    PushByte(ProcessorStatus);
                    break;
                case 0xda: // PHX
                    if (Index8Bit)
                        PushByte((byte)X);
                    else
                        PushShort(X);
                    break;
                case 0x5a: // PHY
                    if (Index8Bit)
                        PushByte((byte)Y);
                    else
                        PushShort(Y);
                    break;
                case 0x68: // PLA
                    if (Accumulator8bit)
                        SetAccumulatorLowByte(PullByte());
                    else
                        Accumulator = PullShort();
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case 0x28: // PLP
                    ProcessorStatus = PullByte();
                    break;
                case 0xfa: // PLX
                    if (Index8Bit)
                        SetXLowByte(PullByte());
                    else
                        X = PullShort();
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0x7a: // PLY
                    if (Index8Bit)
                        SetYLowByte(PullByte());
                    else
                        Y = PullShort();
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0x8b: // PHB
                    PushByte(DataBank);
                    break;
                case 0x0b: // PHD
                    PushShort(DirectPage);
                    break;
                case 0x4b: // PHK
                    PushByte(ProgramBank);
                    break;
                case 0xab: // PLB
                    DataBank = PullByte();
                    SetZeroAndNegativeFlagsFromValue(DataBank, true);
                    break;
                case 0x2b: // PLD
                    DirectPage = PullShort();
                    SetZeroAndNegativeFlagsFromValue(DirectPage, false);
                    break;

                case 0xf4: // PEA
                    PushShort(ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, false));
                    break;
                case 0xd4: // PEI
                    PushShort(ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, false));
                    break;
                case 0x62: // PER
                    {
                        var relativeOffset = ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, false);
                        PushShort((ushort)(ProgramCounter + relativeOffset));
                    }
                    break;

                case 0xeb: // XBA
                    Accumulator = (ushort)(((Accumulator & 0xff) << 8) | ((Accumulator & 0xff00) >> 8));
                    SetZeroAndNegativeFlagsFromValue(Accumulator, true);
                    break;
                case 0xfb: // XCE
                    // Switching into native mode sets M (and X?) to 1
                    if (!Carry && EmulationBit)
                    {
                        Accumulator8bit = true;
                        Index8Bit = true;
                    }
                    (Carry, EmulationBit) = (EmulationBit, Carry);
                    break;

                case 0xe2: // SEP
                    ProcessorStatus |= (byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, true);
                    break;
                case 0xc2: // REP
                    ProcessorStatus &= (byte)~ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, true);
                    break;

                case 0xaa: // TAX
                    if (Index8Bit)
                        SetXLowByte((byte)Accumulator);
                    else
                        X = Accumulator;
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0xa8: // TAY
                    if (Index8Bit)
                        SetYLowByte((byte)Accumulator);
                    else
                        Y = Accumulator;
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0x8a: // TXA
                    if (Accumulator8bit)
                        SetAccumulatorLowByte((byte)X);
                    else
                        Accumulator = X;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case 0x98: // TYA
                    if (Accumulator8bit)
                        SetAccumulatorLowByte((byte)Y);
                    else
                        Accumulator = Y;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case 0xba: // TSX
                    if (Index8Bit)
                        SetXLowByte((byte)Stack);
                    else
                        X = Stack;
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0x9a: // TXS
                    Stack = X;
                    SetZeroAndNegativeFlagsFromValue(X, false);
                    break;
                case 0x9b: // TXY
                    Y = X;
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xbb: // TYX
                    X = Y;
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;

                case 0x5b: // TCD
                    DirectPage = Accumulator;
                    SetZeroAndNegativeFlagsFromValue(Accumulator, false);
                    break;
                case 0x7b: // TDC
                    Accumulator = DirectPage;
                    SetZeroAndNegativeFlagsFromValue(Accumulator, false);
                    break;
                case 0x1b: // TCS
                    Stack = Accumulator;
                    // No flags affected on TCS
                    break;
                case 0x3b: // TSC
                    Accumulator = Stack;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;

                // ORA
                case 0x09:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0x0d:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0x0f:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0x05:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0x12:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0x07:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0x1d:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0x1f:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0x19:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0x15:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0x01:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0x11:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0x17:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0x03:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0x13:
                    DoAccumulatorOperation(OperationType.Or, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // AND
                case 0x29:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0x2d:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0x2f:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0x25:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0x32:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0x27:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0x3d:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0x3f:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0x39:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0x35:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0x21:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0x31:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0x37:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0x23:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0x33:
                    DoAccumulatorOperation(OperationType.And, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // EOR
                case 0x49:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0x4d:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0x4f:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0x45:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0x52:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0x47:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0x5d:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0x5f:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0x59:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0x55:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0x41:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0x51:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0x57:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0x43:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0x53:
                    DoAccumulatorOperation(OperationType.ExclusiveOr, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // ADC
                case 0x69:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0x6d:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0x6f:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0x65:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0x72:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0x67:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0x7d:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0x7f:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0x79:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0x75:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0x61:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0x71:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0x77:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0x63:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0x73:
                    DoAccumulatorOperation(OperationType.Add, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // STA
                case 0x8d:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0x8f:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteLong));
                    break;
                case 0x85:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0x92:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndirect));
                    break;
                case 0x87:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong));
                    break;
                case 0x9d:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x9f:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX));
                    break;
                case 0x99:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY));
                    break;
                case 0x95:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;
                case 0x81:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX));
                    break;
                case 0x91:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY));
                    break;
                case 0x97:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY));
                    break;
                case 0x83:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.StackRelative));
                    break;
                case 0x93:
                    StoreAccumulator(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY));
                    break;
                // LDA
                case 0xa9:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0xad:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0xaf:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0xa5:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0xb2:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0xa7:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0xbd:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0xbf:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0xb9:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0xb5:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0xa1:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0xb1:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0xb7:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0xa3:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0xb3:
                    DoAccumulatorOperation(OperationType.Load, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // CMP
                case 0xc9:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0xcd:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0xcf:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0xc5:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0xd2:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0xc7:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0xdd:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0xdf:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0xd9:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0xd5:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0xc1:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0xd1:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0xd7:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0xc3:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0xd3:
                    DoAccumulatorOperation(OperationType.Compare, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // SBC
                case 0xe9:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0xed:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0xef:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLong, Accumulator8bit));
                    break;
                case 0xe5:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0xf2:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirect, Accumulator8bit));
                    break;
                case 0xe7:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLong, Accumulator8bit));
                    break;
                case 0xfd:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0xff:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteLongIndexedX, Accumulator8bit));
                    break;
                case 0xf9:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Accumulator8bit));
                    break;
                case 0xf5:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                case 0xe1:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedIndirectX, Accumulator8bit));
                    break;
                case 0xf1:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectIndexedY, Accumulator8bit));
                    break;
                case 0xf7:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndirectLongIndexedY, Accumulator8bit));
                    break;
                case 0xe3:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelative, Accumulator8bit));
                    break;
                case 0xf3:
                    DoAccumulatorOperation(OperationType.Subtract, ReadInstructionValueAndIncrementProgramCounter(AddressingType.StackRelativeIndirectIndexedY, Accumulator8bit));
                    break;
                // ASL
                case 0x0a:
                    {
                        if (Accumulator8bit)
                        {
                            SetAccumulatorLowByte((byte)DoShiftLeft(Accumulator));
                        }
                        else
                        {
                            Accumulator = DoShiftLeft(Accumulator);
                        }
                    }
                    break;
                case 0x0e:
                    DoShiftLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0x06:
                    DoShiftLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0x1e:
                    DoShiftLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x16:
                    DoShiftLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;
                // LSR
                case 0x4a:
                    {
                        if (Accumulator8bit)
                        {
                            SetAccumulatorLowByte((byte)DoShiftRight(Accumulator));
                        }
                        else
                        {
                            Accumulator = DoShiftRight(Accumulator);
                        }
                    }
                    break;
                case 0x4e:
                    DoShiftRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0x46:
                    DoShiftRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0x5e:
                    DoShiftRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x56:
                    DoShiftRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;
                // ROL
                case 0x2a:
                    {
                        if (Accumulator8bit)
                        {
                            SetAccumulatorLowByte((byte)DoRotateLeft(Accumulator));
                        }
                        else
                        {
                            Accumulator = DoRotateLeft(Accumulator);
                        }
                    }
                    break;
                case 0x2e:
                    DoRotateLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0x26:
                    DoRotateLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0x3e:
                    DoRotateLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x36:
                    DoRotateLeftOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;
                // ROR
                case 0x6a:
                    {
                        if (Accumulator8bit)
                        {
                            SetAccumulatorLowByte((byte)DoRotateRight(Accumulator));
                        }
                        else
                        {
                            Accumulator = DoRotateRight(Accumulator);
                        }
                    }
                    break;
                case 0x6e:
                    DoRotateRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0x66:
                    DoRotateRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0x7e:
                    DoRotateRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0x76:
                    DoRotateRightOnMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;
                // INC
                case 0x1a:
                    if (Accumulator8bit)
                    {
                        SetAccumulatorLowByte((byte)(Accumulator + 1));
                    }
                    else
                    {
                        Accumulator++;
                    }
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case 0xe8:
                    if (Index8Bit)
                    {
                        SetXLowByte((byte)(X + 1));
                    }
                    else
                    {
                        X++;
                    }
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0xc8:
                    if (Index8Bit)
                    {
                        SetYLowByte((byte)(Y + 1));
                    }
                    else
                    {
                        Y++;
                    }
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xee:
                    IncrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0xe6:
                    IncrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0xfe:
                    IncrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0xf6:
                    IncrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;
                // DEC
                case 0x3a:
                    if (Accumulator8bit)
                    {
                        SetAccumulatorLowByte((byte)(Accumulator - 1));
                    }
                    else
                    {
                        Accumulator--;
                    }
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case 0xca:
                    if (Index8Bit)
                    {
                        SetXLowByte((byte)(X - 1));
                    }
                    else
                    {
                        X--;
                    }
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0x88:
                    if (Index8Bit)
                    {
                        SetYLowByte((byte)(Y - 1));
                    }
                    else
                    {
                        Y--;
                    }
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xce:
                    DecrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0xc6:
                    DecrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                case 0xde:
                    DecrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX));
                    break;
                case 0xd6:
                    DecrementMemory(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX));
                    break;

                // STZ
                case 0x9c:
                    if (Accumulator8bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute), 0);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute), 0);
                    break;
                case 0x64:
                    if (Accumulator8bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage), 0);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage), 0);
                    break;
                case 0x9e:
                    if (Accumulator8bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX), 0);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX), 0);
                    break;
                case 0x74:
                    if (Accumulator8bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX), 0);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX), 0);
                    break;
                // STX
                case 0x8e:
                    if (Index8Bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute), (byte)X);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute), X);
                    break;
                case 0x86:
                    if (Index8Bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage), (byte)X);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage), X);
                    break;
                case 0x96:
                    if (Index8Bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedY), (byte)X);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedY), X);
                    break;
                // STY
                case 0x8c:
                    if (Index8Bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute), (byte)Y);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute), Y);
                    break;
                case 0x84:
                    if (Index8Bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage), (byte)Y);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage), Y);
                    break;
                case 0x94:
                    if (Index8Bit)
                        _bus.WriteByte(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX), (byte)Y);
                    else
                        _bus.WriteShort(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPageIndexedX), Y);
                    break;
                // LDX
                case 0xa2:
                    X = ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0xae:
                    if (Index8Bit)
                        SetXLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Index8Bit));
                    else
                        X = ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0xa6:
                    if (Index8Bit)
                        SetXLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Index8Bit));
                    else
                        X = ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0xbe:
                    if (Index8Bit)
                        SetXLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Index8Bit));
                    else
                        X = ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedY, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                case 0xb6:
                    if (Index8Bit)
                        SetXLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedY, Index8Bit));
                    else
                        X = ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedY, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(X, Index8Bit);
                    break;
                // LDY
                case 0xa0:
                    Y = ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xac:
                    if (Index8Bit)
                        SetYLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Index8Bit));
                    else
                        Y = ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xa4:
                    if (Index8Bit)
                        SetYLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Index8Bit));
                    else
                        Y = ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xbc:
                    if (Index8Bit)
                        SetYLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Index8Bit));
                    else
                        Y = ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                case 0xb4:
                    if (Index8Bit)
                        SetYLowByte((byte)ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Index8Bit));
                    else
                        Y = ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Index8Bit);
                    SetZeroAndNegativeFlagsFromValue(Y, Index8Bit);
                    break;
                // CPX
                case 0xe0:
                    DoCompare(X, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Index8Bit), Index8Bit);
                    break;
                case 0xec:
                    DoCompare(X, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Index8Bit), Index8Bit);
                    break;
                case 0xe4:
                    DoCompare(X, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Index8Bit), Index8Bit);
                    break;
                // CPY
                case 0xc0:
                    DoCompare(Y, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Index8Bit), Index8Bit);
                    break;
                case 0xcc:
                    DoCompare(Y, ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Index8Bit), Index8Bit);
                    break;
                case 0xc4:
                    DoCompare(Y, ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Index8Bit), Index8Bit);
                    break;
                // BIT
                case 0x89:
                    DoTestBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.Immediate, Accumulator8bit));
                    break;
                case 0x2c:
                    DoTestBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0x24:
                    DoTestBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;
                case 0x3c:
                    DoTestBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, Accumulator8bit));
                    break;
                case 0x34:
                    DoTestBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPageIndexedX, Accumulator8bit));
                    break;
                // TRB
                case 0x1c:
                    DoTestResetBits(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.Absolute));
                    break;
                case 0x14:
                    DoTestResetBits(GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType.DirectPage));
                    break;
                // TSB
                case 0x0c:
                    DoTestSetBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, Accumulator8bit));
                    break;
                case 0x04:
                    DoTestSetBits(ReadInstructionValueAndIncrementProgramCounter(AddressingType.DirectPage, Accumulator8bit));
                    break;

                case 0x54: // MVN
                    {
                        var destinationBank = _bus.ReadByte(ProgramBank, ProgramCounter++);
                        var sourceBank = _bus.ReadByte(ProgramBank, ProgramCounter++);
                        while (Accumulator != 0xffff)
                        {
                            _bus.WriteByte(destinationBank, Y++, _bus.ReadByte(sourceBank, X++));
                            Accumulator--;
                        }
                    }
                    break;
                case 0x44: // MVP
                    {
                        var destinationBank = _bus.ReadByte(ProgramBank, ProgramCounter++);
                        var sourceBank = _bus.ReadByte(ProgramBank, ProgramCounter++);
                        while (Accumulator != 0xffff)
                        {
                            _bus.WriteByte(destinationBank, Y--, _bus.ReadByte(sourceBank, X--));
                            Accumulator--;
                        }
                    }
                    break;

                case 0x80: // BRA
                    ReadOffsetAndTakeBranch();
                    break;
                case 0x90: // BCC
                    if (!Carry)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0xb0: // BCS
                    if (Carry)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0xf0: // BEQ
                    if (Zero)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0xd0: // BNE
                    if (!Zero)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0x30: // BMI
                    if (Negative)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0x10: // BPL
                    if (!Negative)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0x50: // BVC
                    if (!Overflow)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0x70: // BVS
                    if (Overflow)
                        ReadOffsetAndTakeBranch();
                    else
                        ProgramCounter++;
                    break;
                case 0x82: // BRL
                    {
                        var relativeSkip = (short)_bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        ProgramCounter = (ushort)(ProgramCounter + relativeSkip);
                    }
                    break;

                // JMP
                case 0x4c:
                    ProgramCounter = _bus.ReadShort(ProgramBank, ProgramCounter);
                    break;
                case 0x6c:
                    // TODO indirect
                    ProgramCounter = ReadInstructionValueAndIncrementProgramCounter(AddressingType.Absolute, false);
                    break;
                case 0x7c:
                    // TODO absolute indexed indirect
                    ProgramCounter = ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, false);
                    break;
                case 0x5c:
                    {
                        var addr = _bus.ReadLong(ProgramBank, ProgramCounter);
                        ProgramCounter = (ushort)addr;
                        ProgramBank = (byte)(addr >> 16);
                    }
                    break;
                case 0xdc:
                    // TODO absolute indirect long
                    {
                        var addr1 = _bus.ReadShort(ProgramBank, ProgramCounter);
                        var destination = _bus.ReadLong(DataBank, addr1);
                        ProgramCounter = (ushort)destination;
                        ProgramBank = (byte)(destination >> 16);
                    }
                    break;

                case 0x22: // JSL
                    {
                        var destination = _bus.ReadLong(ProgramBank, ProgramCounter);
                        ProgramCounter += 3;
                        PushByte(ProgramBank);
                        PushShort((ushort)(ProgramCounter - 1));
                        ProgramCounter = (ushort)destination;
                        ProgramBank = (byte)(destination >> 16);
                    }
                    break;
                case 0x20: // JSR
                    {
                        var destination = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        PushShort((ushort)(ProgramCounter - 1));
                        ProgramCounter = destination;
                    }
                    break;
                case 0xfc: // JSR
                    // TODO absolute indexed indirect
                    {
                        var destination = ReadInstructionValueAndIncrementProgramCounter(AddressingType.AbsoluteIndexedX, false);
                        PushShort((ushort)(ProgramCounter - 1));
                        ProgramCounter = destination;
                    }
                    break;

                case 0x40: // RTI
                    // TODO verify
                    ProgramCounter = PullShort();
                    ProgramBank = PullByte();
                    break;
                case 0x6b: // RTL
                    ProgramCounter = PullShort();
                    ProgramCounter++;
                    ProgramBank = PullByte();
                    break;
                case 0x60: // RTS
                    ProgramCounter = PullShort();
                    ProgramCounter++;
                    break;

                default:
                    throw new Exception("Unhandled opcode " + opcode);
            }

            return opcode;
        }

        private void DoTestResetBits(uint addr)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(addr);
                Zero = ((byte)Accumulator & val) == 0;
                _bus.WriteByte(addr, (byte)((~Accumulator) & val));
            }
            else
            {
                var val = _bus.ReadShort(addr);
                Zero = (Accumulator & val) == 0;
                _bus.WriteShort(addr, (ushort)((~Accumulator) & val));
            }
        }

        private void DoTestSetBits(uint addr)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(addr);
                Zero = ((byte)Accumulator & val) == 0;
                _bus.WriteByte(addr, (byte)(Accumulator | val));
            }
            else
            {
                var val = _bus.ReadShort(addr);
                Zero = (Accumulator & val) == 0;
                _bus.WriteShort(addr, (ushort)(Accumulator | val));
            }
        }

        private void ReadOffsetAndTakeBranch()
        {
            var relativeSkip = (sbyte)_bus.ReadByte(ProgramBank, ProgramCounter++);
            ProgramCounter = (ushort)(ProgramCounter + relativeSkip);
        }

        private uint MakeAddress(byte bank, ushort addr)
        {
            return (uint)((bank << 16) | addr);
        }

        private void StoreAccumulator(uint address)
        {
            if (Accumulator8bit)
                _bus.WriteByte(address, (byte)Accumulator);
            else
                _bus.WriteShort(address, Accumulator);
        }

        private ushort DoShiftLeft(ushort val)
        {
            if (Accumulator8bit)
            {
                var result = (ushort)(val << 1);
                Carry = (result & 0x100) != 0;
                SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
                return result;
            }
            else
            {
                var result = val << 1;
                Carry = (result & 0x10000) != 0;
                SetZeroAndNegativeFlagsFromValue((ushort)result, Accumulator8bit);
                return (ushort)result;
            }
        }

        private void DoShiftLeftOnMemory(uint address)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(address);
                _bus.WriteByte(address, (byte)DoShiftLeft(val));
            }
            else
            {
                var val = _bus.ReadShort(address);
                _bus.WriteShort(address, DoShiftLeft(val));
            }
        }

        private ushort DoShiftRight(ushort val)
        {
            Carry = (val & 1) != 0;
            var result = Accumulator8bit
                ? (ushort)(((byte)val) >> 1)
                : (ushort)(val >> 1);
            SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
            return result;
        }

        private void DoShiftRightOnMemory(uint address)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(address);
                _bus.WriteByte(address, (byte)DoShiftRight(val));
            }
            else
            {
                var val = _bus.ReadShort(address);
                _bus.WriteShort(address, DoShiftRight(val));
            }
        }

        private ushort DoRotateRight(ushort val)
        {
            var setCarry = (val & 1) != 0;
            var result = Accumulator8bit
                ? (ushort)(((byte)val) >> 1)
                : (ushort)(val >> 1);
            if (Carry)
            {
                if (Accumulator8bit)
                    result |= 0x80;
                else
                    result |= 0x8000;
            }
            Carry = setCarry;
            SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
            return result;
        }

        private void DoRotateRightOnMemory(uint address)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(address);
                _bus.WriteByte(address, (byte)DoRotateRight(val));
            }
            else
            {
                var val = _bus.ReadShort(address);
                _bus.WriteShort(address, DoRotateRight(val));
            }
        }

        private ushort DoRotateLeft(ushort val)
        {
            if (Accumulator8bit)
            {
                var setCarry = (val & 0x80) != 0;
                var result = (ushort)(val << 1);
                if (Carry)
                    result |= 1;
                Carry = setCarry;
                SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
                return result;
            }
            else
            {
                var setCarry = (val & 0x8000) != 0;
                var result = val << 1;
                if (Carry)
                    result |= 1;
                Carry = setCarry;
                SetZeroAndNegativeFlagsFromValue((ushort)result, Accumulator8bit);
                return (ushort)result;
            }
        }

        private void DoRotateLeftOnMemory(uint address)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(address);
                _bus.WriteByte(address, (byte)DoRotateRight(val));
            }
            else
            {
                var val = _bus.ReadShort(address);
                _bus.WriteShort(address, DoRotateRight(val));
            }
        }

        private void IncrementMemory(uint address)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(address);
                var result = (byte)(val + 1);
                SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
                _bus.WriteByte(address, result);
            }
            else
            {
                var val = _bus.ReadShort(address);
                var result = (ushort)(val + 1);
                SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
                _bus.WriteShort(address, result);
            }
        }

        private void DecrementMemory(uint address)
        {
            if (Accumulator8bit)
            {
                var val = _bus.ReadByte(address);
                var result = (byte)(val - 1);
                SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
                _bus.WriteByte(address, result);
            }
            else
            {
                var val = _bus.ReadShort(address);
                var result = (ushort)(val - 1);
                SetZeroAndNegativeFlagsFromValue(result, Accumulator8bit);
                _bus.WriteShort(address, result);
            }
        }

        private uint GetInstructionParameterAddressAndIncrementProgramCounter(AddressingType addressingType)
        {
            // TODO handle rollover properly
            switch (addressingType)
            {
                case AddressingType.Absolute:
                    {
                        var addr = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        return MakeAddress(DataBank, addr);
                    }
                case AddressingType.AbsoluteLong:
                    {
                        var addr = _bus.ReadLong(ProgramBank, ProgramCounter);
                        ProgramCounter += 3;
                        return addr;
                    }
                case AddressingType.DirectPage:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return MakeAddress(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr));
                    }
                case AddressingType.DirectPageIndirect:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr));
                        return MakeAddress(DataBank, pointer);
                    }
                case AddressingType.DirectPageIndirectLong:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return _bus.ReadLong(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr));
                    }
                case AddressingType.AbsoluteIndexedX:
                    {
                        var addr = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        return MakeAddress(DataBank, (ushort)(addr + X));
                    }
                case AddressingType.AbsoluteLongIndexedX:
                    {
                        var addr = _bus.ReadLong(ProgramBank, ProgramCounter);
                        ProgramCounter += 3;
                        return addr + X;
                    }
                case AddressingType.AbsoluteIndexedY:
                    {
                        var addr = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        return MakeAddress(DataBank, (ushort)(addr + Y));
                    }
                case AddressingType.DirectPageIndexedX:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return MakeAddress(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr + X));
                    }
                case AddressingType.DirectPageIndexedIndirectX: //  ORA (dp,X) 
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr + X));
                        return MakeAddress(DataBank, pointer);
                    }
                case AddressingType.DirectPageIndirectIndexedY: // ORA (dp),Y
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = (ushort)(_bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr)) + Y);
                        return MakeAddress(DataBank, pointer);
                    }
                case AddressingType.DirectPageIndirectLongIndexedY: // ORA [dp],Y
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return _bus.ReadLong(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr)) + Y;
                    }
                case AddressingType.StackRelative: // ORA sr,S
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return MakeAddress(DIRECT_PAGE_BANK, (ushort)(Stack + addr));
                    }
                case AddressingType.StackRelativeIndirectIndexedY: // ORA (sr,S),Y
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = (ushort)(_bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(Stack + addr)) + Y);
                        return MakeAddress(DataBank, pointer);
                    }
            }

            throw new Exception("Unhandled addressing type " + addressingType);
        }

        private ushort ReadInstructionValueAndIncrementProgramCounter(AddressingType addressingType, bool is8Bit)
        {
            // TODO handle rollover properly
            // TODO reuse GetInstructionParameterAddressAndIncrementProgramCounter (must handle immediate separately)
            switch (addressingType)
            {
                case AddressingType.Immediate:
                    {
                        var value = is8Bit ? _bus.ReadByte(ProgramBank, ProgramCounter) : _bus.ReadShort(ProgramBank, ProgramCounter);
                        if (is8Bit)
                            ProgramCounter++;
                        else
                            ProgramCounter += 2;
                        return value;
                    }
                case AddressingType.Absolute:
                    {
                        var addr = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        return is8Bit ? _bus.ReadByte(DataBank, addr) : _bus.ReadShort(DataBank, addr);
                    }
                case AddressingType.AbsoluteLong:
                    {
                        var addr = _bus.ReadLong(ProgramBank, ProgramCounter);
                        ProgramCounter += 3;
                        return is8Bit ? _bus.ReadByte(addr) : _bus.ReadShort(addr);
                    }
                case AddressingType.DirectPage:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return is8Bit ? _bus.ReadByte(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr)) : _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr));
                    }
                case AddressingType.DirectPageIndirect:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr));
                        return is8Bit ? _bus.ReadByte(DataBank, pointer) : _bus.ReadShort(DataBank, pointer);
                    }
                case AddressingType.DirectPageIndirectLong:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = _bus.ReadLong(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr));
                        return is8Bit ? _bus.ReadByte(pointer) : _bus.ReadShort(pointer);
                    }
                case AddressingType.AbsoluteIndexedX:
                    {
                        var addr = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        return is8Bit ? _bus.ReadByte(DataBank, (ushort)(addr + X)) : _bus.ReadShort(DataBank, (ushort)(addr + X));
                    }
                case AddressingType.AbsoluteLongIndexedX:
                    {
                        var addr = _bus.ReadLong(ProgramBank, ProgramCounter);
                        ProgramCounter += 3;
                        return is8Bit ? _bus.ReadByte(addr + X) : _bus.ReadShort(addr + X);
                    }
                case AddressingType.AbsoluteIndexedY:
                    {
                        var addr = _bus.ReadShort(ProgramBank, ProgramCounter);
                        ProgramCounter += 2;
                        return is8Bit ? _bus.ReadByte(DataBank, (ushort)(addr + Y)) : _bus.ReadShort(DataBank, (ushort)(addr + Y));
                    }
                case AddressingType.DirectPageIndexedX:
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return is8Bit ? _bus.ReadByte(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr + X)) : _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr + X));
                    }
                case AddressingType.DirectPageIndexedIndirectX: //  ORA (dp,X) 
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr + X));
                        return is8Bit ? _bus.ReadByte(DataBank, pointer) : _bus.ReadShort(DataBank, pointer);
                    }
                case AddressingType.DirectPageIndirectIndexedY: // ORA (dp),Y
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = (ushort)(_bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr)) + Y);
                        return is8Bit ? _bus.ReadByte(DataBank, pointer) : _bus.ReadShort(DataBank, pointer);
                    }
                case AddressingType.DirectPageIndirectLongIndexedY: // ORA [dp],Y
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = _bus.ReadLong(DIRECT_PAGE_BANK, (ushort)(DirectPage + addr)) + Y;
                        return is8Bit ? _bus.ReadByte(pointer) : _bus.ReadShort(pointer);
                    }
                case AddressingType.StackRelative: // ORA sr,S
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        return is8Bit ? _bus.ReadByte(DIRECT_PAGE_BANK, (ushort)(Stack + addr)) : _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(Stack + addr));
                    }
                case AddressingType.StackRelativeIndirectIndexedY: // ORA (sr,S),Y
                    {
                        var addr = _bus.ReadByte(ProgramBank, ProgramCounter);
                        ProgramCounter += 1;
                        var pointer = (ushort)(_bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(Stack + addr)) + Y);
                        return is8Bit ? _bus.ReadByte(DataBank, pointer) : _bus.ReadShort(DataBank, pointer);
                    }
            }

            throw new Exception("Unhandled addressing type " + addressingType);
        }

        private void DoAccumulatorOperation(OperationType operation, ushort val)
        {
            switch (operation)
            {
                case OperationType.Or:
                    Accumulator |= val;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case OperationType.And:
                    if (Accumulator8bit)
                        SetAccumulatorLowByte((byte)(Accumulator & val));
                    else
                        Accumulator &= val;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case OperationType.ExclusiveOr:
                    if (Accumulator8bit)
                        SetAccumulatorLowByte((byte)(Accumulator ^ val));
                    else
                        Accumulator ^= val;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case OperationType.Add:
                    // TODO set overflow flag
                    if (Accumulator8bit)
                    {
                        var result = (Accumulator & 0xff) + val + (Carry ? 1 : 0);
                        Carry = (result & 0x100) != 0;
                        SetAccumulatorLowByte((byte)result);
                    }
                    else
                    {
                        var result = Accumulator + val + (Carry ? 1 : 0);
                        Carry = (result & 0x10000) != 0;
                        Accumulator = (ushort)result;
                    }
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case OperationType.Subtract:
                    // TODO set overflow flag
                    if (Accumulator8bit)
                    {
                        var result = ((Accumulator & 0xff) | (Carry ? 0x100 : 0)) - val;
                        Carry = (result & 0x100) != 0;
                        SetAccumulatorLowByte((byte)result);
                    }
                    else
                    {
                        var result = (Accumulator | (Carry ? 0x10000 : 0)) - val;
                        Carry = (result & 0x10000) != 0;
                        Accumulator = (ushort)result;
                    }
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case OperationType.Load:
                    if (Accumulator8bit)
                        SetAccumulatorLowByte((byte)val);
                    else
                        Accumulator = val;
                    SetZeroAndNegativeFlagsFromAccumulator();
                    break;
                case OperationType.Compare:
                    DoCompare(Accumulator, val, Accumulator8bit);
                    break;
                default:
                    throw new Exception("Unhandled operation: " + operation);
            }
        }

        private void DoCompare(ushort registerValue, ushort memoryValue, bool is8Bit)
        {
            if (Accumulator8bit)
            {
                Zero = (byte)memoryValue == (byte)registerValue;
                Carry = (byte)memoryValue <= (byte)registerValue;
                Negative = (((byte)registerValue - (byte)memoryValue) & 0x80) != 0;
            }
            else
            {
                Zero = memoryValue == registerValue;
                Carry = memoryValue <= registerValue;
                Negative = ((registerValue - memoryValue) & 0x8000) != 0;
            }
        }

        private void DoTestBits(ushort memoryValue)
        {
            if (Accumulator8bit)
            {
                Zero = ((byte)memoryValue & (byte)Accumulator) == 0;
                Negative = (memoryValue & 0x80) != 0;
                Overflow = (memoryValue & 0x40) != 0;
            }
            else
            {
                Zero = (memoryValue & Accumulator) == 0;
                Negative = (memoryValue & 0x8000) != 0;
                Overflow = (memoryValue & 0x4000) != 0;
            }
        }

        private void SetAccumulatorLowByte(byte val)
        {
            Accumulator = (ushort)((Accumulator & 0xff00) | val);
        }

        private void SetXLowByte(byte val)
        {
            X = (ushort)((X & 0xff00) | val);
        }

        private void SetYLowByte(byte val)
        {
            Y = (ushort)((Y & 0xff00) | val);
        }

        private void SetZeroAndNegativeFlagsFromValue(ushort val, bool is8bit)
        {
            if (is8bit)
            {
                Zero = ((byte)val) == 0;
                Negative = (val & 0x80) != 0;
            }
            else
            {
                Zero = val == 0;
                Negative = (val & 0x8000) != 0;
            }
        }

        private void SetZeroAndNegativeFlagsFromAccumulator()
        {
            SetZeroAndNegativeFlagsFromValue(Accumulator, Accumulator8bit);
        }

        private void PushByte(byte b)
        {
            _bus.WriteByte(DIRECT_PAGE_BANK, Stack, b);
            Stack--;
        }

        private void PushShort(ushort s)
        {
            _bus.WriteShort(DIRECT_PAGE_BANK, (ushort)(Stack - 1), s);
            Stack -= 2;
        }

        private byte PullByte()
        {
            // TODO rollover
            Stack++;
            return _bus.ReadByte(DIRECT_PAGE_BANK, Stack);
        }

        private ushort PullShort()
        {
            // TODO rollover
            Stack += 2;
            return _bus.ReadShort(DIRECT_PAGE_BANK, (ushort)(Stack - 1));
        }

        #region Debug
        private Dictionary<int, RemeSnes.Breakpoint> _breakpoints = new Dictionary<int, RemeSnes.Breakpoint>();

        internal void SetBreakpoint(Breakpoint bp)
        {
            _breakpoints[bp.Address] = bp;
        }
        #endregion

        private enum OperationType
        {
            Or,
            And,
            ExclusiveOr,
            Add,
            Subtract,
            Load,
            Store,
            Compare,
            ShiftLeft,
            ShiftRight,
            RotateLeft,
            RotateRight,
        }

        private enum AddressingType
        {
            Immediate,
            Absolute,
            AbsoluteIndexedIndirect,
            AbsoluteIndexedX,
            AbsoluteIndexedY,
            AbsoluteLong,
            AbsoluteLongIndexedX,
            StackRelative,
            StackRelativeIndirectIndexedY,
            Indirect,
            IndirectLong,
            DirectPage,
            DirectPageIndexedX,
            DirectPageIndexedY,
            DirectPageIndexedIndirectX,
            DirectPageIndirect,
            DirectPageIndirectLong,
            DirectPageIndirectIndexedY,
            DirectPageIndirectLongIndexedY,
        }
    }
}
