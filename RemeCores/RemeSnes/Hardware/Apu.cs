using System.Security.Cryptography;
using System;
using Utils;

namespace RemeSnes.Hardware
{
    /// <summary>
    /// Represents the combination of the SPC700 CPU, the S-DSP, and the audio PSRAM.
    /// </summary>
    internal class Apu
    {
        public byte[] Psram = new byte[0x10000]; // Holds code and data for the SPC700

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

        private int DirectPageOffset(byte offset) { return offset + (DirectPage ? 0x100 : 0); }
        private int XOffset { get { return X + (DirectPage ? 0x100 : 0); } }
        private int YOffset { get { return Y + (DirectPage ? 0x100 : 0); } }

        /**
           $0000 - $00EF - direct page 0
           $00F0 - $00FF - memory-mapped hardware registers
           $0100 - $01FF - direct page 1
           $0100 - $01FF - potential stack memory
           $0200 - $FFBF - memory
           $FFC0 - $FFFF - If "X bit is set in the undocumented register": Read: IPL ROM Write: memory. Else read/write memory.
        */

        /**
         * Memory mapped registers:
            F0	Undocumented	?/W
            F1	Control Register	/W
            F2	DSP Register Address	R/W
            F3	DSP Register Data	R/W
            F4	Port 0	R/W
            F5	Port 1	R/W
            F6	Port 2	R/W
            F7	Port 3	R/W
            F8	Regular Memory	R/W
            F9	Regular Memory	R/W
            FA	Timer-0	/W
            FB	Timer-1	/W
            FC	Timer-2	/W
            FD	Counter-0	R/
            FE	Counter-1	R/
            FF	Counter-2	R/
        */

        // TODO add functions for addressing modes so each instruction can be short
        public void RunOneInstruction()
        {
            var opcode = Psram[ProgramCounter++];

            switch (opcode)
            {
                case 0: // NOP
                    break;
                case 0x0f: // BRK
                    PushShort(ProgramCounter);
                    PushByte(ProgramStatus);
                    ProgramCounter = Psram.ReadShort(0xffde);
                    break;
                case 0xef: // SLEEP
                    throw new Exception("Sleep encountered");
                    break;
                case 0xff: // STOP
                    throw new Exception("Stop encountered");
                    break;

                // ADC
                case 0x99:
                    Psram[XOffset] = DoAdd(Psram[XOffset], Psram[YOffset]);
                    break;
                case 0x88:
                    Accumulator = DoAdd(Accumulator, Psram[ProgramCounter++]);
                    break;
                case 0x86:
                    Accumulator = DoAdd(Accumulator, Psram[XOffset]);
                    break;
                case 0x97:
                    Accumulator = DoAdd(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y]);
                    break;
                case 0x87:
                    Accumulator = DoAdd(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)]);
                    break;
                case 0x84:
                    Accumulator = DoAdd(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x94:
                    Accumulator = DoAdd(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0x85:
                    Accumulator = DoAdd(Accumulator, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x95:
                    Accumulator = DoAdd(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + X]);
                    ProgramCounter += 2;
                    break;
                case 0x96:
                    Accumulator = DoAdd(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + Y]);
                    ProgramCounter += 2;
                    break;
                case 0x89:
                    {
                        var dp1 = DirectPageOffset(Psram[ProgramCounter++]);
                        var dp2 = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dp2] = DoAdd(Psram[dp2], Psram[dp1]);
                    }
                    break;
                case 0x98:
                    {
                        var immediate = Psram[ProgramCounter++];
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpOffset] = DoAdd(Psram[dpOffset], immediate);
                    }
                    break;
                case 0x7a: // ADDW
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        var word = Accumulator | (Y << 8);
                        var word2 = Psram.ReadShort(dpOffset);
                        var result = word + word2 + (Carry ? 1 : 0);
                        Carry = result > 0xffff;
                        Negative = (result & 0x8000) != 0;
                        Zero = (result & 0xffff) == 0;
                        Overflow = (~(word ^ word2) & (word2 ^ result) & 0x8000) != 0;
                        HalfCarry = ((word >> 8) & 0x0F) + ((word2 >> 8) & 0x0F) + ((((word2 & 0xff) + (word & 0xff)) > 0xff) ? 1 : 0) > 0x0F;
                        Accumulator = (byte)result;
                        Y = (byte)(result >> 8);
                    }
                    break;

                // SBC
                case 0xb9:
                    Psram[XOffset] = DoSubtract(Psram[XOffset], Psram[YOffset]);
                    break;
                case 0xa8:
                    Accumulator = DoSubtract(Accumulator, Psram[ProgramCounter++]);
                    break;
                case 0xa6:
                    Accumulator = DoSubtract(Accumulator, Psram[XOffset]);
                    break;
                case 0xb7:
                    Accumulator = DoSubtract(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y]);
                    break;
                case 0xa7:
                    Accumulator = DoSubtract(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)]);
                    break;
                case 0xa4:
                    Accumulator = DoSubtract(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0xb4:
                    Accumulator = DoSubtract(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0xa5:
                    Accumulator = DoSubtract(Accumulator, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0xb5:
                    Accumulator = DoSubtract(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + X]);
                    ProgramCounter += 2;
                    break;
                case 0xb6:
                    Accumulator = DoSubtract(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + Y]);
                    ProgramCounter += 2;
                    break;
                case 0xa9:
                    {
                        var dp1 = DirectPageOffset(Psram[ProgramCounter++]);
                        var dp2 = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dp2] = DoSubtract(Psram[dp2], Psram[dp1]);
                    }
                    break;
                case 0xb8:
                    {
                        var immediate = Psram[ProgramCounter++];
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpOffset] = DoSubtract(Psram[dpOffset], immediate);
                    }
                    break;
                case 0x9a: // SUBW
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        var word = Accumulator | (Y << 8);
                        var word2 = Psram.ReadShort(dpOffset);
                        var result = word - word2 - (Carry ? 0 : 1);
                        Carry = word >= word2;
                        Negative = (result & 0x8000) != 0;
                        Zero = (result & 0xffff) == 0;
                        Overflow = ((word ^ word2) & (word ^ (ushort)result) & 0x8000) != 0;
                        HalfCarry = (((word >> 8) & 0x0F) - ((word2 >> 8) & 0x0F) - (((byte)word2 > (byte)word) ? 1 : 0)) <= 0x0F;
                        Accumulator = (byte)result;
                        Y = (byte)(result >> 8);
                    }
                    break;

                // CMP
                case 0x79:
                    DoCompare(Psram[XOffset], Psram[YOffset]);
                    break;
                case 0x68:
                    DoCompare(Accumulator, Psram[ProgramCounter++]);
                    break;
                case 0x66:
                    DoCompare(Accumulator, Psram[XOffset]);
                    break;
                case 0x77:
                    DoCompare(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y]);
                    break;
                case 0x67:
                    DoCompare(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)]);
                    break;
                case 0x64:
                    DoCompare(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x74:
                    DoCompare(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0x65:
                    DoCompare(Accumulator, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x75:
                    DoCompare(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + X]);
                    ProgramCounter += 2;
                    break;
                case 0x76:
                    DoCompare(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + Y]);
                    ProgramCounter += 2;
                    break;
                case 0xc8:
                    DoCompare(X, Psram[ProgramCounter++]);
                    break;
                case 0x3e:
                    DoCompare(X, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x1e:
                    DoCompare(X, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0xad:
                    DoCompare(Y, Psram[ProgramCounter++]);
                    break;
                case 0x7e:
                    DoCompare(Y, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x5e:
                    DoCompare(Y, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x69:
                    {
                        var dp1 = DirectPageOffset(Psram[ProgramCounter++]);
                        var dp2 = DirectPageOffset(Psram[ProgramCounter++]);
                        DoCompare(Psram[dp2], Psram[dp1]);
                    }
                    break;
                case 0x78:
                    {
                        var immediate = Psram[ProgramCounter++];
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoCompare(Psram[dpOffset], immediate);
                    }
                    break;
                case 0x5a: // CMPW
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        var word = Accumulator | (Y << 8);
                        var word2 = Psram.ReadShort(dpOffset);
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
                    DoIncrement(ref Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0xbb:
                    DoIncrement(ref Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0xac:
                    DoIncrement(ref Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x3a: // INCW
                    DoIncrementWord(DirectPageOffset(Psram[ProgramCounter++]));
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
                    DoDecrement(ref Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x9b:
                    DoDecrement(ref Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0x8c:
                    DoDecrement(ref Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x1a: // DECW
                    DoDecrementWord(DirectPageOffset(Psram[ProgramCounter++]));
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
                        var word = Accumulator | (Y << 8);
                        var result = word / X;
                        Accumulator = (byte)result;
                        Y = (byte)(word % X);
                        SetNegativeAndZero(Accumulator);
                        Overflow = result > 0xff;
                    }
                    break;

                // AND
                case 0x39:
                    Psram[XOffset] = DoAnd(Psram[XOffset], Psram[YOffset]);
                    break;
                case 0x28:
                    Accumulator = DoAnd(Accumulator, Psram[ProgramCounter++]);
                    break;
                case 0x26:
                    Accumulator = DoAnd(Accumulator, Psram[XOffset]);
                    break;
                case 0x37:
                    Accumulator = DoAnd(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y]);
                    break;
                case 0x27:
                    Accumulator = DoAnd(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)]);
                    break;
                case 0x24:
                    Accumulator = DoAnd(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x34:
                    Accumulator = DoAnd(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0x25:
                    Accumulator = DoAnd(Accumulator, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x35:
                    Accumulator = DoAnd(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + X]);
                    ProgramCounter += 2;
                    break;
                case 0x36:
                    Accumulator = DoAnd(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + Y]);
                    ProgramCounter += 2;
                    break;
                case 0x29:
                    {
                        var dp1 = DirectPageOffset(Psram[ProgramCounter++]);
                        var dp2 = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dp2] = DoAnd(Psram[dp2], Psram[dp1]);
                    }
                    break;
                case 0x38:
                    {
                        var immediate = Psram[ProgramCounter++];
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpOffset] = DoAnd(Psram[dpOffset], immediate);
                    }
                    break;
                // AND1
                case 0x6a:
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Carry &= !GetBit(bitAddress);
                    }
                    break;
                case 0x4a:
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Carry &= GetBit(bitAddress);
                    }
                    break;
                // OR
                case 0x19:
                    Psram[XOffset] = DoOr(Psram[XOffset], Psram[YOffset]);
                    break;
                case 0x08:
                    Accumulator = DoOr(Accumulator, Psram[ProgramCounter++]);
                    break;
                case 0x06:
                    Accumulator = DoOr(Accumulator, Psram[XOffset]);
                    break;
                case 0x17:
                    Accumulator = DoOr(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y]);
                    break;
                case 0x07:
                    Accumulator = DoOr(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)]);
                    break;
                case 0x04:
                    Accumulator = DoOr(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x14:
                    Accumulator = DoOr(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0x05:
                    Accumulator = DoOr(Accumulator, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x15:
                    Accumulator = DoOr(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + X]);
                    ProgramCounter += 2;
                    break;
                case 0x16:
                    Accumulator = DoOr(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + Y]);
                    ProgramCounter += 2;
                    break;
                case 0x09:
                    {
                        var dp1 = DirectPageOffset(Psram[ProgramCounter++]);
                        var dp2 = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dp2] = DoOr(Psram[dp2], Psram[dp1]);
                    }
                    break;
                case 0x18:
                    {
                        var immediate = Psram[ProgramCounter++];
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpOffset] = DoOr(Psram[dpOffset], immediate);
                    }
                    break;
                // OR1
                case 0x2a:
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Carry |= !GetBit(bitAddress);
                    }
                    break;
                case 0x0a:
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Carry |= GetBit(bitAddress);
                    }
                    break;
                // EOR
                case 0x59:
                    Psram[XOffset] = DoExclusiveOr(Psram[XOffset], Psram[YOffset]);
                    break;
                case 0x48:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[ProgramCounter++]);
                    break;
                case 0x46:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[XOffset]);
                    break;
                case 0x57:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y]);
                    break;
                case 0x47:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)]);
                    break;
                case 0x44:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    break;
                case 0x54:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    break;
                case 0x45:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[Psram.ReadShort(ProgramCounter)]);
                    ProgramCounter += 2;
                    break;
                case 0x55:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + X]);
                    ProgramCounter += 2;
                    break;
                case 0x56:
                    Accumulator = DoExclusiveOr(Accumulator, Psram[Psram.ReadShort(ProgramCounter) + Y]);
                    ProgramCounter += 2;
                    break;
                case 0x49:
                    {
                        var dp1 = DirectPageOffset(Psram[ProgramCounter++]);
                        var dp2 = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dp2] = DoExclusiveOr(Psram[dp2], Psram[dp1]);
                    }
                    break;
                case 0x58:
                    {
                        var immediate = Psram[ProgramCounter++];
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpOffset] = DoExclusiveOr(Psram[dpOffset], immediate);
                    }
                    break;
                case 0x8a: // EOR1
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Carry ^= GetBit(bitAddress);
                    }
                    break;
                case 0xea: // NOT1
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        // Flip the specified bit
                        Psram[bitAddress & 0x1fff] ^= (byte)(1 << (bitAddress >> 13));
                    }
                    break;
                // ASL
                case 0x1c:
                    DoLeftShift(ref Accumulator);
                    break;
                case 0x0b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoLeftShift(ref Psram[dpOffset]);
                    }
                    break;
                case 0x1b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoLeftShift(ref Psram[dpOffset + X]);
                    }
                    break;
                case 0x0c:
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        DoLeftShift(ref Psram[address]);
                    }
                    break;
                // LSR
                case 0x5c:
                    DoRightShift(ref Accumulator);
                    break;
                case 0x4b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoRightShift(ref Psram[dpOffset]);
                    }
                    break;
                case 0x5b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoRightShift(ref Psram[dpOffset + X]);
                    }
                    break;
                case 0x4c:
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        DoRightShift(ref Psram[address]);
                    }
                    break;
                // ROL
                case 0x3c:
                    DoRotateLeft(ref Accumulator);
                    break;
                case 0x2b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoRotateLeft(ref Psram[dpOffset]);
                    }
                    break;
                case 0x3b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoRotateLeft(ref Psram[dpOffset + X]);
                    }
                    break;
                case 0x2c:
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        DoRotateLeft(ref Psram[address]);
                    }
                    break;
                // ROR
                case 0x7c:
                    DoRotateRight(ref Accumulator);
                    break;
                case 0x6b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoRotateRight(ref Psram[dpOffset]);
                    }
                    break;
                case 0x7b:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        DoRotateRight(ref Psram[dpOffset + X]);
                    }
                    break;
                case 0x6c:
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        DoRotateRight(ref Psram[address]);
                    }
                    break;

                case 0x4e: // TCLR1
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Psram[address] &= (byte)~Accumulator;
                    }
                    break;
                case 0x0e: // TSET1
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        Psram[address] |= Accumulator;
                    }
                    break;

                // TODO
                case 0xdf: // DAA
                    break;
                case 0xbe: // DAS
                    break;

                // MOV
                case 0xaf:
                    Psram[XOffset] = Accumulator;
                    X++;
                    break;
                case 0xc6:
                    Psram[XOffset] = Accumulator;
                    break;
                case 0xd7:
                    Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y] = Accumulator;
                    break;
                case 0xc7:
                    Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)] = Accumulator;
                    break;
                case 0xe8:
                    Accumulator = Psram[ProgramCounter++];
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe6:
                    Accumulator = Psram[XOffset];
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xbf:
                    Accumulator = Psram[XOffset];
                    SetNegativeAndZero(Accumulator);
                    X++;
                    break;
                case 0xf7:
                    Accumulator = Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++])) + Y];
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe7:
                    Accumulator = Psram[Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]) + X)];
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
                    Accumulator = Psram[DirectPageOffset(Psram[ProgramCounter++])];
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xf4:
                    Accumulator = Psram[DirectPageOffset(Psram[ProgramCounter++]) + X];
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xe5:
                    Accumulator = Psram[Psram.ReadShort(ProgramCounter)];
                    ProgramCounter += 2;
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xf5:
                    Accumulator = Psram[Psram.ReadShort(ProgramCounter) + X];
                    ProgramCounter += 2;
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xf6:
                    Accumulator = Psram[Psram.ReadShort(ProgramCounter) + Y];
                    ProgramCounter += 2;
                    SetNegativeAndZero(Accumulator);
                    break;
                case 0xbd:
                    Stack = X;
                    break;
                case 0xcd:
                    X = Psram[ProgramCounter++];
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
                    X = Psram[DirectPageOffset(Psram[ProgramCounter++])];
                    SetNegativeAndZero(X);
                    break;
                case 0xf9:
                    X = Psram[DirectPageOffset(Psram[ProgramCounter++]) + Y];
                    SetNegativeAndZero(X);
                    break;
                case 0xe9:
                    X = Psram[Psram.ReadShort(ProgramCounter)];
                    SetNegativeAndZero(X);
                    break;
                case 0x8d:
                    Y = Psram[ProgramCounter++];
                    SetNegativeAndZero(Y);
                    break;
                case 0xfd:
                    Y = Accumulator;
                    SetNegativeAndZero(Y);
                    break;
                case 0xeb:
                    Y = Psram[DirectPageOffset(Psram[ProgramCounter++])];
                    SetNegativeAndZero(Y);
                    break;
                case 0xfb:
                    Y = Psram[DirectPageOffset(Psram[ProgramCounter++]) + X];
                    SetNegativeAndZero(Y);
                    break;
                case 0xec:
                    Y = Psram[Psram.ReadShort(ProgramCounter)];
                    SetNegativeAndZero(Y);
                    break;
                case 0xfa:
                    {
                        var dpSource = DirectPageOffset(Psram[ProgramCounter++]);
                        var dpDest = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpDest] = Psram[dpSource];
                    }
                    break;
                case 0xd4:
                    Psram[DirectPageOffset(Psram[ProgramCounter++]) + X] = Accumulator;
                    break;
                case 0xdb:
                    Psram[DirectPageOffset(Psram[ProgramCounter++]) + X] = Y;
                    break;
                case 0xd9:
                    Psram[DirectPageOffset(Psram[ProgramCounter++]) + Y] = X;
                    break;
                case 0x8f:
                    {
                        var immediate = Psram[ProgramCounter++];
                        Psram[DirectPageOffset(Psram[ProgramCounter++])] = immediate;
                    }
                    break;
                case 0xc4:
                    Psram[DirectPageOffset(Psram[ProgramCounter++])] = Accumulator;
                    break;
                case 0xd8:
                    Psram[DirectPageOffset(Psram[ProgramCounter++])] = Y;
                    break;
                case 0xcb:
                    Psram[DirectPageOffset(Psram[ProgramCounter++])] = X;
                    break;
                case 0xd5:
                    Psram[Psram.ReadShort(ProgramCounter) + X] = Accumulator;
                    ProgramCounter += 2;
                    break;
                case 0xd6:
                    Psram[Psram.ReadShort(ProgramCounter) + Y] = Accumulator;
                    ProgramCounter += 2;
                    break;
                case 0xc5:
                    Psram[Psram.ReadShort(ProgramCounter)] = Accumulator;
                    ProgramCounter += 2;
                    break;
                case 0xc9:
                    Psram[Psram.ReadShort(ProgramCounter)] = X;
                    ProgramCounter += 2;
                    break;
                case 0xcc:
                    Psram[Psram.ReadShort(ProgramCounter)] = Y;
                    ProgramCounter += 2;
                    break;
                // MOV1
                case 0xaa:
                    Carry = GetBit(Psram.ReadShort(ProgramCounter));
                    ProgramCounter += 2;
                    break;
                case 0xca:
                    {
                        var bitAddress = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        if (Carry)
                            SetBit(bitAddress & 0x1fff, bitAddress >> 13);
                        else
                            ClearBit(bitAddress & 0x1fff, bitAddress >> 13);
                    }
                    break;
                // MOVW
                case 0xba:
                    {
                        var word = Psram.ReadShort(DirectPageOffset(Psram[ProgramCounter++]));
                        Accumulator = (byte)word;
                        Y = (byte)(word >> 8);
                        SetNegativeAndZero(Accumulator); // is this correct?
                    }
                    break;
                case 0xda:
                    Psram.WriteShort(DirectPageOffset(Psram[ProgramCounter++]), (ushort)(Accumulator | (Y << 8)));
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
                    DoCompare(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++]) + X]);
                    DoBranchInstruction(!Zero);
                    break;
                case 0x2e:
                    DoCompare(Accumulator, Psram[DirectPageOffset(Psram[ProgramCounter++])]);
                    DoBranchInstruction(!Zero);
                    break;
                // DBNZ
                case 0xfe:
                    Y--;
                    DoBranchInstruction(Y != 0);
                    break;
                case 0x6e:
                    {
                        var dpOffset = DirectPageOffset(Psram[ProgramCounter++]);
                        Psram[dpOffset]--;
                        DoBranchInstruction(Psram[dpOffset] != 0);
                    }
                    break;
                case 0x3f: // CALL
                    {
                        var address = Psram.ReadShort(ProgramCounter);
                        ProgramCounter += 2;
                        DoCall(address);
                    }
                    break;
                case 0x4f: // PCALL
                    {
                        var address = (ushort)(Psram[ProgramCounter++] + 0xff00);
                        DoCall(address);
                    }
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
                    ProgramCounter = Psram.ReadShort(Psram.ReadShort(ProgramCounter) + X);
                    break;
                case 0x5f:
                    ProgramCounter = Psram.ReadShort(ProgramCounter);
                    break;

                // CLR1
                case 0x12:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 0);
                    break;
                case 0x32:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 1);
                    break;
                case 0x52:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 2);
                    break;
                case 0x72:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 3);
                    break;
                case 0x92:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 4);
                    break;
                case 0xb2:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 5);
                    break;
                case 0xd2:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 6);
                    break;
                case 0xf2:
                    ClearBit(DirectPageOffset(Psram[ProgramCounter++]), 7);
                    break;
                // SET1
                case 0x02:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 0);
                    break;
                case 0x22:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 1);
                    break;
                case 0x42:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 2);
                    break;
                case 0x62:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 3);
                    break;
                case 0x82:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 4);
                    break;
                case 0xa2:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 5);
                    break;
                case 0xc2:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 6);
                    break;
                case 0xe2:
                    SetBit(DirectPageOffset(Psram[ProgramCounter++]), 7);
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
                    Accumulator = (byte)((Accumulator >> 4) | (Accumulator << 4));
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
            DoCall(Psram.ReadShort(lookupAddress));
        }

        /// <summary>
        /// Argument is 13 bit address, with 3 high bits addressing the bit within the addressed byte.
        /// </summary>
        private bool GetBit(ushort bitAddress)
        {
            return ((Psram[bitAddress & 0x1fff] >> (bitAddress >> 13)) & 1) != 0;
        }

        private static bool GetBit(byte b, int bit)
        {
            return ((b >> bit) & 1) != 0;
        }

        private void ClearBit(int address, int bit)
        {
            Psram[address] &= (byte)(~(1 << bit));
        }

        private void SetBit(int address, int bit)
        {
            Psram[address] |= (byte)(1 << bit);
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
            return (ushort)(low | (PullByte() << 8));
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

        private void DoIncrementWord(int address)
        {
            var val = Psram.ReadShort(address);
            val++;
            Zero = val == 0;
            Negative = (val & 0x8000) != 0;
            Psram.WriteShort(address, val);
        }

        private void DoDecrement(ref byte a)
        {
            a--;
            SetNegativeAndZero(a);
        }

        private void DoDecrementWord(int address)
        {
            var val = Psram.ReadShort(address);
            val--;
            Zero = val == 0;
            Negative = (val & 0x8000) != 0;
            Psram.WriteShort(address, val);
        }

        private byte DoAdd(byte a, byte b)
        {
            var result = a + b + (Carry ? 1 : 0);
            Carry = result > 0xff;
            Overflow = ((~(a ^ b)) & (a ^ result) & 0x80) != 0; // Don't understand this, found this code here: https://github.com/mamedev/mame/blob/master/src/devices/cpu/spc700/spc700.cpp
            HalfCarry = (((result & 0x0f) - ((a & 0xf) + (Carry ? 1 : 0))) & 0x10) != 0; // Same as above
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

        private void DoRightShift(ref byte b)
        {
            Carry = (b & 0x1) != 0;
            b >>= 1;
        }

        private void DoRotateLeft(ref byte b)
        {
            var carryBefore = Carry;
            Carry = (b & 0x1) != 0;
            b <<= 1;
            if (carryBefore)
                b |= 1;
        }

        private void DoRotateRight(ref byte b)
        {
            var carryBefore = Carry;
            Carry = (b & 0x1) != 0;
            b >>= 1;
            if (carryBefore)
                b |= 0x80;
        }

        private void DoDirectPageBranchOnBit(int bit, bool branchIfSet)
        {
            var dpOffset = Psram[ProgramCounter++];
            DoBranchInstruction(GetBit(Psram[dpOffset], bit) == branchIfSet);
        }

        private void DoBranchInstruction(bool condition)
        {
            var branchOffset = (sbyte)Psram[ProgramCounter++];
            if (condition)
                ProgramCounter = (ushort)(ProgramCounter + branchOffset);
        }
    }
}
