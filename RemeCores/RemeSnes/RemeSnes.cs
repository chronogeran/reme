using RemeSnes.Hardware;

namespace RemeSnes
{
    public class RemeSnes
    {
        private Cpu Cpu;
        private Bus Bus;
        private Ppu Ppu;
        private Wram Wram;
        private Rom Rom;
        private Sram Sram;

        public RemeSnes()
        {
            Wram = new Wram();
            Ppu = new Ppu();
            Sram = new Sram();
            Rom = new Rom();
            Bus = new Bus(Wram, Sram, Rom, Ppu);
            Cpu = new Cpu(Bus, Rom);
        }

        public void LoadRom(byte[] romFile)
        {
            Rom.Initialize(romFile);
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

        public void RunOneInstruction()
        {
            Cpu.RunOneInstruction();
        }

        public void RunOneFrame()
        {
            // TODO
        }
    }
}