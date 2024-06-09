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
        public Flags Status;
        public Bus.Bus Bus;
        
        bool Running;
        ulong TotalCycles;
        byte NumCyclesExecuted;

        public bool ShouldLog = false;
        int BreakpointStart = -1;
        int BreakpointEnd = -1;

        /// <summary>
        /// If RDY flag is set low, it means the CPU should just stop execution, but still allow interrupts
        /// <br>NMI should for example switch to the interrupt vector, but not do anything more</br>
        /// </summary>
        public bool Ready;
        public bool FreezeExecution;
        public bool IRQPending;

        public void RegisterBus(Bus.Bus bus) {
            Bus = bus;
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
            ProgramCounter = Bus.MemReadShort(0xFFFC);
#endif
            Running = true;
            Ready = true;
        }

        public byte ExecuteInstruction() {
            FreezeExecution = false;

            if (ProgramCounter == BreakpointStart) {
                ShouldLog = true;
            }

            if (ProgramCounter == BreakpointEnd) {
                ShouldLog = false;
            }

            var op = OpCodeList.OpCodes[Bus.MemRead(ProgramCounter)];
            NumCyclesExecuted = 0;
            HandleInstruction(op);
            return NumCyclesExecuted;
        }

        public bool CanInterrupt() {
            return (Status & Flags.InterruptDisable) == 0;
        }

        void SetStatusFlag(Flags flag) {
            Status |= flag;
        }

        void ClearStatusFlag(Flags flag) {
            Status &= ~flag;
        }

        public void Save(BinaryWriter writer) {
            writer.Write(Accumulator);
            writer.Write(RegisterX);
            writer.Write(RegisterY);
            writer.Write(ProgramCounter);
            writer.Write(StackPointer);
            writer.Write(Running);
            writer.Write((int)Status);
            writer.Write(TotalCycles);
            writer.Write(NumCyclesExecuted);
            writer.Write(Ready);
            writer.Write(FreezeExecution);
            writer.Write(IRQPending);
        }

        public void Load(BinaryReader reader) {
            Accumulator = reader.ReadByte();
            RegisterX = reader.ReadByte();
            RegisterY = reader.ReadByte();
            ProgramCounter = reader.ReadUInt16();
            StackPointer = reader.ReadByte();
            Running = reader.ReadBoolean();
            Status = (Flags)reader.ReadInt32();
            TotalCycles = reader.ReadUInt64();
            NumCyclesExecuted = reader.ReadByte();
            Ready = reader.ReadBoolean();
            FreezeExecution = reader.ReadBoolean();
            IRQPending = reader.ReadBoolean();
        }
    }
}
