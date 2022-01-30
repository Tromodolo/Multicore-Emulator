using NesEmu.Bus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {

    [Flags]
    public enum Flags {
        Carry = 1,
        Zero = 1 << 1,
        InterruptDisable = 1 << 2,
        DecimalMode = 1 << 3,
        Break = 1 << 4,
        Break2 = 1 << 5,
        Overflow = 1 << 6,
        Negative = 1 << 7,
    }

    public partial class NesCpu {
        const ushort StackStart = 0x0100;
        const byte StackReset = 0xfd;

        public byte Accumulator { get; private set; }
        public byte RegisterX { get; private set; }
        public byte RegisterY { get; private set; }
        public ushort ProgramCounter { get; private set; }
        public byte StackPointer { get; private set; }
        public bool Running { get; private set; }
        public Flags Status { get; private set; }
        public UInt64 TotalCycles { get; set; }
        public Bus.Bus Bus { get; set; }

        public NesCpu(Rom.Rom rom) {
            Bus = new Bus.Bus(rom);
            Reset();
        }

        public void Reset() {
            Accumulator = 0;
            RegisterX = 0;
            RegisterY = 0;
            StackPointer = StackReset;
            SetStatusFlag(Flags.InterruptDisable);
            SetStatusFlag(Flags.Break2);
#if NESTEST
            ProgramCounter = 0xC000;
#else
            ProgramCounter = MemReadShort(0xFFFC);
#endif
            Running = true;
        }

#if NESTEST
        FileStream stream = File.Open("nestest-log.log", FileMode.OpenOrCreate);
#endif

        public void RunScanline(Func<int, bool> renderScanline) {
            while (true) {
#if NESTEST
                if (ProgramCounter == 0x0001) {
                    stream.Flush();
                }
#endif

                if ((Bus.UnprocessedCycles * 3) + Bus.PPU.CurrentCycle >= 341) {
                    Bus.FastForwardPPU();
                    break;
                }
#if NESTEST
                var trace = Trace.Log(this);
                stream.Write(Encoding.UTF8.GetBytes(trace));
                stream.Flush();

#endif
                ExecuteInstruction();
            }
            renderScanline.Invoke(Bus.PPU.CurrentScanline - 1);
        }

        public void ExecuteInstruction() {
            var op = GetOpFromByte(MemRead(ProgramCounter));
            HandleInstruction(op);
        }

        void SetStatusFlag(Flags flag) {
            Status |= flag;
        }

        void ClearStatusFlag(Flags flag) {
            Status &= ~flag;
        }
    }
}
