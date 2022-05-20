using NesEmu.Bus;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

        public byte Accumulator;
        public byte RegisterX;
        public byte RegisterY;
        public ushort ProgramCounter;
        public byte StackPointer;
        public bool Running;
        public Flags Status;
        public UInt64 TotalCycles;
        public Bus.Bus Bus;

        public NesCpu(Rom.Rom rom) {
            Bus = new Bus.Bus(this, rom);
            Reset();
            Bus.Reset();
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

        public byte RunScanline() {
            var currentScanline = Bus.PPU.CurrentScanline;
            while (currentScanline == Bus.PPU.CurrentScanline) {
                ExecuteInstruction();
            }
            return (byte)(Bus.PPU.CurrentScanline - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
