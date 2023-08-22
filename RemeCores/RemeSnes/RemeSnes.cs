using RemeSnes.Hardware;
using RemeSnes.Hardware.Audio;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

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

        private static int _instanceNumber;

        public RemeSnes()
        {
            Wram = new Wram();
            Ppu = new Ppu();
            Sram = new Sram();
            Rom = new Rom();
            Apu = new Apu();
            Cpu = new Cpu();
            Bus = new Bus(Wram, Sram, Rom, Ppu, Apu, Cpu);
            Ppu.SetBus(Bus);
            Cpu.SetBus(Bus);

            _emulationThread = new Thread(ThreadLoop) { Name = $"RemeSnes_{_instanceNumber++} Thread" };
            _emulationThread.Start();
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

        public short[] GetAudioBufferRight()
        {
            return Apu.GetAudioBufferRight();
        }
        public short[] GetAudioBufferLeft()
        {
            return Apu.GetAudioBufferLeft();
        }
        public int GetValidAudioSampleLength()
        {
            return Apu.GetNumAudioSamples();
        }
        public int GetValidAudioSampleIndex()
        {
            return Apu.GetAudioSampleStartIndex();
        }

        public void Update()
        {
            _emulateSignal.Set();
            //Cpu.EmulateFrame();
            //Apu.EmulateFrame();
            //Ppu.EmulateFrame();
        }

        public void RunOneInstruction()
        {
            Cpu.RunOneInstruction();
        }

        public void SetBreakpoint(BreakpointType type, int address, BreakpointFlags flags, string name)
        {
            var bp = new Breakpoint
            {
                Address = address,
                Flags = flags,
                Name = name
            };
            if (type == BreakpointType.Cpu)
            {
                Cpu.SetBreakpoint(bp);
            }
            else if (type == BreakpointType.Spc)
            {
                Apu.SetBreakpoint(bp);
            }
        }

        public enum BreakpointType
        {
            Cpu,
            Spc,
        }
        [Flags]
        public enum BreakpointFlags
        {
            Read = 0x1, Write = 0x2, Execute = 0x4,
        }
        internal struct Breakpoint
        {
            public string Name;
            public BreakpointFlags Flags;
            public int Address;
        }

        internal static readonly uint MASTER_CYCLES_PER_FRAME = 357366;
        internal static readonly uint APU_CYCLES_PER_FRAME = 17087;
        private static readonly uint MASTER_CYCLES_PER_ITERATION = 335;
        private static readonly uint APU_CYCLES_PER_ITERATION = 16;
        private static readonly uint ITERATIONS = MASTER_CYCLES_PER_FRAME / MASTER_CYCLES_PER_ITERATION;

        // Single thread per emulator approach
        private Thread _emulationThread;
        private ManualResetEvent _emulateSignal = new ManualResetEvent(false);
        private bool _shuttingDown;
        private void ThreadLoop()
        {
            while (!_shuttingDown)
            {
                _emulateSignal.WaitOne();
                EmulateFrame();
            }
        }

        private void EmulateFrame()
        {
            var sw = Stopwatch.StartNew();
            // One frame would be about 357,366 master clock cycles and 17,868 (or 17,038 or 17,087) SPC cycles.
            // I want each component to run in lock step with each other. What would a good step value be?
            // Maybe every 16 SPC cycles?

            uint masterCyclesRun = 0;
            uint apuCyclesRun = 0;
            for (int i = 0; i < ITERATIONS; i++)
            {
                masterCyclesRun += MASTER_CYCLES_PER_ITERATION;
                apuCyclesRun += APU_CYCLES_PER_ITERATION;
                Cpu.Run(MASTER_CYCLES_PER_ITERATION);
                Apu.Run(APU_CYCLES_PER_ITERATION);
                Ppu.Run(MASTER_CYCLES_PER_ITERATION);
            }

            Cpu.Run(MASTER_CYCLES_PER_FRAME - masterCyclesRun);
            Apu.Run(APU_CYCLES_PER_FRAME - apuCyclesRun);
            Ppu.Run(MASTER_CYCLES_PER_FRAME - masterCyclesRun);

            sw.Stop();
            //Console.WriteLine($"Frame emulated in {sw.Elapsed.TotalMilliseconds} ms");
        }
    }
}