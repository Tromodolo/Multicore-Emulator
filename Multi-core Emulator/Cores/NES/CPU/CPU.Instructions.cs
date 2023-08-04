// ReSharper does not like the asm functions
// ReSharper disable InconsistentNaming

namespace NesEmu.CPU {
    public partial class NesCpu {
        public enum InterruptType {
            NMI,
            IRQ,
            BRK,
            RESET
        }

        static bool IsPageCross(ushort addr, ushort addr2) {
            return (addr & 0xFF00) != (addr2 & 0xFF00);
        }

        public (ushort programCounter, bool crossedBoundary) GetAbsoluteAddress(AddressingMode mode, ushort address) {
            switch (mode) {
                case AddressingMode.NoneAddressing: return (0, false);
                case AddressingMode.Accumulator: return (0, false);
                case AddressingMode.Immediate: return (address, false);
                case AddressingMode.Relative: {
                    ushort jump = Bus.MemRead(address);
                    address++;
                    ushort jumpAddr = (ushort)(address + jump);
                    return (jumpAddr, false);
                }
                case AddressingMode.ZeroPage: return (Bus.MemRead(address), false);
                case AddressingMode.ZeroPageX: {
                    byte pos = Bus.MemRead(address);
                    pos += RegisterX;
                    return (pos, false);
                }
                case AddressingMode.ZeroPageY: {
                    byte pos = Bus.MemRead(address);
                    pos += RegisterY;
                    return (pos, false);
                }
                case AddressingMode.Absolute: return (Bus.MemReadShort(address), false);
                case AddressingMode.AbsoluteX: {
                    ushort baseAddr = Bus.MemReadShort(address);
                    ushort addr = (ushort)(baseAddr + RegisterX);
                    return (addr, IsPageCross(baseAddr, addr));
                }
                case AddressingMode.AbsoluteY: {
                    ushort baseAddr = Bus.MemReadShort(address);
                    ushort addr = (ushort)(baseAddr + RegisterY);
                    return (addr, IsPageCross(baseAddr, addr));
                }
                case AddressingMode.Indirect: {
                    ushort addr = Bus.MemReadShort(address);
                    // 6502 bug mode with with page boundary:
                    //  if address $3000 contains $40, $30FF contains $80, and $3100 contains $50,
                    // the result of JMP ($30FF) will be a transfer of control to $4080 rather than $5080 as you intended
                    // i.e. the 6502 took the low byte of the address from $30FF and the high byte from $3000
                    if ((addr & 0x00ff) == 0x00ff) {
                        byte lo = Bus.MemRead(addr);
                        byte hi = Bus.MemRead((ushort)(addr & 0xff00));
                        return ((ushort)((hi << 8) | lo), false);
                    } else {
                        return (Bus.MemReadShort(addr), false);
                    }
                }
                case AddressingMode.IndirectX: {
                        byte baseAddr = Bus.MemRead(address);
                        byte pointer = (byte)(baseAddr + RegisterX);
                        byte lo = Bus.MemRead(pointer);
                        pointer++;
                    byte hi = Bus.MemRead(pointer);
                    return ((ushort)((hi << 8) | lo), false);
                }
                case AddressingMode.IndirectY: {
                    byte baseAddr = Bus.MemRead(address);

                    byte lo = Bus.MemRead(baseAddr);
                    baseAddr++;
                    byte hi = Bus.MemRead(baseAddr);

                    ushort derefBase = ((ushort)((ushort)(hi << 8) | lo));
                    ushort deref = (ushort)(derefBase + RegisterY);

                    bool isCross = IsPageCross(deref, derefBase);
                    return (deref, isCross);
                }
                default:
                    throw new NotImplementedException("Unknown mode received");
            }
        }

        (ushort programCounter, bool crossedBoundary) GetOperandAddress(AddressingMode mode) {
            return mode switch {
                AddressingMode.Immediate => (ProgramCounter, false),
                _ => GetAbsoluteAddress(mode, ProgramCounter),
            };
        }

        void UpdateZeroAndNegative(byte value) {
            if (value == 0) {
                SetStatusFlag(Flags.Zero);
            } else {
                ClearStatusFlag(Flags.Zero);
            }

            if (value >> 7 == 1) {
                SetStatusFlag(Flags.Negative);
            } else {
                ClearStatusFlag(Flags.Negative);
            }
        }

        void UpdateNegative(byte value) {
            if (value >> 7 == 1) {
                SetStatusFlag(Flags.Negative);
            } else {
                ClearStatusFlag(Flags.Negative);
            }
        }

        void AddToAcc(byte value) {
            var sum = (ushort)(Accumulator + value);
            if (Status.HasFlag(Flags.Carry)) {
                sum++;
            }

            if (sum > 0xff) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            var result = (byte)sum;
            // If value has wrapped, set overflow flag
            if (((value ^ result) & (result ^ Accumulator) & 0x80) != 0) {
                SetStatusFlag(Flags.Overflow);
            } else {
                ClearStatusFlag(Flags.Overflow);
            }

            Accumulator = result;
            UpdateZeroAndNegative(Accumulator);
        }
               

        void BRK() {
#if NESTEST
            stream.Write(Encoding.UTF8.GetBytes("==BRK==\r\n"));
#endif
            NumCyclesExecuted += 7;
            Interrupt(InterruptType.BRK);
        }

        void NMI() {
#if NESTEST
            stream.Write(Encoding.UTF8.GetBytes("==NMI==\r\n"));
#endif
            NumCyclesExecuted += 7;
            Interrupt(InterruptType.NMI);
        }

        void IRQ() {
            NumCyclesExecuted += 7;
            IRQPending = false;
            Interrupt(InterruptType.IRQ);
        }

        void Interrupt(InterruptType interrupt) {
            if (interrupt != InterruptType.RESET) {
                StackPushShort(ProgramCounter);

                // Pushes the CPU flags
                var flags = Status; // Sets bit 5 and 4
                flags |= (Flags)0b110000;
                if (interrupt != InterruptType.BRK)
                    flags &= ~Flags.Break; // Disable the bit 4 to the copy of the CPU flags

                StackPush((byte)flags);
                Status |= Flags.InterruptDisable;
            }

            switch (interrupt) {
                case InterruptType.NMI:
                    ProgramCounter = Bus.MemReadShort(0xfffa);
                    break;
                case InterruptType.RESET:
                    ProgramCounter = Bus.MemReadShort(0xfffc);
                    break;
                case InterruptType.IRQ:
                case InterruptType.BRK:
                    ProgramCounter = Bus.MemReadShort(0xfffe);
                    break;
                default:
                    throw new InvalidOperationException($"The interruption {interrupt} does not exist.");
            }
        }

        void HandleInstruction(OpCode op) {
            AddressingMode mode;
            ushort PCCopy;

            // IRQ and NMI should still happen during cpu being frozen, but they just won't be able to continue
            if (IRQPending && (Status & Flags.InterruptDisable) == 0) {
                IRQ();
                return;
            } else if (Bus.GetNmiStatus()) {
                NMI();
                return;
            } else {
                FreezeExecution = !Ready;
                if (FreezeExecution) {
                    return;
                }
                ProgramCounter++;
                mode = op.Mode;
                PCCopy = ProgramCounter;
            }

            switch (op.Name) {
                case Op.NOP:
                case Op._NOP:
                    (_, bool pageCross) = GetOperandAddress(mode);
                    if (pageCross) {
                        NumCyclesExecuted += 1;
                    }
                    break;
                case Op.BRK:
                    BRK();
                    return;
                case Op.ADC:
                    ADC(mode);
                    break;
                case Op.AND:
                    AND(mode);
                    break;
                case Op.ASL:
                    ASL(mode);
                    break;
                case Op.ASL_ACC:
                    ASL_ACC();
                    break;
                case Op.BEQ:
                    Branch(Status.HasFlag(Flags.Zero));
                    break;
                case Op.BCS:
                    Branch(Status.HasFlag(Flags.Carry));
                    break;
                case Op.BVS:
                    Branch(Status.HasFlag(Flags.Overflow));
                    break;
                case Op.BMI:
                    Branch(Status.HasFlag(Flags.Negative));
                    break;
                case Op.BNE:
                    Branch(!Status.HasFlag(Flags.Zero));
                    break;
                case Op.BCC:
                    Branch(!Status.HasFlag(Flags.Carry));
                    break;
                case Op.BVC:
                    Branch(!Status.HasFlag(Flags.Overflow));
                    break;
                case Op.BPL:
                    Branch(!Status.HasFlag(Flags.Negative));
                    break;
                case Op.BIT:
                    BIT(mode);
                    break;
                case Op.CMP:
                    Compare(mode, Accumulator, true);
                    break;
                case Op.CPX:
                    Compare(mode, RegisterX, false);
                    break;
                case Op.CPY:
                    Compare(mode, RegisterY, false);
                    break;
                case Op.DEC:
                    DEC(mode);
                    break;
                case Op.DEX:
                    DEX();
                    break;
                case Op.DEY:
                    DEY();
                    break;
                case Op.EOR:
                    EOR(mode);
                    break;
                case Op.INC:
                    INC(mode);
                    break;
                case Op.INX:
                    INX();
                    break;
                case Op.INY:
                    INY();
                    break;
                case Op.JMP:
                    JMP(mode);
                    break;
                case Op.JSR:
                    JSR();
                    break;
                case Op.LDA:
                    LDA(mode);
                    break;
                case Op.LDX:
                    LDX(mode);
                    break;
                case Op.LDY:
                    LDY(mode);
                    break;
                case Op.LSR:
                    LSR(mode);
                    break;
                case Op.LSR_ACC:
                    LSR_ACC();
                    break;
                case Op.ORA:
                    ORA(mode);
                    break;
                case Op.PHA:
                    PHA();
                    break;
                case Op.PLP:
                    PLP();
                    break;
                case Op.PHP:
                    PHP();
                    break;
                case Op.PLA:
                    PLA();
                    break;
                case Op.ROL:
                    ROL(mode);
                    break;
                case Op.ROL_ACC:
                    ROL_ACC();
                    break;
                case Op.ROR:
                    ROR(mode);
                    break;
                case Op.ROR_ACC:
                    ROR_ACC();
                    break;
                case Op.RTI:
                    RTI();
                    break;
                case Op.RTS:
                    RTS();
                    break;
                case Op.SBC:
                case Op._SBC:
                    SBC(mode);
                    break;
                case Op.STA:
                    STA(mode);
                    break;
                case Op.STX:
                    STX(mode);
                    break;
                case Op.STY:
                    STY(mode);
                    break;
                case Op.TAX:
                    TAX();
                    break;
                case Op.TAY:
                    TAY();
                    break;
                case Op.TSX:
                    TSX();
                    break;
                case Op.TXA:
                    TXA();
                    break;
                case Op.TXS:
                    TXS();
                    break;
                case Op.TYA:
                    TYA();
                    break;
                case Op.CLD:
                    ClearStatusFlag(Flags.DecimalMode);
                    break;
                case Op.CLI:
                    ClearStatusFlag(Flags.InterruptDisable);
                    break;
                case Op.CLV:
                    ClearStatusFlag(Flags.Overflow);
                    break;
                case Op.CLC:
                    ClearStatusFlag(Flags.Carry);
                    break;
                case Op.SEC:
                    SetStatusFlag(Flags.Carry);
                    break;
                case Op.SEI:
                    SetStatusFlag(Flags.InterruptDisable);
                    break;
                case Op.SED:
                    SetStatusFlag(Flags.DecimalMode);
                    break;

                //Unofficial
                case Op.AAC:
                    AAC(mode);
                    break;
                case Op._SAX:
                    AAX(mode);
                    break;
                case Op.ARR:
                    ARR(mode);
                    break;
                case Op.ASR:
                    ASR(mode);
                    break;
                case Op.ATX:
                    ATX(mode);
                    break;
                case Op.AXA:
                    AXA(mode);
                    break;
                case Op.AXS:
                    AXS(mode);
                    break;
                case Op._DCP:
                    DCP(mode);
                    break;
                case Op._ISB:
                    ISC(mode);
                    break;
                case Op.LAR:
                    LAR(mode);
                    break;
                case Op._LAX:
                    LAX(mode);
                    break;
                case Op._RLA:
                    RLA(mode);
                    break;
                case Op._RRA:
                    RRA(mode);
                    break;
                case Op._SLO:
                    SLO(mode);
                    break;
                case Op._SRE:
                    SRE(mode);
                    break;
                case Op.SXA:
                    SXA(mode);
                    break;
                case Op.SYA:
                    SYA(mode);
                    break;
                default:
                    break;
            }

            NumCyclesExecuted += op.NumCycles;

            if (ProgramCounter == PCCopy) {
                ProgramCounter += (ushort)(op.NumBytes - 1);
            }
        }

        /*
         * Instructions start here
         */
        void ADC(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            AddToAcc(value);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void AND(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            Accumulator &= value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void ASL(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            if (value >> 7 == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            var newVal = (byte)(value << 1);
            Bus.MemWrite(address, newVal);
            UpdateZeroAndNegative(newVal);
        }

        void ASL_ACC() {
            if (Accumulator >> 7 == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            Accumulator = (byte)(Accumulator << 1);
            UpdateZeroAndNegative(Accumulator);
        }

        void Branch(bool condition) {
            if (!condition)
                return;
            
            NumCyclesExecuted += 1;

            var jump = (sbyte)Bus.MemRead(ProgramCounter);
            var jumpAddr = (ushort)(ProgramCounter + jump + 1);

            if ((((ushort)(ProgramCounter + 1)) & 0xff00) != (jumpAddr & 0xff00)) {
                NumCyclesExecuted += 1;
            }
            ProgramCounter = jumpAddr;
        }

        void BIT(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte mask = Bus.MemRead(address);

            if ((mask & Accumulator) == 0) {
                SetStatusFlag(Flags.Zero);
            } else {
                ClearStatusFlag(Flags.Zero);
            }

            if ((mask & (1 << 6)) > 0) {
                SetStatusFlag(Flags.Overflow);
            } else {
                ClearStatusFlag(Flags.Overflow);
            }

            if ((mask & (1 << 7)) > 0) {
                SetStatusFlag(Flags.Negative);
            } else {
                ClearStatusFlag(Flags.Negative);
            }
        }

        void Compare(AddressingMode mode, byte value, bool pageCrossAvailable) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte memVal = Bus.MemRead(address);
            if (memVal <= value) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            UpdateZeroAndNegative((byte)(value - memVal));

            if (pageCross && pageCrossAvailable) {
                NumCyclesExecuted += 1;
            }
        }

        void DEC(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            value--;
            Bus.MemWrite(address, value);
            UpdateZeroAndNegative(value);
        }

        void DEX() {
            RegisterX--;
            UpdateZeroAndNegative(RegisterX);
        }

        void DEY() {
            RegisterY--;
            UpdateZeroAndNegative(RegisterY);
        }

        void EOR(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            Accumulator ^= value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        byte INC(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            value++;
            Bus.MemWrite(address, value);
            UpdateZeroAndNegative(value);
            return value;
        }

        void INX() {
            RegisterX++;
            UpdateZeroAndNegative(RegisterX);
        }

        void INY() {
            RegisterY++;
            UpdateZeroAndNegative(RegisterY);
        }

        void JMP(AddressingMode mode) {
            if (mode == AddressingMode.Indirect) {
                jmp_indirect();
            } else {
                ushort address = Bus.MemReadShort(ProgramCounter);
                ProgramCounter = address;
            }
        }

        void jmp_indirect() {
            ushort address = Bus.MemReadShort(ProgramCounter);
            NumCyclesExecuted += 2;
            // 6502 bug mode with with page boundary:
            //  if address $3000 contains $40, $30FF contains $80, and $3100 contains $50,
            // the result of JMP ($30FF) will be a transfer of control to $4080 rather than $5080 as you intended
            // i.e. the 6502 took the low byte of the address from $30FF and the high byte from $3000
            ushort indirectRef;
            if ((address & 0x00ff) == 0x00ff) {
                byte loAddr = Bus.MemRead(address);
                byte hiAddr = Bus.MemRead((ushort)(address & 0xff00));
                indirectRef = (ushort)((hiAddr << 8) | loAddr);
            } else {
                indirectRef = Bus.MemReadShort(address);
            }
            ProgramCounter = indirectRef;
        }

        void JSR() {
            StackPushShort((ushort)(ProgramCounter + 1));
            ushort addr = Bus.MemReadShort(ProgramCounter);
            ProgramCounter = addr;
        }

        void LDA(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            Accumulator = value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void LDX(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            RegisterX = value;
            UpdateZeroAndNegative(RegisterX);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void LDY(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            RegisterY = value;
            UpdateZeroAndNegative(RegisterY);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void LSR(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            if ((value & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            var newVal = (byte)(value >> 1);
            Bus.MemWrite(address, newVal);
            UpdateZeroAndNegative(newVal);
        }

        void LSR_ACC() {
            if ((Accumulator & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            Accumulator = (byte)(Accumulator >> 1);
            UpdateZeroAndNegative(Accumulator);
        }

        void ORA(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            Accumulator |= value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void PHA() {
            StackPush(Accumulator);
        }

        void PHP() {
            var flags = Status;
            flags |= Flags.Break;
            flags |= Flags.Break2;
            StackPush((byte)flags);
        }

        void PLA() {
            Accumulator = StackPop();
            UpdateZeroAndNegative(Accumulator);
        }

        void PLP() {
            byte statusBits = StackPop();
            Status = (Flags)statusBits;
            ClearStatusFlag(Flags.Break);
            SetStatusFlag(Flags.Break2);
        }

        void ROL(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            bool oldCarry = Status.HasFlag(Flags.Carry);

            if ((value >> 7) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            value = (byte)(value << 1);
            if (oldCarry) {
                value |= 1;
            }
            Bus.MemWrite(address, value);
            UpdateNegative(value);

        }

        void ROL_ACC() {
            byte value = Accumulator;
            bool oldCarry = Status.HasFlag(Flags.Carry);

            if ((value >> 7) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            value = (byte)(value << 1);
            if (oldCarry) {
                value |= 1;
            }
            Accumulator = value;
            UpdateZeroAndNegative(value);
        }
        void ROR(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            bool oldCarry = Status.HasFlag(Flags.Carry);

            if ((value & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            value = (byte)(value >> 1);
            if (oldCarry) {
                value |= 0b10000000;
            }
            Bus.MemWrite(address, value);
            UpdateNegative(value);

        }

        void ROR_ACC() {
            byte value = Accumulator;
            bool oldCarry = Status.HasFlag(Flags.Carry);

            if ((value & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            value = (byte)(value >> 1);
            if (oldCarry) {
                value |= 0b10000000;
            }
            Accumulator = value;
            UpdateZeroAndNegative(Accumulator);
        }

        void RTI() {
            byte statusBits = StackPop();
            Status = (Flags)statusBits;
            ClearStatusFlag(Flags.Break);
            SetStatusFlag(Flags.Break2);
            ProgramCounter = StackPopShort();
        }

        void RTS() {
            var addr = (ushort)(StackPopShort() + 1);
            ProgramCounter = addr;
        }

        void SBC(AddressingMode mode) {
            (ushort address, bool pageCross) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            AddToAcc((byte)(byte.MaxValue - value));

            if (pageCross) {
                NumCyclesExecuted += 1;
            }
        }

        void STA(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            Bus.MemWrite(address, Accumulator);
        }

        void STX(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            Bus.MemWrite(address, RegisterX);
        }

        void STY(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            Bus.MemWrite(address, RegisterY);
        }

        void TAX() {
            RegisterX = Accumulator;
            UpdateZeroAndNegative(RegisterX);
        }

        void TAY() {
            RegisterY = Accumulator;
            UpdateZeroAndNegative(RegisterY);
        }

        void TSX() {
            RegisterX = StackPointer;
            UpdateZeroAndNegative(RegisterX);
        }

        void TXA() {
            Accumulator = RegisterX;
            UpdateZeroAndNegative(Accumulator);
        }

        void TXS() {
            StackPointer = RegisterX;
        }

        void TYA() {
            Accumulator = RegisterY;
            UpdateZeroAndNegative(Accumulator);
        }

        /* 
         * Here be ILLEGAL instructions
         * 👮🏻‍
         */

        void AAC(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator &= value;
            UpdateZeroAndNegative(value);

            if (Status.HasFlag(Flags.Negative)) {
                SetStatusFlag(Flags.Carry);
            }
        }

        void AAX(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            var value = (byte)(RegisterX & Accumulator);
            Bus.MemWrite(address, value);
            //UpdateZeroAndNegative(value);
        }

        void ARR(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            Accumulator &= value;
            ROR_ACC();
            UpdateZeroAndNegative(Accumulator);

            byte res = Accumulator;
            bool six = (res & 0b01000000) > 0;
            bool five = (res & 0b00100000) > 0;

            if ((six && five) || (six && !five)) {
                SetStatusFlag(Flags.Carry);
                ClearStatusFlag(Flags.Overflow);
            } else if ((!six && !five) || (!six && five)) {
                SetStatusFlag(Flags.Overflow);
                ClearStatusFlag(Flags.Carry);
            }
        }

        void ASR(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator &= value;
            ROR_ACC();
        }

        void ATX(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator &= value;
            RegisterX = Accumulator;
            UpdateZeroAndNegative(RegisterX);
        }

        void AXA(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte result = (byte)(RegisterX & Accumulator);
            result &= 0b111;
            Bus.MemWrite(address, result);
        }

        void AXS(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            RegisterX &= Accumulator;
            RegisterX -= value;
            if ((value >> 7) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            UpdateZeroAndNegative(RegisterX);
        }

        void DCP(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            value -= 1;
            Bus.MemWrite(address, value);

            if (value <= Accumulator) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            UpdateZeroAndNegative((byte)(Accumulator - value));
        }

        void ISC(AddressingMode mode) {
            byte value = INC(mode);
            AddToAcc((byte)(byte.MaxValue - value));
        }

        //fn isc(&mut self, mode: &AddressingMode) {
        //    let value = self.inc(&mode);
        //    self.add_to_acc((value as i8).wrapping_neg().wrapping_sub(1) as u8);
        //}

        void LAR(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            byte res = (byte)(value & StackPointer);
            Accumulator = res;
            RegisterX = res;
            StackPointer = res;
            UpdateZeroAndNegative(res);
        }

        void LAX(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator = value;
            RegisterX = value;
            UpdateZeroAndNegative(value);
        }

        void RLA(AddressingMode mode) {
            ROL(mode);

            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator &= value;
            UpdateZeroAndNegative(Accumulator);
        }

        void RRA(AddressingMode mode) {
            ROR(mode);
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);
            AddToAcc(value);
        }

        void SLO(AddressingMode mode) {
            ASL(mode);

            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator |= value;
            UpdateZeroAndNegative(Accumulator);
        }

        void SRE(AddressingMode mode) {
            LSR(mode);

            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            Accumulator ^= value;
            UpdateZeroAndNegative(Accumulator);
        }

        void SXA(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            var result = (byte)(RegisterX & (value & 0b11110000 + 1));
            Bus.MemWrite(address, result);
        }

        void SYA(AddressingMode mode) {
            (ushort address, _) = GetOperandAddress(mode);
            byte value = Bus.MemRead(address);

            var result = (byte)(RegisterY & (value & 0b11110000 + 1));
            Bus.MemWrite(address, result);
        }
    }
}
