var snesCore = new RemeSnes.RemeSnes();

snesCore.LoadRom(File.ReadAllBytes(@"C:\Users\chron\reme\Final Fantasy 2 (V1.1).smc"));

snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04802b, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Waiting for AABB");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04805d, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Signaling CC, waiting for echo");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04807d, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Transfer complete");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x0480b0, RemeSnes.RemeSnes.BreakpointFlags.Execute, "All transfers complete");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x0480c0, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Signaling SPC to start");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x048120, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Sending command to APU");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04815a, RemeSnes.RemeSnes.BreakpointFlags.Execute, "APU command done");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x00866f, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Beginning title screen wait loop");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x008687, RemeSnes.RemeSnes.BreakpointFlags.Execute, "End title screen wait loop");

snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0x085f, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Beginning main loop");
//snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0x0878, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Outside top main loop");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0x1432, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Processing command 1,3, or 4");

while (true)
{
    snesCore.Update();
    Thread.Sleep(16);
}
