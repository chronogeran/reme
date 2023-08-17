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
            Bus = new Bus(Wram, Sram, Rom);
            Cpu = new Cpu(Bus, Rom);
        }

        public void LoadRom(byte[] romFile)
        {
            Rom.Initialize(romFile);
            Cpu.Begin(Rom.ResetVector);
        }

        public void RunOneInstruction()
        {
            Cpu.RunOneInstruction();
        }
    }
}