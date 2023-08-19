var snesCore = new RemeSnes.RemeSnes();

snesCore.LoadRom(File.ReadAllBytes(@"C:\Users\chron\reme\Final Fantasy 2 (V1.1).smc"));

while (true)
{
    snesCore.EmulateFrame();
    Thread.Sleep(16);
}
