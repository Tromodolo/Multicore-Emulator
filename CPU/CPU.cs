using NesEmu.Bus;
using System;
using System.Collections.Generic;
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

    public partial class CPU {
        const ushort StackStart = 0x0100;
        const byte StackReset = 0xfd;

        public byte Accumulator { get; private set; }
        public byte RegisterX { get; private set; }
        public byte RegisterY { get; private set; }
        public ushort ProgramCounter { get; private set; }
        public byte StackPointer { get; private set; }
        public Bus.Bus Bus { get; private set; }
        public bool Running { get; private set; }
        public Flags Status { get; private set; }
        public UInt64 InstructionCount { get; private set; }

        public CPU(Rom.Rom rom) {
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

        public void ExecuteNextInstruction() {
#if NESTEST
            if (ProgramCounter == 0xC66E) {
                Running = false;
            }
#endif

            var op = GetOpFromByte(MemRead(ProgramCounter));
            ProgramCounter++;
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
