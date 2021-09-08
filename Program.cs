using NesEmu.CPU;
using NesEmu.PPU;
using NesEmu.Rom;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osuTK;
using osuTK.Graphics;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace NesEmu {
    class Program {
        static void Main(string[] args) {
            using (GameHost host = Host.GetSuitableHost(@"NesEmu"))
            using (osu.Framework.Game game = new EmuWindow())
                host.Run(game);
        }
    }

    public class EmuWindow : osu.Framework.Game {
        private readonly DrawSizePreservingFillContainer gameScreen = new DrawSizePreservingFillContainer();
        private Box[] pixelList;

        Random rng;
        bool isInit = false;
        string fileName;
        CPU.CPU cpu;

        public EmuWindow() : base() {
            rng = new Random();

#if !NESTEST
            Console.WriteLine("Enter the filename of the .nes file to run");
            fileName = Console.ReadLine();
#else
            string fileName = "nestest.nes";
#endif
            byte[] romByteArr;
            try {
                romByteArr = File.ReadAllBytes(fileName);
            } catch (Exception e) {
                throw new FileNotFoundException("Couldn't find file, try again");
            }
            var rom = new Rom.Rom(romByteArr);
            cpu = new CPU.CPU(rom);
            var thread = new Thread(_ExecuteCPU);
            thread.Start();
        }

        private void _ExecuteCPU() {
            File.Delete($"log-{fileName}.log");
            var logFile = File.OpenWrite($"log-{fileName}.log");
            string[] stringBuffer = new string[256];
            byte bufferIdx = 0;
            try {
                int frameCount = 0;
                Stopwatch watch = new Stopwatch();
                watch.Start();

                while (cpu.Running) {
                    if (!isInit) {
                        Thread.Sleep(100);
                        continue;
                    }

                    if (cpu.ProgramCounter >= 60000) {
                        var trace = Trace.Log(cpu);
                        stringBuffer[bufferIdx] = trace;
                        bufferIdx++;
                    }
                    cpu.ExecuteNextInstruction();

                    if (cpu.Bus.GetDrawFrame()) {
                        frameCount++;
                        RenderFrame(cpu.Bus.PPU);
                        if (frameCount % 60 == 0) {
                            Console.WriteLine("Average {0} fps", frameCount / watch.Elapsed.TotalSeconds);
                        }
                    }
                }
                logFile.Close();
            } catch (Exception e) {
                foreach (var s in stringBuffer) {
                    if (s != null) {
                        logFile.Write(Encoding.UTF8.GetBytes(s));
                    }
                }
                logFile.Close();
            }
        }

        private void SetPixel(int x, int y, Color4 color) {
            var addr = y * 256 + x;
            pixelList[addr].Colour = color;
        }

        private void RenderFrame(PPU.PPU ppu) {
            var bank = ppu.GetBackgroundPatternAddr();

            for (var tileIndex = 0; tileIndex < 0x3c0; tileIndex++) {
                var tileAddr = ppu.Vram[tileIndex];
                var tile_x = tileIndex % 32;
                var tile_y = tileIndex / 32;

                var tile = ppu.ChrRom[(bank + tileAddr * 16)..(bank + tileAddr * 16 + 16)];

                for (var y = 0; y <= 7; y++) {
                    var upper = tile[y];
                    var lower = tile[y + 8];

                    for (var x = 0; x <= 7; x++) {
                        var value = (1 & lower) << 1 | (1 & upper);
                        upper = (byte)(upper >> 1);
                        lower = (byte)(lower >> 1);
                        Color4 color;
                        switch(value) {
                            case 0:
                                color = Palette.SystemPalette[0x01];
                                break;
                            case 1:
                                color = Palette.SystemPalette[0x23];
                                break;
                            case 2:
                                color = Palette.SystemPalette[0x27];
                                break;
                            case 3:
                                color = Palette.SystemPalette[0x30];
                                break;
                            default: throw new Exception("Something fucky");
                        };

                        var pixelX = tile_x * 8 + x;
                        var pixelY = tile_y * 8 + y;
                        SetPixel(pixelX, pixelY, color);
                    }
                }
            }
        }

        [BackgroundDependencyLoader]
        private void load() {
            // This is dumb, but add one box per pixel of the nes display
            // I blame not being able to easily find any other library I could easily use
            pixelList = new Box[256 * 240];
            for (var i = 0; i < 256 * 240; i++) {
                var x = i % 256;
                var y = (float)(Math.Floor((double)(i / 256)));

                pixelList[i] = new Box {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.Centre,
                    Colour = Color4.AntiqueWhite,
                    Size = new Vector2(1, 1),
                    Position = new Vector2(x, y),
                };
            }
            gameScreen.Children = pixelList;
            gameScreen.Strategy = DrawSizePreservationStrategy.Separate;
            gameScreen.TargetDrawSize = new Vector2(256, 240);
            AddInternal(gameScreen);
        }

        protected override void LoadComplete() {
            base.LoadComplete();

            isInit = true;
        }
    }

}
