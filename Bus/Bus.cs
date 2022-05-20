using NesEmu.CPU;
using NesEmu.PPU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NesEmu.Bus {
    //  _______________ $10000  _______________
    // | PRG-ROM       |       |               |
    // | Upper Bank    |       |               |
    // |_ _ _ _ _ _ _ _| $C000 | PRG-ROM       |
    // | PRG-ROM       |       |               |
    // | Lower Bank    |       |               |
    // |_______________| $8000 |_______________|
    // | SRAM          |       | SRAM          |
    // |_______________| $6000 |_______________|
    // | Expansion ROM |       | Expansion ROM |
    // |_______________| $4020 |_______________|
    // | I/O Registers |       |               |
    // |_ _ _ _ _ _ _ _| $4000 |               |
    // | Mirrors       |       | I/O Registers |
    // | $2000-$2007   |       |               |
    // |_ _ _ _ _ _ _ _| $2008 |               |
    // | I/O Registers |       |               |
    // |_______________| $2000 |_______________|
    // | Mirrors       |       |               |
    // | $0000-$07FF   |       |               |
    // |_ _ _ _ _ _ _ _| $0800 |               |
    // | RAM           |       | RAM           |
    // |_ _ _ _ _ _ _ _| $0200 |               |
    // | Stack         |       |               |
    // |_ _ _ _ _ _ _ _| $0100 |               |
    // | Zero Page     |       |               |
    // |_______________| $0000 |_______________|
    public partial class Bus {
        public UInt64 CycleCount;
        public PPU.PPU PPU;
        public BizHawk.NES.APU APU;
        public ControllerRegister Controller1;

        byte[] VRAM;
        byte[] PrgRom;

        int FrameCycle;
        bool IsNewFrame;

        public Bus(NesCpu nes, Rom.Rom rom) {
            PPU = new PPU.PPU(rom.ChrRom, rom.Mirroring);
            APU = new BizHawk.NES.APU(nes, null, false);
            Controller1 = new ControllerRegister();

            VRAM = new byte[2048];
            PrgRom = rom.PrgRom;
            CycleCount = 0;
        }

        public void Reset() {
            APU.NESHardReset();
        }

        public bool GetNmiStatus() {
            return PPU.GetInterrupt();
        }

        public bool PollDrawFrame() {
            return IsNewFrame;
        }

        public bool GetDrawFrame() {
            var isFrame = IsNewFrame;
            IsNewFrame = false;
            return isFrame;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadPrgRom(ushort address) {
            unsafe {
                fixed (byte* ptr = PrgRom) {
                    // Make sure the address lines up with the prg rom
                    address -= 0x8000;

                    // If the address is longer than the prg rom, mirror it down
                    if (PrgRom.Length == 0x4000 && address >= 0x4000) {
                        address = (ushort)(address % 0x4000);
                    }
                    return *(ptr + address);
                }
            }
        }

        public int UnprocessedCycles;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TickCycles(byte cycleCount) {
//#if NESTEST
            CycleCount += cycleCount;

            // The PPU Runs at three times the cpu clock rate, so multiply cycles by 3
            var isNewFrame = PPU.IncrementCycle(cycleCount);
            if (isNewFrame) {
                IsNewFrame = isNewFrame;
                FrameCycle = 0;
            }
        }

        public void DumpPPUMemory() {
            var ChrRom = PPU.ChrRom;
            var Vram = PPU.Vram;
            var palette = PPU.PaletteTable;

            var chrFile = File.OpenWrite("chr.dump.txt");
            var vramFile = File.OpenWrite("vram.dump.txt");
            var paletteFile = File.OpenWrite("palete.dump.txt");

            chrFile.Write(
                Encoding.ASCII.GetBytes(string.Join(
                    ", ",
                    ChrRom.Select(x => x.ToString("X")).ToArray()
                ))
            );
            chrFile.Flush();
            chrFile.Close();

            vramFile.Write(
                Encoding.ASCII.GetBytes(string.Join(
                    ", ",
                    Vram.Select(x => x.ToString("X")).ToArray()
                ))
            );
            vramFile.Flush();
            vramFile.Close();

            paletteFile.Write(
                Encoding.ASCII.GetBytes(string.Join(
                    ", ",
                    palette.Select(x => x.ToString("X")).ToArray()
                ))
            );
            paletteFile.Flush();
            paletteFile.Close();
        }
    }
}
