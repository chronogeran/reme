namespace RemeSnes.Hardware.Audio
{
    /// <summary>
    /// Represents the combination of the SPC700 CPU, the S-DSP, and the audio PSRAM.
    /// </summary>
    internal class Apu
    {
        //public byte[] Psram = new byte[0x10000]; // Holds code and data for the SPC700
        private Spc700 _spc700 = new Spc700();
        private Dsp _dsp = new Dsp();

        public Apu()
        {
            _spc700.SetDsp(_dsp);
        }

        public void Reset()
        {
            _spc700.Reset();
            //_dsp.Reset();
        }

        public void EmulateFrame()
        {
            _spc700.EmulateFrame();
        }

        internal void Run(uint cycles)
        {
            _spc700.Run(cycles);
        }

        internal void SetBreakpoint(RemeSnes.Breakpoint bp)
        {
            _spc700.SetBreakpoint(bp);
        }

        public byte Port0 { get { return _spc700.Port0; } set { _spc700.Port0 = value; } }
        public byte Port1 { get { return _spc700.Port1; } set { _spc700.Port1 = value; } }
        public byte Port2 { get { return _spc700.Port2; } set { _spc700.Port2 = value; } }
        public byte Port3 { get { return _spc700.Port3; } set { _spc700.Port3 = value; } }
    }
}
