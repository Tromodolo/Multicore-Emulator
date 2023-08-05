using MultiCoreEmulator.Utility;
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

namespace NesEmu.Bus
{
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
        const int BLIP_BUFFER_SIZE = 4096;
        public BlipBuffer blipBuffer = new BlipBuffer(BLIP_BUFFER_SIZE);
        
        public readonly IMapper currentMapper;
        
        public bool IsNewFrame;
        public bool APUIRQ;
        public bool DMCDMAActive;

        NesCpu CPU;
        PPU.PPU PPU;
        BizHawk.NES.APU APU;
        
        byte[] VRAM;
        byte[] PRG;

        ulong totalCycleCount;
        int CPUCyclesUntilNext;

        int oldBlipSample;

        byte DMAPage;
        byte DMAAddress;
        byte DMAData;
        bool DMADummyRead;
        bool DMAActive;
        bool DMCRealign;

        // The NTSC has this weird bug for DMC DMA where it might sometimes cause 
        // some registers to read twice and corrupt reads
        // This needs to be implemented eventually but for now it's alright
        //public bool reread_trigger;
        //public int do_the_reread_2002, do_the_reread_2007, do_the_reread_cont_1, do_the_reread_cont_2;
        //public int reread_opp_4016, reread_opp_4017;

        public Bus(NesCpu nes, PPU.PPU ppu, BizHawk.NES.APU apu, Rom.Rom rom) {
            CPU = nes;
            PPU = ppu;
            APU = apu;
            
            VRAM = new byte[2048];
            currentMapper = rom.Mapper;
            currentMapper.RegisterBus(this);
            PRG = rom.PrgRom;
            
            player1ButtonState = 0b00000000;
            player1ButtonLatch = 0;
            
            totalCycleCount = 0;
            
            blipBuffer.Clear();
            blipBuffer.SetRates(1789773, 44100);
        }

        public bool Clock() {
            totalCycleCount++;

            bool newFrame = PPU.Clock();
            if (newFrame) {
                IsNewFrame = newFrame;
            }

            currentMapper.SetProgramCounter(CPU.ProgramCounter);

            // PPU Runs at 3x the CPU speed, so only do something on every third clock
            if (totalCycleCount % 3 != 0)
                return IsNewFrame;
            
            if (DMAActive && APU.dmc_dma_countdown != 1 && !DMCRealign) {
                if (DMADummyRead) {
                    if (totalCycleCount % 2 == 1) {
                        DMADummyRead = false;
                    }
                } else {
                    if (totalCycleCount % 2 == 0) {
                        DMAData = MemRead((ushort)(DMAPage << 8 | DMAAddress));
                    } else {
                        PPU.OAM[DMAAddress] = DMAData;

                        DMAAddress++;

                        // Wrap around
                        if (DMAAddress == 0) {
                            DMAActive = false;
                            DMADummyRead = true;
                        }
                    }
                }
            }

            DMCRealign = false;

            if (APU.dmc_dma_countdown > 0) {
                if (APU.dmc_dma_countdown == 1) {
                    DMCRealign = true;
                }

                // By this point the cpu should be frozen, if it is not, then we are in a multi-write opcode, add another cycle delay
                if (!CPU.Ready && !CPU.FreezeExecution && (APU.dmc_dma_countdown == APU.DMC_RDY_check)) {
                    APU.dmc_dma_countdown += 2;
                }

                CPU.Ready = false;
                DMCDMAActive = true;
                APU.dmc_dma_countdown--;
                if (APU.dmc_dma_countdown == 0) {
                    APU.RunDMCFetch();

                    DMCDMAActive = false;
                    APU.dmc_dma_countdown = -1;

                    if ((APU.dmc.timer == 2) && (APU.dmc.out_bits_remaining == 0)) {
                        if (APU.dmc.sample_length != 0) {
                            APU.dmc.fill_glitch = true;
                        }
                    }

                    if ((APU.dmc.timer == 4) && (APU.dmc.out_bits_remaining == 0) && (APU.dmc.sample_length == 1)) {
                        APU.dmc.fill_glitch_2 = true;
                    }
                }
            }

            APU.RunOneFirst();


            if (CPU.CanInterrupt()) {
                // This timing is super broken and I need to fix it
                // Implement mapper 71 maybe?
                //if (APUIRQ) {
                //    CPU.IRQPending = true;
                //}
                if (currentMapper.GetIRQ()) {
                    CPU.IRQPending = true;
                }
            }

            if (CPUCyclesUntilNext <= 0) {
                //Console.WriteLine("ClockCpu");
                if (CPU.ShouldLog) {
                    // Console.WriteLine($"TRACE: {Trace.Log(CPU)}");
                }

                CPUCyclesUntilNext = CPU.ExecuteInstruction();

                if (!CPU.IRQPending && APUIRQ) {
                    APUIRQ = false;
                    currentMapper.SetIRQ(false);
                }
            }
            CPUCyclesUntilNext--;

            APU.RunOneLast();

            int sample = APU.EmitSample();
            if (sample != oldBlipSample) {
                blipBuffer.AddDelta(APU.sampleclock, sample - oldBlipSample);
                oldBlipSample = sample;
            }

            APU.sampleclock++;

            if (!CPU.Ready && !DMCDMAActive && !DMAActive) {
                CPU.Ready = true;
            }

            return IsNewFrame;
        }

        public void Reset() {
            APU.NESHardReset();
            currentMapper.Persist();

            DMAPage = 0x00;
            DMAAddress = 0x00;
            DMAData = 0x00;
            DMADummyRead = true;
            DMAActive = false;
        }

        public bool GetNmiStatus() {
            return PPU.GetInterrupt();
        }

        public bool PollDrawFrame() {
            return IsNewFrame;
        }

        public bool GetDrawFrame() {
            bool isFrame = IsNewFrame;
            IsNewFrame = false;
            return isFrame;
        }

        public byte ReadPrgRom(ushort address) {
            unsafe {
                fixed (byte* ptr = PRG) {
                    // Make sure the address lines up with the prg rom
                    address -= 0x8000;

                    // If the address is longer than the prg rom, mirror it down
                    if (PRG.Length == 0x4000 && address >= 0x4000) {
                        address = (ushort)(address % 0x4000);
                    }
                    return *(ptr + address);
                }
            }
        }

        public void Save(BinaryWriter writer) {
            writer.Write(totalCycleCount);
            writer.Write(VRAM);
            writer.Write(IsNewFrame);
            writer.Write(CPUCyclesUntilNext);
            writer.Write(DMAPage);
            writer.Write(DMAAddress);
            writer.Write(DMAData);
            writer.Write(DMADummyRead);
            writer.Write(DMAActive);
            currentMapper.Save(writer);
        }

        public void Load(BinaryReader reader) {
            totalCycleCount = reader.ReadUInt64();
            VRAM = reader.ReadBytes(VRAM.Length);
            IsNewFrame = reader.ReadBoolean();
            CPUCyclesUntilNext = reader.ReadInt32();
            DMAPage = reader.ReadByte();
            DMAAddress = reader.ReadByte();
            DMAData = reader.ReadByte();
            DMADummyRead = reader.ReadBoolean();
            DMAActive = reader.ReadBoolean();
            currentMapper.Load(reader);
        }

        public void DumpPPUMemory() {
            byte[] ChrRom = PPU.CHR;
            byte[] Vram = PPU.VRAM;
            byte[] palette = PPU.PALETTE;

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
