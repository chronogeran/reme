using RemeSnes.Hardware;
using RemeSnes.Hardware.Audio;

namespace RemeSnes
{
    public class RemeSnes
    {
        private readonly Cpu Cpu;
        private readonly Bus Bus;
        private readonly Ppu Ppu;
        private readonly Wram Wram;
        private readonly Rom Rom;
        private readonly Sram Sram;
        private readonly Apu Apu;

        public RemeSnes()
        {
            Wram = new Wram();
            Ppu = new Ppu();
            Sram = new Sram();
            Rom = new Rom();
            Apu = new Apu();
            Cpu = new Cpu(Rom);
            Bus = new Bus(Wram, Sram, Rom, Ppu, Apu, Cpu);
            Ppu.SetBus(Bus);
            Cpu.SetBus(Bus);
        }

        public void LoadRom(byte[] romFile)
        {
            Rom.Initialize(romFile);
            Apu.Reset();
            Ppu.Reset();
            Cpu.Begin(Rom.ResetVector);
        }

        /// <summary>
        /// Sets input values for a SNES controller.
        /// </summary>
        /// <param name="joypadIndex">Controller index from 0 to 3.</param>
        /// <param name="buttons">Bits from least to most significant: right, left, down, up, Start, Select, Y, B, -, -, -, -, R, L, X, A.
        /// (Low 4 bits of high byte are not used.)
        /// </param>
        public void SetJoypadButtons(int joypadIndex, ushort buttons)
        {
            Bus.SetJoypadData(joypadIndex, buttons);
        }

        /// <summary>
        /// Returns a pointer to the rendered graphical frame data for display on screen.
        /// TODO: format
        /// TODO: when this should be called/when the data is valid
        /// </summary>
        public Span<byte> GetFrameBuffer()
        {
            return Ppu.RenderedFrameData;
        }

        //public Span<byte> GetAudioBuffer()
        //{
        //}
        public int GetValidAudioSampleLength()
        {
            return 0;
        }
        public int GetValidAudioSampleIndex()
        {
            return 0;
        }

        public void EmulateFrame()
        {
            Cpu.EmulateFrame();
            Apu.EmulateFrame();
            Ppu.EmulateFrame();
        }

        public void RunOneInstruction()
        {
            Cpu.RunOneInstruction();
        }
    }
}