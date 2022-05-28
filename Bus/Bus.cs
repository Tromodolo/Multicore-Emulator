using BizHawk.Emulation.Common;
using NesEmu.CPU;
using NesEmu.Mapper;
using NesEmu.PPU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SDL2.SDL;

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
        public ulong CycleCount;
        public NesCpu CPU;
        public PPU.PPU PPU;
        public BizHawk.NES.APU APU;
        public IMapper Mapper;
        public ControllerRegister Controller1;

        byte[] VRAM;
        byte[] PrgRom;

        int FrameCycle;
        bool IsNewFrame;

        int cpuCyclesLeft = 0;

        public BlipBuffer blip = new(4096);
        int blipbuffsize = 4096;
        int oldSample;

        public byte DmaPage;
        public byte DmaAddr;
        public byte DmaData;
        public bool DmaDummyRead;
        public bool DmaActive;

        public Bus(NesCpu nes, PPU.PPU ppu, BizHawk.NES.APU apu, Rom.Rom rom) {
            CPU = nes;
            PPU = ppu;
            APU = apu;
            Controller1 = new();
            Mapper = rom.Mapper;

            VRAM = new byte[2048];
            PrgRom = rom.PrgRom;
            CycleCount = 0;

            blip.Clear();
            blip.SetRates(1789773, 44100);
        }

        public bool Clock() {
            CycleCount++;

            var isNewFrame = PPU.Clock();
            if (isNewFrame) {
                IsNewFrame = isNewFrame;
                FrameCycle = 0;
            }

            Mapper.SetProgramCounter(CPU.ProgramCounter);
            Mapper.SetScanline(PPU.CurrentScanline);

            if (CycleCount % 3 == 0) {
                if (DmaActive) {
                    if (DmaDummyRead) {
                        if (CycleCount % 2 == 1) {
                            DmaDummyRead = false;
                        }
                    } else {
                        if (CycleCount % 2 == 0) {
                            DmaData = MemRead((ushort)(DmaPage << 8 | DmaAddr));
                        } else {
                            PPU.OamData[DmaAddr] = DmaData;

                            DmaAddr++;

                            // Wrap around
                            if (DmaAddr == 0) {
                                DmaActive = false;
                                DmaDummyRead = true;
                            }
                        }
                    }
                }
                APU.RunOneFirst();

                if (cpuCyclesLeft == 0) {
                    cpuCyclesLeft = CPU.ExecuteInstruction();
                }
                cpuCyclesLeft--;

                APU.RunOneLast();

                int samle = APU.EmitSample();
                if (samle != oldSample) {
                    blip.AddDelta(APU.sampleclock, samle - oldSample);
                    oldSample = samle;
                }

                APU.sampleclock++;
            }

            return IsNewFrame;
        }

        public void Reset() {
            APU.NESHardReset();

            DmaPage = 0x00;
            DmaAddr = 0x00;
            DmaData = 0x00;
            DmaDummyRead = true;
            DmaActive = false;
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

        public void Save(BinaryWriter writer) {
            writer.Write(CycleCount);
            writer.Write(VRAM);
            writer.Write(FrameCycle);
            writer.Write(IsNewFrame);
            writer.Write(cpuCyclesLeft);
            writer.Write(DmaPage);
            writer.Write(DmaAddr);
            writer.Write(DmaData);
            writer.Write(DmaDummyRead);
            writer.Write(DmaActive);
            Mapper.Save(writer);
        }

        public void Load(BinaryReader reader) {
            CycleCount = reader.ReadUInt64();
            VRAM = reader.ReadBytes(VRAM.Length);
            FrameCycle = reader.ReadInt32();
            IsNewFrame = reader.ReadBoolean();
            cpuCyclesLeft = reader.ReadInt32();
            DmaPage = reader.ReadByte();
            DmaAddr = reader.ReadByte();
            DmaData = reader.ReadByte();
            DmaDummyRead = reader.ReadBoolean();
            DmaActive = reader.ReadBoolean();
            Mapper.Load(reader);
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
