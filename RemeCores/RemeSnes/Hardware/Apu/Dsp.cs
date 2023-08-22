using Utils;

namespace RemeSnes.Hardware.Audio
{
    // TODO noise
    // TODO get new samples at 32000 Hz
    internal class Dsp
    {
        // How does the DSP know what time it is? Does it have an internal clock?
        // Who does the mixing of all the voices into the final output samples?
        // Outputs samples at 32KHz, or one sample every 32 SPC700 cycles.

        // Apparently this has its own clock of nominal 24576000 Hz (24.576 MHz)

        // https://problemkaputt.de/fullsnes.htm

        // For samples: Each voice:
        // 1. Decompress BRR sample
        // 2. Apply pitch modulation and pitch scale
            // Pitch affects which samples are grabbed/how quickly
            // output is always 32kHz, so skipping samples will result in higher pitch, and reusing samples will result in a lower pitch
        // 3. Apply ADSR/Gain (env value goes to ENVx, mixed goes to OUTx)
        // 4. Apply volume left/right

        // Then: for each L/R
        // 1. Add all voices together
        // 2. Apply master volume
        // 3. Add Echo

        // For Echo:
        // 1. Add all voices together (what's the source data for this?)
        // 2. lots of stuff...

        private readonly Voice[] Voices = new Voice[8];
        private readonly byte[] FilterCoefficients = new byte[8];
        private readonly byte[] OutputSamplesLeft = new byte[1200];
        private readonly byte[] OutputSamplesRight = new byte[1200];
        private int _outputSampleIndex;

        // Size 0x200
        private readonly ushort[] Gauss = new ushort[] {
        0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,0x000,
        0x001,0x001,0x001,0x001,0x001,0x001,0x001,0x001,0x001,0x001,0x001,0x002,0x002,0x002,0x002,0x002,
        0x002,0x002,0x003,0x003,0x003,0x003,0x003,0x004,0x004,0x004,0x004,0x004,0x005,0x005,0x005,0x005,
        0x006,0x006,0x006,0x006,0x007,0x007,0x007,0x008,0x008,0x008,0x009,0x009,0x009,0x00A,0x00A,0x00A,
        0x00B,0x00B,0x00B,0x00C,0x00C,0x00D,0x00D,0x00E,0x00E,0x00F,0x00F,0x00F,0x010,0x010,0x011,0x011,
        0x012,0x013,0x013,0x014,0x014,0x015,0x015,0x016,0x017,0x017,0x018,0x018,0x019,0x01A,0x01B,0x01B,
        0x01C,0x01D,0x01D,0x01E,0x01F,0x020,0x020,0x021,0x022,0x023,0x024,0x024,0x025,0x026,0x027,0x028,
        0x029,0x02A,0x02B,0x02C,0x02D,0x02E,0x02F,0x030,0x031,0x032,0x033,0x034,0x035,0x036,0x037,0x038,
        0x03A,0x03B,0x03C,0x03D,0x03E,0x040,0x041,0x042,0x043,0x045,0x046,0x047,0x049,0x04A,0x04C,0x04D,
        0x04E,0x050,0x051,0x053,0x054,0x056,0x057,0x059,0x05A,0x05C,0x05E,0x05F,0x061,0x063,0x064,0x066,
        0x068,0x06A,0x06B,0x06D,0x06F,0x071,0x073,0x075,0x076,0x078,0x07A,0x07C,0x07E,0x080,0x082,0x084,
        0x086,0x089,0x08B,0x08D,0x08F,0x091,0x093,0x096,0x098,0x09A,0x09C,0x09F,0x0A1,0x0A3,0x0A6,0x0A8,
        0x0AB,0x0AD,0x0AF,0x0B2,0x0B4,0x0B7,0x0BA,0x0BC,0x0BF,0x0C1,0x0C4,0x0C7,0x0C9,0x0CC,0x0CF,0x0D2,
        0x0D4,0x0D7,0x0DA,0x0DD,0x0E0,0x0E3,0x0E6,0x0E9,0x0EC,0x0EF,0x0F2,0x0F5,0x0F8,0x0FB,0x0FE,0x101,
        0x104,0x107,0x10B,0x10E,0x111,0x114,0x118,0x11B,0x11E,0x122,0x125,0x129,0x12C,0x130,0x133,0x137,
        0x13A,0x13E,0x141,0x145,0x148,0x14C,0x150,0x153,0x157,0x15B,0x15F,0x162,0x166,0x16A,0x16E,0x172,
        0x176,0x17A,0x17D,0x181,0x185,0x189,0x18D,0x191,0x195,0x19A,0x19E,0x1A2,0x1A6,0x1AA,0x1AE,0x1B2,
        0x1B7,0x1BB,0x1BF,0x1C3,0x1C8,0x1CC,0x1D0,0x1D5,0x1D9,0x1DD,0x1E2,0x1E6,0x1EB,0x1EF,0x1F3,0x1F8,
        0x1FC,0x201,0x205,0x20A,0x20F,0x213,0x218,0x21C,0x221,0x226,0x22A,0x22F,0x233,0x238,0x23D,0x241,
        0x246,0x24B,0x250,0x254,0x259,0x25E,0x263,0x267,0x26C,0x271,0x276,0x27B,0x280,0x284,0x289,0x28E,
        0x293,0x298,0x29D,0x2A2,0x2A6,0x2AB,0x2B0,0x2B5,0x2BA,0x2BF,0x2C4,0x2C9,0x2CE,0x2D3,0x2D8,0x2DC,
        0x2E1,0x2E6,0x2EB,0x2F0,0x2F5,0x2FA,0x2FF,0x304,0x309,0x30E,0x313,0x318,0x31D,0x322,0x326,0x32B,
        0x330,0x335,0x33A,0x33F,0x344,0x349,0x34E,0x353,0x357,0x35C,0x361,0x366,0x36B,0x370,0x374,0x379,
        0x37E,0x383,0x388,0x38C,0x391,0x396,0x39B,0x39F,0x3A4,0x3A9,0x3AD,0x3B2,0x3B7,0x3BB,0x3C0,0x3C5,
        0x3C9,0x3CE,0x3D2,0x3D7,0x3DC,0x3E0,0x3E5,0x3E9,0x3ED,0x3F2,0x3F6,0x3FB,0x3FF,0x403,0x408,0x40C,
        0x410,0x415,0x419,0x41D,0x421,0x425,0x42A,0x42E,0x432,0x436,0x43A,0x43E,0x442,0x446,0x44A,0x44E,
        0x452,0x455,0x459,0x45D,0x461,0x465,0x468,0x46C,0x470,0x473,0x477,0x47A,0x47E,0x481,0x485,0x488,
        0x48C,0x48F,0x492,0x496,0x499,0x49C,0x49F,0x4A2,0x4A6,0x4A9,0x4AC,0x4AF,0x4B2,0x4B5,0x4B7,0x4BA,
        0x4BD,0x4C0,0x4C3,0x4C5,0x4C8,0x4CB,0x4CD,0x4D0,0x4D2,0x4D5,0x4D7,0x4D9,0x4DC,0x4DE,0x4E0,0x4E3,
        0x4E5,0x4E7,0x4E9,0x4EB,0x4ED,0x4EF,0x4F1,0x4F3,0x4F5,0x4F6,0x4F8,0x4FA,0x4FB,0x4FD,0x4FF,0x500,
        0x502,0x503,0x504,0x506,0x507,0x508,0x50A,0x50B,0x50C,0x50D,0x50E,0x50F,0x510,0x511,0x511,0x512,
        0x513,0x514,0x514,0x515,0x516,0x516,0x517,0x517,0x517,0x518,0x518,0x518,0x518,0x518,0x519,0x519 };

        private sbyte MainVolumeLeft;
        private sbyte MainVolumeRight;
        private sbyte EchoVolumeLeft;
        private sbyte EchoVolumeRight;
        private byte DspFlags;
        private byte EndX;
        private byte EchoFeedback;
        private byte PitchModulation;
        private byte NoiseEnable;
        private byte EchoEnable;
        private byte SourceDirectoryOffset;
        private byte EchoBufferStartOffset;
        private byte EchoDelay;

        public Dsp()
        {
            for (int i = 0; i < Voices.Length; i++)
            {
                Voices[i].CurrentBrrBlock = new short[16];
            }
        }

        private bool Mute { get { return (DspFlags & 0x40) != 0; } }

        private ReadOnlyMemory<byte> _psram;

        internal void SetRam(ReadOnlyMemory<byte> psram)
        {
            _psram = psram;
        }

        public byte Read(byte address)
        {
            address &= 0x7f;

            if ((address & 0xf) < 0xc)
            {
                var voiceIndex = address >> 4;
                return (address & 0xf) switch
                {
                    0 => (byte)Voices[voiceIndex].VolumeLeft,
                    1 => (byte)Voices[voiceIndex].VolumeRight,
                    2 => (byte)Voices[voiceIndex].Pitch,
                    3 => (byte)(Voices[voiceIndex].Pitch >> 8),
                    4 => Voices[voiceIndex].SourceNumber,
                    5 => Voices[voiceIndex].Adsr1,
                    6 => Voices[voiceIndex].Adsr2,
                    7 => Voices[voiceIndex].Gain,
                    8 => Voices[voiceIndex].EnvX,
                    9 => (byte)Voices[voiceIndex].OutX,
                    0xa => Voices[voiceIndex].Unused1,
                    0xb => Voices[voiceIndex].Unused2,
                    _ => throw new Exception("Unsupported voice register " + address),
                };
            }
            if ((address & 0xf) == 0xf)
                return FilterCoefficients[address >> 4];

            return address switch
            {
                0x0c => (byte)MainVolumeLeft,
                0x1c => (byte)MainVolumeRight,
                0x2c => (byte)EchoVolumeLeft,
                0x3c => (byte)EchoVolumeRight,
                0x4c => 0,// KeyOn. No read?
                0x5c => 0,// KeyOff. No read?
                0x6c => DspFlags,
                0x7c => EndX,
                0x0d => EchoFeedback,
                0x2d => PitchModulation,
                0x3d => NoiseEnable,
                0x4d => EchoEnable,
                0x5d => SourceDirectoryOffset,
                0x6d => EchoBufferStartOffset,
                0x7d => EchoDelay,
                _ => 0,
            };
        }

        public void Write(byte address, byte b)
        {
            if (address > 0x7f)
                return;

            if ((address & 0xf) < 0xa)
            {
                var voiceIndex = address >> 4;
                switch (address & 0xf)
                {
                    case 0:
                        Voices[voiceIndex].VolumeLeft = (sbyte)b;
                        break;
                    case 1:
                        Voices[voiceIndex].VolumeRight = (sbyte)b;
                        break;
                    case 2:
                        Voices[voiceIndex].Pitch = (ushort)((Voices[voiceIndex].Pitch & 0xff00) | b);
                        break;
                    case 3:
                        Voices[voiceIndex].Pitch = (ushort)((Voices[voiceIndex].Pitch & 0xff) | (b << 8));
                        break;
                    case 4:
                        Voices[voiceIndex].SourceNumber = b;
                        break;
                    case 5:
                        Voices[voiceIndex].Adsr1 = b;
                        break;
                    case 6:
                        Voices[voiceIndex].Adsr2 = b;
                        break;
                    case 7:
                        Voices[voiceIndex].Gain = b;
                        break;
                    case 8:
                    case 9:
                        // Read only registers ENVX and OUTX
                        break;
                    case 0xa:
                        Voices[voiceIndex].Unused1 = b;
                        break;
                    case 0xb:
                        Voices[voiceIndex].Unused2 = b;
                        break;
                    default:
                        throw new Exception($"Unsupported voice register {address:x2}");
                }
                return;
            }

            if ((address & 0xf) == 0xf)
                FilterCoefficients[address >> 4] = b;

            switch (address)
            {
                case 0x0c:
                    MainVolumeLeft = (sbyte)b;
                    break;
                case 0x1c:
                    MainVolumeRight = (sbyte)b;
                    break;
                case 0x2c:
                    EchoVolumeLeft = (sbyte)b;
                    break;
                case 0x3c:
                    EchoVolumeRight = (sbyte)b;
                    break;
                case 0x4c:
                    // KeyOn
                    for (int i = 0; i < Voices.Length; i++)
                    {
                        if ((b & 1) != 0)
                            KeyOn(i);
                        b >>= 1;
                    }
                    break;
                case 0x5c:
                    // KeyOff
                    for (int i = 0; i < Voices.Length; i++)
                    {
                        if ((b & 1) != 0)
                            KeyOff(i);
                        b >>= 1;
                    }
                    break;
                case 0x6c:
                    DspFlags = b;
                    break;
                case 0x7c:
                    // EndX, read only
                    //EndX = b;
                    break;
                case 0x0d:
                    EchoFeedback = b;
                    break;
                case 0x2d:
                    PitchModulation = b;
                    break;
                case 0x3d:
                    NoiseEnable = b;
                    break;
                case 0x4d:
                    EchoEnable = b;
                    break;
                case 0x5d:
                    SourceDirectoryOffset = b;
                    break;
                case 0x6d:
                    EchoBufferStartOffset = b;
                    break;
                case 0x7d:
                    EchoDelay = b;
                    break;
            }
        }

        private void GetOutputSamples()
        {
            CalculateOutputSamples(OutputSamplesLeft, _outputSampleIndex, isLeft: true);
            CalculateOutputSamples(OutputSamplesRight, _outputSampleIndex, isLeft: false);
            _outputSampleIndex += 2;
        }

        private void CalculateOutputSamples(byte[] output, int outputIndex, bool isLeft)
        {
            if (Mute)
            {
                output[outputIndex] = 0xff;
                output[outputIndex + 1] = 0xff;
                return;
            }

            var sample = 0;
            // TODO 16 bit overflow handling after each addition
            for (int i = 0; i < Voices.Length; i++)
            {
                // shift right because we're multiplying by volume, which is an integer
                sample += (Voices[i].OutX * (isLeft ? Voices[i].VolumeLeft : Voices[i].VolumeRight)) >> 6;
            }
            sample = (sample * (isLeft ? MainVolumeLeft : MainVolumeRight)) >> 7;
            sample = sample + (GetEchoSample(isLeft) * (isLeft ? EchoVolumeLeft : EchoVolumeRight)) >> 7;
            // Final phase inversion (as done by built-in post-amp)
            sample ^= 0xffff;

            output[outputIndex] = (byte)sample;
            output[outputIndex + 1] = (byte)(sample >> 8);
        }

        private short GetEchoSample(bool isLeft)
        {
            var sample = 0;
            for (int i = 0; i < Voices.Length; i++)
            {
                if ((EchoEnable & (1 << i)) == 0)
                    continue;
                sample += (Voices[i].OutX * (isLeft ? Voices[i].VolumeLeft : Voices[i].VolumeRight)) >> 6;
            }
            // TODO echo stuff
            return 0;
        }

        private short ApplyGaussianInterpolation(short sample, int interpolationIndex, short old, short older, short oldest)
        {
            // TODO with/without 16 bit overflow handling
            var output = (Gauss[0xff - interpolationIndex] * oldest) >> 10;
            output += (Gauss[0x1ff - interpolationIndex] * older) >> 10;
            output += (Gauss[0x100 + interpolationIndex] * old) >> 10;
            output += (Gauss[0x00 + interpolationIndex] * sample) >> 10;
            return (short)(output >> 1);
        }

        /// <summary>
        /// Executes at an emulated 32000 Hz, calculating one output sample per L/R channel.
        /// </summary>
        private void EachSampleCycle()
        {
            CalculateVoiceSamples(); // Updates ENVx and OUTx on each voice, which are used by the next step
            GetOutputSamples();
            // Are voice counters incremented first or last?
            IncrementVoiceCounters();
        }

        private void CalculateVoiceSamples()
        {
            for (int i = 0; i < Voices.Length; i++)
            {
                UpdateEnvValue(i);
                var sample = Voices[i].CurrentBrrBlock[Voices[i].PitchCounter >> 12];
            }
        }

        private void UpdateEnvValue(int voiceIndex)
        {
            Voices[voiceIndex].SamplesSinceEnvStep++;
            if (Voices[voiceIndex].SamplesSinceEnvStep == Voices[voiceIndex].GetSamplesForEnvStep())
            {
                Voices[voiceIndex].SamplesSinceEnvStep = 0;
                switch (Voices[voiceIndex].AdsrState)
                {
                    case AdsrState.Attack:
                        Voices[voiceIndex].ModifyLevel(0x20);
                        if (Voices[voiceIndex].Level >= 0x7e0)
                            Voices[voiceIndex].AdsrState = AdsrState.Decay;
                        break;
                    case AdsrState.Decay:
                        Voices[voiceIndex].ModifyLevel((short)-(((Voices[voiceIndex].Level - 1) >> 8) + 1));
                        if (Voices[voiceIndex].Level <= Voices[voiceIndex].GetSustainLevel())
                            Voices[voiceIndex].AdsrState = AdsrState.Sustain;
                        break;
                    case AdsrState.Sustain:
                        Voices[voiceIndex].ModifyLevel((short)-(((Voices[voiceIndex].Level - 1) >> 8) + 1));
                        break;
                    case AdsrState.Release:
                        Voices[voiceIndex].ModifyLevel(-8);
                        break;
                }
            }
        }

        private void IncrementVoiceCounters()
        {
            for (int i = 0; i < Voices.Length; i++)
            {
                var step = Voices[i].Pitch;
                if (i > 0 && (PitchModulation & (1 << i)) != 0)
                {
                    int factor = Voices[i - 1].OutX;
                    factor = (factor >> 4) + 0x400;
                    step = (ushort)((step * factor) >> 10);
                }
                var nextCounter = Voices[i].PitchCounter + step;
                if ((nextCounter & 0x10000) != 0)
                {
                    AdvanceBrrBlock(i);
                }
                Voices[i].PitchCounter = (ushort)nextCounter;
            }
        }

        private void AdvanceBrrBlock(int voiceIndex)
        {
            if ((EndX & (1 << voiceIndex)) != 0)
            {
                Voices[voiceIndex].CurrentBrrBlockAddress = GetLoopAddress(voiceIndex);
                if (!Voices[voiceIndex].Loop)
                {
                    KeyOff(voiceIndex);
                    Voices[voiceIndex].SetLevel(0);
                }
            }
            else
                Voices[voiceIndex].CurrentBrrBlockAddress += 9;

            DecompressBrrBlock(voiceIndex);
        }

        private void KeyOn(int voiceIndex)
        {
            Voices[voiceIndex].CurrentBrrBlockAddress = GetStartAddress(voiceIndex);
            Voices[voiceIndex].SetLevel(0);
            Voices[voiceIndex].PitchCounter = 0;
            Voices[voiceIndex].AdsrState = AdsrState.Attack;
            Voices[voiceIndex].SamplesSinceEnvStep = 0;
        }

        private void KeyOff(int voiceIndex)
        {
            Voices[voiceIndex].AdsrState = AdsrState.Release;
            Voices[voiceIndex].SamplesSinceEnvStep = 0;
        }

        private ushort GetSampleTableEntryAddress(int voiceIndex)
        {
            return (ushort)(SourceDirectoryOffset * 0x100 + Voices[voiceIndex].SourceNumber * 4);
        }

        private ushort GetStartAddress(int voiceIndex)
        {
            var tableEntryOffset = GetSampleTableEntryAddress(voiceIndex);
            return (ushort)(_psram.Span[tableEntryOffset] | (_psram.Span[tableEntryOffset + 1] << 8));
        }

        private ushort GetLoopAddress(int voiceIndex)
        {
            var tableEntryOffset = GetSampleTableEntryAddress(voiceIndex);
            return (ushort)(_psram.Span[tableEntryOffset + 2] | (_psram.Span[tableEntryOffset + 3] << 8));
        }

        private void DecompressBrrBlock(int voiceIndex)
        {
            // https://wiki.superfamicom.org/spc700-reference#bit-rate-reduction-(brr)-933
            // https://sneslab.net/wiki/Bit_Rate_Reduction
            var blockStartIndex = Voices[voiceIndex].CurrentBrrBlockAddress;
            var shift = _psram.Span[blockStartIndex] >> 4;
            var filter = (_psram.Span[blockStartIndex] >> 2) & 0x3;

            for (int i = 0; i < 16; i++)
            {
                byte compressedSampleRaw = (byte)((_psram.Span[blockStartIndex + 1 + i / 2] >> (i % 2 == 1 ? 0 : 4)) & 0xf);
                sbyte compressedSampleSigned = (sbyte)(compressedSampleRaw > 7 ? 0xf0 | compressedSampleRaw : compressedSampleRaw);
                short sample = shift < 13
                    ? (short)((compressedSampleSigned << shift) >> 1)
                    : (short)((compressedSampleSigned >> 3) << 11);

                switch (filter)
                {
                    case 0:
                        break;
                    case 1:
                        sample = (short)(sample + Voices[voiceIndex].lastSample - Voices[voiceIndex].lastSample >> 4);
                        break;
                    case 2:
                        sample = (short)(sample + Voices[voiceIndex].lastSample * 2 - (Voices[voiceIndex].lastSample * 3) >> 5
                            - Voices[voiceIndex].lastSample2 + Voices[voiceIndex].lastSample2 >> 4);
                        break;
                    case 3:
                        sample = (short)(sample + Voices[voiceIndex].lastSample * 2 - (Voices[voiceIndex].lastSample * 13) >> 6
                            - Voices[voiceIndex].lastSample2 + (Voices[voiceIndex].lastSample2 * 3) >> 4);
                        break;
                }
                // TODO handle clipping etc.

                Voices[voiceIndex].lastSample2 = Voices[voiceIndex].lastSample;
                Voices[voiceIndex].lastSample = sample;

                Voices[voiceIndex].CurrentBrrBlock[i] = sample;
            }

            Voices[voiceIndex].Loop = (_psram.Span[blockStartIndex] & 0x2) != 0;
            EndX |= (byte)((_psram.Span[blockStartIndex] & 1) << voiceIndex);
        }

        private struct Voice
        {
            public sbyte VolumeLeft; // TODO is this sign-magnitude or regular?
            public sbyte VolumeRight;
            public ushort Pitch; // 14 bits
            public byte SourceNumber;
            public byte Adsr1;
            public byte Adsr2;
            public byte Gain;
            public byte EnvX; // 7-bit unsigned current envelope value (top 7 of Level)
            public sbyte OutX; // 8-bit signed wave height * envelope value (not multiplied by volume) (upper 8 bits of 15 bit sample)
            public byte Unused1;
            public byte Unused2;

            // Incremented at each sample (32 kHz) based on pitch
            // High 4 bits: sample index, middle 8 bits: gaussian interpolation index
            public ushort PitchCounter;
            public ushort CurrentBrrBlockAddress;
            public short[] CurrentBrrBlock;
            public bool Loop;
            public AdsrState AdsrState;
            public ushort Level; // 11-bits unsigned current envelope value
            public ushort SamplesSinceEnvStep;

            public short lastSample;
            public short lastSample2;

            public bool AdsrEnabled { get { return (Adsr1 & 0x80) != 0; } }

            public void ModifyLevel(short change)
            {
                Level = (ushort)(Level + change);
                UpdateEnvX();
            }

            public void SetLevel(ushort level)
            {
                Level = level;
                UpdateEnvX();
            }

            private void UpdateEnvX()
            {
                EnvX = (byte)((Level >> 4) & 0x7f);
            }

            public ushort GetSamplesForEnvStep()
            {
                switch (AdsrState)
                {
                    case AdsrState.Attack:
                        var attackRate = (Adsr1 & 0xf) * 2 + 1;
                        return RateTable[attackRate];
                    case AdsrState.Decay:
                        var decayRate = ((Adsr1 >> 4) & 0x7) * 2 + 16;
                        return RateTable[decayRate];
                    case AdsrState.Sustain:
                        var sustainRate = (Adsr2 & 0x1f);
                        return RateTable[sustainRate];
                    case AdsrState.Release:
                        return 1;
                    default:
                        throw new Exception("Unexpected ADSR state: " + AdsrState);
                }
            }

            public ushort GetSustainLevel()
            {
                return (ushort)(((Adsr2 >> 5) + 1) * 0x100);
            }
        }

        /*  00h=Stop   04h=1024  08h=384   0Ch=160   10h=64   14h=24   18h=10   1Ch=4
            01h=2048   05h=768   09h=320   0Dh=128   11h=48   15h=20   19h=8    1Dh=3
            02h=1536   06h=640   0Ah=256   0Eh=96    12h=40   16h=16   1Ah=6    1Eh=2
            03h=1280   07h=512   0Bh=192   0Fh=80    13h=32   17h=12   1Bh=5    1Fh=1*/
        private static ushort[] RateTable = new ushort[]
            { 0xffff, 0x800, 0x600, 0x500, 0x400, 0x300, 0x280, 0x200, 0x180, 0x140, 0x100,
                0xc0, 0xa0, 0x80, 0x60, 0x50, 0x40, 0x30, 0x28, 0x20, 0x18, 0x14, 0x10, 0xc, 0xa, 8, 6, 5, 4, 3, 2, 1 };

        private enum AdsrState
        {
            Attack,
            Decay,
            Sustain,
            Release
        }
    }
}
