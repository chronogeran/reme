var snesCore = new RemeSnes.RemeSnes();

snesCore.LoadRom(File.ReadAllBytes(@"C:\Users\chron\reme\Final Fantasy 2 (V1.1).smc"));

snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04802b, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Waiting for AABB");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04805d, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Signaling CC, waiting for echo");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x04807d, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Transfer complete");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x0480b0, RemeSnes.RemeSnes.BreakpointFlags.Execute, "All transfers complete");
snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Cpu, 0x0480c0, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Signaling SPC to start");

snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0x08ed, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Danger zone");
//snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0xffc9, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Signaling AABB");
//snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0xffd4, RemeSnes.RemeSnes.BreakpointFlags.Execute, "CC received");
//snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0xffef, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Reading instruction");
//snesCore.SetBreakpoint(RemeSnes.RemeSnes.BreakpointType.Spc, 0xfffb, RemeSnes.RemeSnes.BreakpointFlags.Execute, "Jumping to user code");

while (true)
{
    snesCore.Update();
    Thread.Sleep(16);
}
