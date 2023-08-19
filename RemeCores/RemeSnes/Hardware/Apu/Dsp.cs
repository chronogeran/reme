namespace RemeSnes.Hardware.Audio
{
    internal class Dsp
    {
        // How does the DSP know what time it is? Does it have an internal clock?
        // Who does the mixing of all the voices into the final output samples?
        // Outputs samples at 32KHz, or one sample every 32 SPC700 cycles.

        private readonly Voice[] Voices = new Voice[8];
        private readonly byte[] FilterCoefficients = new byte[8];

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

        public byte Read(byte address)
        {
            address &= 0x7f;

            if ((address & 0xf) < 0xa)
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
                    default:
                        throw new Exception("Unsupported voice register " + address);
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
                            Voices[i].KeyOn();
                        b >>= 1;
                    }
                    break;
                case 0x5c:
                    // KeyOff
                    for (int i = 0; i < Voices.Length; i++)
                    {
                        if ((b & 1) != 0)
                            Voices[i].KeyOff();
                        b >>= 1;
                    }
                    break;
                case 0x6c:
                    DspFlags = b;
                    break;
                case 0x7c:
                    // TODO no write to this?
                    EndX = b;
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

        private struct Voice
        {
            public sbyte VolumeLeft; // TODO is this sign-magnitude or regular?
            public sbyte VolumeRight;
            public ushort Pitch;
            public byte SourceNumber;
            public byte Adsr1;
            public byte Adsr2;
            public byte Gain;
            public byte EnvX; // 7-bit unsigned current envelope value
            public sbyte OutX; // 8-bit signed wave height * envelope value (not multiplied by volume)

            byte TimeSinceKeyOn;

            public void KeyOn()
            {
                // TODO
                TimeSinceKeyOn = 0;
            }

            public void KeyOff()
            {
                // TODO
            }

            public bool AdsrEnabled { get { return (Adsr1 & 0x80) != 0; } }
        }
    }
}
