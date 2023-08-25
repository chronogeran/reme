using System;

namespace RemeSnes.Hardware.Audio
{
    // TODO noise
    // TODO echo
    // TODO Sort out BRR decode. I should only have one block at a time rather than reading ahead...?
        // It looks like bsnes looks ahead for interpolation, but... doesn't look like those are decoded yet. I'm missing something.
        // Adds the next decode destination index to the interp_index, then adds 0-3 to it...
        // Okay, so yes, it does look ahead. Because of circular buffer and the way he flattens it out to twice the length,
        // the next destination is the same as the current base/first active sample.
        // Interpolation index can be as high as 7, and with interpolation, that means we can look up to 10 samples past the first.
        // Once our base gets to the second group, we decode a new block and advance.
        // The reason I don't see him decoding 3 blocks at first is because that happens on 3 separate cycles (--kon_delay & 3).
        // 3 groups are populated, the buffer cycles around to the starting position, and then we're off.
        // So it's not so different from what I'm doing.
        // If I add the keyon delay, then I can do a similar implementation to his.
        // Highest priority is probably just making sure pitch increments are handled at the appropriate times, with the appropriate clipping.e
    internal class Dsp
    {
        // How does the DSP know what time it is? Does it have an internal clock?
        // Who does the mixing of all the voices into the final output samples?
        // Outputs samples at 32KHz, or one sample every 32 SPC700 cycles.

        // Apparently this has its own input clock of nominal 24576000 Hz (24.576 MHz)
        // And internal clock of 3.072 MHz. This is 96 cycles between each 32kHz sample.

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
        internal readonly short[] OutputSamplesLeft = new short[1200];
        internal readonly short[] OutputSamplesRight = new short[1200];
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

        // These volumes are 8-bit two's complement
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
        private byte KeyOn;
        private byte KeyOff;

        private bool _everyOtherSample;

        public Dsp()
        {
            byte b = 1;
            for (byte i = 0; i < Voices.Length; i++)
            {
                Voices[i].SampleBuffer = new short[12];
                Voices[i].VoiceBit = b;
                b <<= 1;
                Voices[i].VoiceIndex = i;
            }
        }

        private bool Mute { get { return (DspFlags & 0x40) != 0; } }

        private ReadOnlyMemory<byte> _psram;

        internal void SetRam(ReadOnlyMemory<byte> psram)
        {
            _psram = psram;
        }

        private int _lastUnreadIndex;
        internal int GetAudioSampleStartIndex()
        {
            return _lastUnreadIndex;
        }
        internal int GetNumAudioSamples()
        {
            var length = _outputSampleIndex > _lastUnreadIndex
                ? _outputSampleIndex - _lastUnreadIndex
                : OutputSamplesLeft.Length - _lastUnreadIndex + _outputSampleIndex;
            _lastUnreadIndex = _outputSampleIndex;
            return length;
        }

        private enum VoiceRegister
        {
            VolL,
            VolR,
            PitchL,
            PitchH,
            SrcN,
            Adsr1,
            Adsr2,
            Gain,
            EnvX,
            OutX,
            Unused1,
            Unused2
        }

        private enum Register
        {
            MVolL = 0x0c,
            MVolR = 0x1c,
            EVolL = 0x2c,
            EVolR = 0x3c,
            KeyOn = 0x4c,
            KeyOff = 0x5c,
            Flags = 0x6c,
            EndX = 0x7c,
            EchoFeedback = 0x0d,
            PitchModulation = 0x2d,
            NoiseEnable = 0x3d,
            EchoEnable = 0x4d,
            SourceDirectory = 0x5d,
            EchoBufferStart = 0x6d,
            EchoDelay = 0x7d,
            FirCoefficients = 0x0f // at 0f, 1f, ... 7f
        }

        private const byte BRR_BLOCK_SIZE = 9;

        public byte Read(byte address)
        {
            address &= 0x7f;

            if ((address & 0xf) < 0xc)
            {
                var voiceIndex = address >> 4;
                return (VoiceRegister)(address & 0xf) switch
                {
                    VoiceRegister.VolL    => (byte)Voices[voiceIndex].VolumeLeft,
                    VoiceRegister.VolR    => (byte)Voices[voiceIndex].VolumeRight,
                    VoiceRegister.PitchL  => (byte)Voices[voiceIndex].Pitch,
                    VoiceRegister.PitchH  => (byte)(Voices[voiceIndex].Pitch >> 8),
                    VoiceRegister.SrcN    => Voices[voiceIndex].SourceNumber,
                    VoiceRegister.Adsr1   => Voices[voiceIndex].Adsr1,
                    VoiceRegister.Adsr2   => Voices[voiceIndex].Adsr2,
                    VoiceRegister.Gain    => Voices[voiceIndex].Gain,
                    VoiceRegister.EnvX    => Voices[voiceIndex].EnvX,
                    VoiceRegister.OutX    => (byte)Voices[voiceIndex].OutX,
                    VoiceRegister.Unused1 => Voices[voiceIndex].Unused1,
                    VoiceRegister.Unused2 => Voices[voiceIndex].Unused2,
                    _ => throw new Exception("Unsupported voice register " + address),
                };
            }
            if ((address & 0xf) == 0xf)
                return FilterCoefficients[address >> 4];

            return (Register)address switch
            {
                Register.MVolL => (byte)MainVolumeLeft,
                Register.MVolR => (byte)MainVolumeRight,
                Register.EVolL => (byte)EchoVolumeLeft,
                Register.EVolR => (byte)EchoVolumeRight,
                Register.KeyOn => 0,
                Register.KeyOff => 0,
                Register.Flags => DspFlags,
                Register.EndX => EndX,
                Register.EchoFeedback => EchoFeedback,
                Register.PitchModulation => PitchModulation,
                Register.NoiseEnable => NoiseEnable,
                Register.EchoEnable => EchoEnable,
                Register.SourceDirectory => SourceDirectoryOffset,
                Register.EchoBufferStart => EchoBufferStartOffset,
                Register.EchoDelay => EchoDelay,
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
                        //Console.WriteLine($"Set voice {voiceIndex} to pitch {Voices[voiceIndex].Pitch:x4}");
                        break;
                    case 3:
                        Voices[voiceIndex].Pitch = (ushort)((Voices[voiceIndex].Pitch & 0xff) | (b << 8));
                        if (voiceIndex == 0 || voiceIndex == 6 || voiceIndex == 7)
                            Console.WriteLine($"Set voice {voiceIndex} to pitch {Voices[voiceIndex].Pitch:x4}");
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

            switch ((Register)address)
            {
                case Register.MVolL:
                    MainVolumeLeft = (sbyte)b;
                    break;
                case Register.MVolR:
                    MainVolumeRight = (sbyte)b;
                    break;
                case Register.EVolL:
                    EchoVolumeLeft = (sbyte)b;
                    break;
                case Register.EVolR:
                    EchoVolumeRight = (sbyte)b;
                    break;
                case Register.KeyOn:
                    KeyOn = b;
                    break;
                case Register.KeyOff:
                    KeyOff = b;
                    break;
                case Register.Flags:
                    DspFlags = b;
                    break;
                case Register.EndX:
                    // read only
                    //EndX = b;
                    break;
                case Register.EchoFeedback:
                    EchoFeedback = b;
                    break;
                case Register.PitchModulation:
                    PitchModulation = b;
                    break;
                case Register.NoiseEnable:
                    NoiseEnable = b;
                    break;
                case Register.EchoEnable:
                    EchoEnable = b;
                    break;
                case Register.SourceDirectory:
                    SourceDirectoryOffset = b;
                    break;
                case Register.EchoBufferStart:
                    EchoBufferStartOffset = b;
                    break;
                case Register.EchoDelay:
                    EchoDelay = b;
                    break;
            }
        }

        // Emulation state
        private ulong _cyclesRun;

        /// <summary>
        /// These are considered 3.072 MHz cycles.
        /// </summary>
        internal void Run(uint cycles)
        {
            // New samples every 96 cycles
            var samples = (_cyclesRun + cycles) / 96 - (_cyclesRun / 96);
            for (uint i = 0; i < samples; i++)
            {
                EachSampleCycle();
            }

            _cyclesRun += cycles;
        }

        private void GetOutputSamples()
        {
            CalculateOutputSamples(OutputSamplesLeft, _outputSampleIndex, isLeft: true);
            CalculateOutputSamples(OutputSamplesRight, _outputSampleIndex, isLeft: false);
            _outputSampleIndex++;
            if (_outputSampleIndex == OutputSamplesLeft.Length)
            {
                _outputSampleIndex = 0;
            }
        }

        private void CalculateOutputSamples(short[] output, int outputIndex, bool isLeft)
        {
            if (Mute)
            {
                output[outputIndex] = -1;
                return;
            }

            var sample = 0;
            for (int i = 0; i < Voices.Length; i++)
            {
                // shift right because we're multiplying by volume, which is an integer
                sample += (Voices[i].CurrentSample * (isLeft ? Voices[i].VolumeLeft : Voices[i].VolumeRight)) >> 7;
                sample = Clamp16(sample);
            }
            sample = (sample * (isLeft ? MainVolumeLeft : MainVolumeRight)) >> 7;
            sample += (GetEchoSample(isLeft) * (isLeft ? EchoVolumeLeft : EchoVolumeRight)) >> 7;
            sample = Clamp16(sample);
            // Final phase inversion (as done by built-in post-amp)
            //sample ^= 0xffff;

            // Write sample
            output[outputIndex] = (short)sample;
        }

        private short GetEchoSample(bool isLeft)
        {
            var sample = 0;
            for (int i = 0; i < Voices.Length; i++)
            {
                if ((EchoEnable & (1 << i)) == 0)
                    continue;
                sample += (Voices[i].OutX * (isLeft ? Voices[i].VolumeLeft : Voices[i].VolumeRight)) >> 7;
            }
            // TODO echo stuff
            return 0;
        }

        private int InterpolateSample(ref Voice v)
        {
            var interpolationIndex = (v.PitchCounter >> 4) & 0xff;
            int output;
            output  = (Gauss[0x0ff - interpolationIndex] * v.GetSampleWithWrapping(0)) >> 11;
            output += (Gauss[0x1ff - interpolationIndex] * v.GetSampleWithWrapping(1)) >> 11;
            output += (Gauss[0x100 + interpolationIndex] * v.GetSampleWithWrapping(2)) >> 11;
            output  = (short)output;
            output += (Gauss[0x000 + interpolationIndex] * v.GetSampleWithWrapping(3)) >> 11;
            output = Clamp16(output);
            output &= ~1;
            return output;
        }

        /// <summary>
        /// Executes at an emulated 32000 Hz, calculating one output sample per L/R channel.
        /// </summary>
        private void EachSampleCycle()
        {
            CalculateVoiceSamples();
            GetOutputSamples();
            _everyOtherSample = !_everyOtherSample;
        }

        private void CalculateVoiceSamples()
        {
            for (int i = 0; i < Voices.Length; i++)
            {
                CalculateVoiceSample(ref Voices[i]);
            }
        }

        /// <summary>
        /// After running this, v.CurrentSample will be ready for adding to the total output sample.
        /// </summary>
        private void CalculateVoiceSample(ref Voice v)
        {
            // Represents all the voice-specific steps, simplified somewhat
            // (not currently worried about super accurate timing

            // 3b
            v.BrrBlockHeader = _psram.Span[v.CurrentBrrBlockAddress];

            // 3c
            bool ignorePitch = false;
            if (v.KeyOnDelay > 0)
            {
                if (v.KeyOnDelay == 5)
                {
                    v.CurrentBrrBlockAddress = GetStartAddress(ref v);
                    v.CurrentBrrBlockOffset = 1;
                    v.CurrentBufferIndex = 0;
                    // Ignore header this sample
                    v.BrrBlockHeader = 0;
                }

                v.Level = 0;

                v.PitchCounter = 0;
                // Last 3 samples, decode a sample each time
                if ((--v.KeyOnDelay & 3) != 0)
                {
                    v.PitchCounter = 0x4000;
                }

                ignorePitch = true;
            }

            int sample = InterpolateSample(ref v);

            // TODO noise override

            // Apply envelope
            v.CurrentSample = ((sample * v.Level) >> 11) & ~1;
            v.EnvX = (byte)(v.Level >> 4);

            // Immediate silence due to end of sample or soft reset
            if ((DspFlags & 0x80) != 0 || (v.BrrBlockHeader & 3) == 1)
            {
                v.AdsrState = AdsrState.Release;
                v.Level = 0;
            }

            if (_everyOtherSample)
            {
                if ((KeyOff & v.VoiceBit) != 0)
                {
                    v.AdsrState = AdsrState.Release;
                    // Technically these values are cleared at a different time, but
                    // for simplicity, I'll clear them as they're handled.
                    KeyOff &= (byte)~v.VoiceBit;
                }
                if ((KeyOn & v.VoiceBit) != 0)
                {
                    v.KeyOnDelay = 5;
                    v.AdsrState = AdsrState.Attack;
                    KeyOn &= (byte)~v.VoiceBit;
                    // The rest of the key on will be processed in the next sample step
                }
            }

            // Update envelope
            if (v.KeyOnDelay == 0)
            {
                UpdateEnvValue(ref v);
            }

            // 4
            // Decode the next group if necessary
            if (v.PitchCounter >= 0x4000)
            {
                DecompressBrrSampleGroup(ref v);

                // Advance to the next block if necessary
                if ((v.CurrentBrrBlockOffset += 2) >= BRR_BLOCK_SIZE)
                {
                    if ((v.BrrBlockHeader & 1) != 0)
                    {
                        v.CurrentBrrBlockAddress = GetLoopAddress(ref v);
                        EndX |= v.VoiceBit;
                    }
                    else
                        v.CurrentBrrBlockAddress += BRR_BLOCK_SIZE;
                    v.CurrentBrrBlockOffset = 1;
                }
            }

            // Apply pitch
            if (!ignorePitch)
            {
                int increment = v.Pitch;
                if ((PitchModulation & v.VoiceBit) != 0 && v.VoiceIndex > 0)
                {
                    // I'm using t_output from the previous voice... after which step?
                    // This usage happens in voice i step 3c
                    // t_output is set later in step 3c, and that's the only time
                    // 
                    // At that point, the previous voice would be on step
                    increment += ((Voices[v.VoiceIndex - 1].CurrentSample >> 5) * increment) >> 10;
                }
                var newCounter = (v.PitchCounter & 0x3fff) + increment;
                if (newCounter > 0x7fff)
                    newCounter = 0x7fff;
                v.PitchCounter = (ushort)newCounter;
            }

            // 5
            if (v.KeyOnDelay == 5)
                EndX &= (byte)~v.VoiceBit;

            // 6
            v.OutX = (byte)(v.CurrentSample >> 8);
        }

        private void UpdateEnvValue(ref Voice v)
        {
            if (v.AdsrState == AdsrState.Release)
            {
                v.SamplesSinceEnvStep = 0;
                if (v.Level < 8)
                    v.Level = 0;
                else
                    v.Level -= 8;
                return;
            }

            var envMode = v.EnvelopeMode;
            if (envMode == EnvelopeMode.DirectGain)
            {
                v.SamplesSinceEnvStep = 0;
                v.Level = (ushort)((v.Gain & 0x7f) * 16);
                return;
            }

            v.SamplesSinceEnvStep++;
            if (v.SamplesSinceEnvStep == v.GetSamplesForEnvStep())
            {
                v.SamplesSinceEnvStep = 0;
                switch (envMode)
                {
                    case EnvelopeMode.LinearDecrease:
                        v.ModifyLevel(-0x20);
                        break;
                    case EnvelopeMode.ExponentialDecrease:
                        v.ModifyLevel((short)-(((v.Level - 1) >> 8) + 1));
                        break;
                    case EnvelopeMode.LinearIncrease:
                        v.ModifyLevel(0x20);
                        break;
                    case EnvelopeMode.BentIncrease:
                        v.ModifyLevel((short)(v.Level < 0x600 ? 0x20 : 0x8));
                        break;
                    case EnvelopeMode.Adsr:
                        switch (v.AdsrState)
                        {
                            case AdsrState.Attack:
                                v.ModifyLevel(0x20);
                                if (v.Level >= 0x7e0)
                                    v.AdsrState = AdsrState.Decay;
                                break;
                            case AdsrState.Decay:
                                v.ModifyLevel((short)-(((v.Level - 1) >> 8) + 1));
                                if (v.Level <= v.GetSustainLevel())
                                    v.AdsrState = AdsrState.Sustain;
                                break;
                            case AdsrState.Sustain:
                                v.ModifyLevel((short)-(((v.Level - 1) >> 8) + 1));
                                break;
                            case AdsrState.Release:
                                v.ModifyLevel(-8);
                                break;
                        }
                        break;
                }
            }
        }

        private ushort GetSampleTableEntryAddress(byte sourceNumber)
        {
            return (ushort)(SourceDirectoryOffset * 0x100 + sourceNumber * 4);
        }

        private ushort GetStartAddress(ref Voice v)
        {
            var tableEntryOffset = GetSampleTableEntryAddress(v.SourceNumber);
            return (ushort)(_psram.Span[tableEntryOffset] | (_psram.Span[tableEntryOffset + 1] << 8));
        }

        private ushort GetLoopAddress(ref Voice v)
        {
            var tableEntryOffset = GetSampleTableEntryAddress(v.SourceNumber);
            return (ushort)(_psram.Span[tableEntryOffset + 2] | (_psram.Span[tableEntryOffset + 3] << 8));
        }

        /// <summary>
        /// Decodes 1 group (4 samples) from the BRR stream.
        /// </summary>
        private void DecompressBrrSampleGroup(ref Voice v)
        {
            var shift = v.BrrBlockHeader >> 4;
            //var filter = (v.BrrBlockHeader >> 2) & 0x3;
            var filter = v.BrrBlockHeader & 0xc;

            // Previous samples come from ring buffer (already decoded, meaning filter is already applied).
            // There is no conflict when overwriting the previous first active group, because we look back as we decode;
            // samples are not overwritten until they are done being used (there is no looking back after the decode step).

            for (int s = 0; s < 4; s++)
            {
                byte compressedSampleRaw = (byte)((_psram.Span[v.CurrentBrrBlockAddress + v.CurrentBrrBlockOffset + s / 2] >> (s % 2 == 1 ? 0 : 4)) & 0xf);
                sbyte compressedSampleSigned = (sbyte)(compressedSampleRaw > 7 ? 0xf0 | compressedSampleRaw : compressedSampleRaw);
                int sample = shift < 13
                    ? (compressedSampleSigned << shift) >> 1
                    : (compressedSampleSigned < 0 ? -0x800 : 0);
                var dest = v.CurrentBufferIndex + s;
                var prev1 = v.GetSampleFromIndexWithWrapping(dest - 1);
                var prev2 = v.GetSampleFromIndexWithWrapping(dest - 2);

                //switch (filter)
                //{
                //    case 0:
                //        break;
                //    case 1:
                //        sample += prev1 - (prev1 >> 4);
                //        break;
                //    case 2:
                //        sample += prev1 * 2 - ((prev1 * 3) >> 5)
                //            - prev2 + (prev2 >> 4);
                //        break;
                //    case 3:
                //        sample += prev1 * 2 - ((prev1 * 13) >> 6)
                //            - prev2 + ((prev2 * 3) >> 4);
                //        break;
                //}

                // From bsnes. Coefficients are halved. What's up with that?
                if (filter >= 8)
                {
                    sample += prev1;
                    sample -= prev2;
                    if (filter == 8) // s += p1 * 0.953125 - p2 * 0.46875
                    {
                        sample += prev2 >> 4;
                        sample += (prev1 * -3) >> 6;
                    }
                    else // s += p1 * 0.8984375 - p2 * 0.40625
                    {
                        sample += (prev1 * -13) >> 7;
                        sample += (prev2 * 3) >> 4;
                    }
                }
                else if (filter > 0) // s += p1 * 0.46875
                {
                    sample += prev1 >> 1;
                    sample += (-prev1) >> 5;
                }

                sample = Clamp16(sample);
                // Clip to 15 bits
                //sample &= 0x7fff;
                sample = (short)(sample * 2);

                v.SampleBuffer[dest] = (short)sample;
            }

            if ((v.CurrentBufferIndex += 4) >= v.SampleBuffer.Length)
                v.CurrentBufferIndex = 0;
        }

        private static int Clamp16(int value)
        {
            if (value < short.MinValue)
                return short.MinValue;
            if (value > short.MaxValue)
                return short.MaxValue;
            return value;
        }

        private struct Voice
        {
            // Register values
            // These volumes are 8-bit two's complement
            public sbyte VolumeLeft;
            public sbyte VolumeRight;
            public ushort Pitch; // 14 bits
            public byte SourceNumber;
            public byte Adsr1;
            public byte Adsr2;
            public byte Gain;
            public byte EnvX; // 7-bit unsigned current envelope value (top 7 of Level)
            public byte OutX; // 8-bit signed wave height * envelope value (not multiplied by volume) (upper 8 bits of 15 bit sample)
            public byte Unused1;
            public byte Unused2;

            // Non-register state
            /// <summary>
            /// High 4 bits: sample index, middle 8 bits: gaussian interpolation index.
            /// Incremented at each sample (32 kHz) based on pitch.
            /// </summary>
            public ushort PitchCounter;
            /// <summary>
            /// Pointer to latest compressed BRR block in memory.
            /// This advances with what we decode, not what the beginning of our buffer came from.
            /// </summary>
            public ushort CurrentBrrBlockAddress;
            /// <summary>
            /// Offset within the current BRR block we are decoding. Advances from group to group,
            /// so should always be 1,3,5, or 7.
            /// </summary>
            public byte CurrentBrrBlockOffset;
            /// <summary>
            /// Base index for accessing decompressed samples in SampleBuffer, rotates in increments of 4 (should always be 0, 4, or 8).
            /// This points both to where the next samples will be decoded to, as well as to the "0" for reading them.
            /// </summary>
            public byte CurrentBufferIndex;
            /// <summary>
            /// 12 sample ring buffer (3 "groups" of 4 samples each). These are decoded 15-bit samples.
            /// </summary>
            public short[] SampleBuffer;
            /// <summary>
            /// Current envelope state. "Release" state is valid even for Gain modes.
            /// </summary>
            public AdsrState AdsrState;
            /// <summary>
            /// Full 11-bit unsigned current envelope value.
            /// </summary>
            public ushort Level;
            /// <summary>
            /// 15 bit sample (full version of OutX).
            /// </summary>
            public int CurrentSample;
            /// <summary>
            /// Counter for when we should update the envelope/Level value.
            /// TODO switch to calculation using global counter rather than saving this for each voice
            /// </summary>
            public ushort SamplesSinceEnvStep;
            /// <summary>
            /// Voices have a warmup period of 5 samples when they are keyed on.
            /// Counts down from 5 to 0.
            /// </summary>
            public byte KeyOnDelay;
            /// <summary>
            /// Copy of the current BRR block's header. We keep a copy so we can ignore it on key on.
            /// </summary>
            public byte BrrBlockHeader;
            /// <summary>
            /// 1, 2, 4, 8... for voices 0, 1, 2, 3...
            /// </summary>
            public byte VoiceBit;
            /// <summary>
            /// 0-7
            /// </summary>
            public byte VoiceIndex;

            /// <summary>
            /// ADSR/Gain mode, as read from current register values.
            /// </summary>
            public EnvelopeMode EnvelopeMode
            {
                get
                {
                    if ((Adsr1 & 0x80) != 0)
                        return EnvelopeMode.Adsr;
                    if ((Gain & 0x80) == 0)
                        return EnvelopeMode.DirectGain;
                    return (EnvelopeMode)(((Gain >> 5) & 0x3) + 2);
                }
            }

            public void ModifyLevel(short change)
            {
                Level = (ushort)(Level + change);
            }

            /// <summary>
            /// Gets a sample from the given index, wrapping if necessary.
            /// </summary>
            public short GetSampleFromIndexWithWrapping(int index)
            {
                if (index >= SampleBuffer.Length)
                    index -= SampleBuffer.Length;
                else if (index < 0)
                    index += SampleBuffer.Length;
                return SampleBuffer[index];
            }

            /// <summary>
            /// Gets a sample relative to current pointer (using PitchCounter and CurrentBufferIndex).
            /// </summary>
            public short GetSampleWithWrapping(int offset = 0)
            {
                return GetSampleFromIndexWithWrapping(CurrentBufferIndex + (PitchCounter >> 12) + offset);
            }

            public ushort GetSamplesForEnvStep()
            {
                var envMode = EnvelopeMode;
                if (AdsrState == AdsrState.Release)
                    return 1;
                if (envMode == EnvelopeMode.DirectGain)
                    return 0;
                if (envMode != EnvelopeMode.Adsr)
                    return RateTable[Gain & 0x1f];

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

        private enum EnvelopeMode
        {
            Adsr,
            DirectGain,
            LinearDecrease,
            ExponentialDecrease,
            LinearIncrease,
            BentIncrease
        }
    }
}
