using MultiCoreEmulator.Utility;
using NesEmu.CPU;
using NesEmu.Mapper;

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
        public readonly IMapper currentMapper;
        
        public bool APUIRQ;
        public bool DMCDMAActive;

        NesCpu CPU;
        PPU.PPU PPU;
        BizHawk.NES.APU APU;
        public CircleBuffer<short> AudioBuffer;

        byte[] VRAM;

        ulong totalCycleCount;
        int CPUCyclesUntilNextInstruction;

        const int CLOCKS_PER_SAMPLE = 1789773 / 44100; 
        int clocksSinceLastSample;
        
        int apuSample;
        
        public int SamplesCollected;
        public bool PendingFrame;

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
            
            player1ButtonState = 0b00000000;
            player1ButtonLatch = 0;
            
            totalCycleCount = 0;

            AudioBuffer = new CircleBuffer<short>(735 * 3);
        }

        public void Clock() {
            totalCycleCount++;

            if (PPU.Clock()) {
                PendingFrame = true;
            }

            currentMapper.SetProgramCounter(CPU.ProgramCounter);

            // PPU Runs at 3x the CPU speed, so only do something on every third clock
            if (totalCycleCount % 3 != 0)
                return;
            
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

            if (CPUCyclesUntilNextInstruction <= 0) {
                //Console.WriteLine("ClockCpu");
                if (CPU.ShouldLog) {
                    // Console.WriteLine($"TRACE: {Trace.Log(CPU)}");
                }

                CPUCyclesUntilNextInstruction = CPU.ExecuteInstruction();

                if (!CPU.IRQPending && APUIRQ) {
                    APUIRQ = false;
                    currentMapper.SetIRQ(false);
                }
            }
            CPUCyclesUntilNextInstruction--;

            APU.RunOneLast();

            if (clocksSinceLastSample >= CLOCKS_PER_SAMPLE) {
                apuSample = APU.EmitSample();
                AudioBuffer.AddBack((short)apuSample);
                clocksSinceLastSample %= CLOCKS_PER_SAMPLE;
                SamplesCollected++;
            } 
            clocksSinceLastSample++;

            if (!CPU.Ready && !DMCDMAActive && !DMAActive) {
                CPU.Ready = true;
            }
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

        public void Save(BinaryWriter writer) {
            writer.Write(totalCycleCount);
            writer.Write(VRAM);
            writer.Write(PendingFrame);
            writer.Write(CPUCyclesUntilNextInstruction);
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
            PendingFrame = reader.ReadBoolean();
            CPUCyclesUntilNextInstruction = reader.ReadInt32();
            DMAPage = reader.ReadByte();
            DMAAddress = reader.ReadByte();
            DMAData = reader.ReadByte();
            DMADummyRead = reader.ReadBoolean();
            DMAActive = reader.ReadBoolean();
            currentMapper.Load(reader);
        }
    }
}
