using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {
    public partial class CPU {
        OpCode GetOpFromByte(byte value) {
            return OpCodeList.OpCodes.FirstOrDefault(x => x.Code == value);
        }

        bool IsPageCross(ushort addr, ushort addr2) {
            return (addr & 0xFF00) != (addr2 & 0xFF00);
        }

        public (ushort programCounter, bool crossedBoundary) GetAbsoluteAdddress(AddressingMode mode, ushort address) {
            switch (mode) {
                case AddressingMode.NoneAddressing: return (0, false);
                case AddressingMode.Accumulator: return (0, false);
                case AddressingMode.Immediate: return (address, false);
                case AddressingMode.Relative: {
                    ushort jump = MemRead(address);
                    address++;
                    ushort jumpAddr = (ushort)(address + jump);
                    return (jumpAddr, false);
                }
                case AddressingMode.ZeroPage: return (MemRead(address), false);
                case AddressingMode.ZeroPageX: {
                    byte pos = MemRead(address);
                    pos += RegisterX;
                    return (pos, false);
                }
                case AddressingMode.ZeroPageY: {
                    byte pos = MemRead(address);
                    pos += RegisterY;
                    return (pos, false);
                }
                case AddressingMode.Absolute: return (MemReadShort(address), false);
                case AddressingMode.AbsoluteX: {
                    ushort baseAddr = MemReadShort(address);
                    ushort addr = (ushort)(baseAddr + RegisterX);
                    return (addr, IsPageCross(baseAddr, addr));
                }
                case AddressingMode.AbsoluteY: {
                    ushort baseAddr = MemReadShort(address);
                    ushort addr = (ushort)(baseAddr + RegisterY);
                    return (addr, IsPageCross(baseAddr, addr));
                }
                case AddressingMode.Indirect: {
                    ushort addr = MemReadShort(address);
                    // 6502 bug mode with with page boundary:
                    //  if address $3000 contains $40, $30FF contains $80, and $3100 contains $50,
                    // the result of JMP ($30FF) will be a transfer of control to $4080 rather than $5080 as you intended
                    // i.e. the 6502 took the low byte of the address from $30FF and the high byte from $3000
                    if ((addr & 0x00ff) == 0x00ff) {
                        byte lo = MemRead(addr);
                        byte hi = MemRead((ushort)(addr & 0xff00));
                        return ((ushort)((hi << 8) | lo), false);
                    } else {
                        return (MemReadShort(addr), false);
                    }
                }
                case AddressingMode.IndirectX: {
                    byte baseAddr = MemRead(address);
                    byte pointer = (byte)(baseAddr + RegisterX);
                    byte lo = MemRead(pointer);
                    pointer++;
                    byte hi = MemRead(pointer);
                    return ((ushort)((hi << 8) | lo), false);
                }
                case AddressingMode.IndirectY: {
                    byte baseAddr = MemRead(address);
                    byte lo = MemRead(baseAddr);
                    baseAddr++;
                    byte hi = MemRead(baseAddr);
                    ushort derefBase = ((ushort)((ushort)(hi << 8) | lo));
                    ushort deref = (ushort)(derefBase + RegisterY);
                    return (deref, IsPageCross(baseAddr, deref));
                }
                default:
                    throw new NotImplementedException("Unknown mode received");
            }
        }

        public (ushort programCounter, bool crossedBoundary) GetOperandAddress(AddressingMode mode) {
            return mode switch {
                AddressingMode.Immediate => (ProgramCounter, false),
                _ => GetAbsoluteAdddress(mode, ProgramCounter),
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
            ushort sum = (ushort)(Accumulator + value);
            if (Status.HasFlag(Flags.Carry)) {
                sum++;
            }

            if (sum > 0xff) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            byte result = (byte)sum;
            // Fuck knows what this means
            if (((value ^ result) & (result ^ Accumulator) & 0x80) != 0) {
                SetStatusFlag(Flags.Overflow);
            } else {
                ClearStatusFlag(Flags.Overflow);
            }

            Accumulator = result;
            UpdateZeroAndNegative(Accumulator);
        }

        void NmiInterrupt() {
            StackPushShort(StackPointer);

            var flags = Status;
            flags |= Flags.Break;
            flags |= Flags.Break2;
            StackPush((byte)flags);

            SetStatusFlag(Flags.InterruptDisable);

            Bus.TickPPUCycles(2);
            ProgramCounter = MemReadShort(0xfffa);
        }

        void BrkInterrupt() {
            StackPushShort(StackPointer);

            var flags = Status;
            flags |= Flags.Break;
            flags |= Flags.Break2;
            StackPush((byte)flags);

            Bus.TickPPUCycles(2);
            ProgramCounter = MemReadShort(0xfffa);
        }

        void HandleInstruction(OpCode op) {
            var mode = op.Mode;
            ushort PCCopy = ProgramCounter;

            InstructionCount++;

            if (Bus.GetNmiStatus()) {
                NmiInterrupt();
            }


            switch (op.Name) {
                case "NOP":
                case "*NOP":
                    break;
                case "BRK":
                    BrkInterrupt();
                    break;
                case "ADC":
                    adc(mode);
                    break;
                case "AND":
                    and(mode);
                    break;
                case "ASL":
                    asl(mode);
                    break;
                case "ASL_ACC":
                    asl_acc();
                    break;
                case "BEQ":
                    branch(Status.HasFlag(Flags.Zero));
                    break;
                case "BCS":
                    branch(Status.HasFlag(Flags.Carry));
                    break;
                case "BVS":
                    branch(Status.HasFlag(Flags.Overflow));
                    break;
                case "BMI":
                    branch(Status.HasFlag(Flags.Negative));
                    break;
                case "BNE":
                    branch(!Status.HasFlag(Flags.Zero));
                    break;
                case "BCC":
                    branch(!Status.HasFlag(Flags.Carry));
                    break;
                case "BVC":
                    branch(!Status.HasFlag(Flags.Overflow));
                    break;
                case "BPL":
                    branch(!Status.HasFlag(Flags.Negative));
                    break;
                case "BIT":
                    bit(mode);
                    break;
                case "CMP":
                    compare(mode, Accumulator);
                    break;
                case "CPX":
                    compare(mode, RegisterX);
                    break;
                case "CPY":
                    compare(mode, RegisterY);
                    break;
                case "DEC":
                    dec(mode);
                    break;
                case "DEX":
                    dex();
                    break;
                case "DEY":
                    dey();
                    break;
                case "EOR":
                    eor(mode);
                    break;
                case "INC":
                    inc(mode);
                    break;
                case "INX":
                    inx();
                    break;
                case "INY":
                    iny();
                    break;
                case "JMP":
                    jmp(mode);
                    break;
                case "JSR":
                    jsr();
                    break;
                case "LDA":
                    lda(mode);
                    break;
                case "LDX":
                    ldx(mode);
                    break;
                case "LDY":
                    ldy(mode);
                    break;
                case "LSR":
                    lsr(mode);
                    break;
                case "LSR_ACC":
                    lsr_acc();
                    break;
                case "ORA":
                    ora(mode);
                    break;
                case "PHA":
                    pha();
                    break;
                case "PLP":
                    plp();
                    break;
                case "PHP":
                    php();
                    break;
                case "PLA":
                    pla();
                    break;
                case "ROL":
                    rol(mode);
                    break;
                case "ROL_ACC":
                    rol_acc();
                    break;
                case "ROR":
                    ror(mode);
                    break;
                case "ROR_ACC":
                    ror_acc();
                    break;
                case "RTI":
                    rti();
                    break;
                case "RTS":
                    rts();
                    break;
                case "SBC":
                case "*SBC":
                    sbc(mode);
                    break;
                case "STA":
                    sta(mode);
                    break;
                case "STX":
                    stx(mode);
                    break;
                case "STY":
                    sty(mode);
                    break;
                case "TAX":
                    tax();
                    break;
                case "TAY":
                    tay();
                    break;
                case "TSX":
                    tsx();
                    break;
                case "TXA":
                    txa();
                    break;
                case "TXS":
                    txs();
                    break;
                case "TYA":
                    tya();
                    break;
                case "CLD":
                    ClearStatusFlag(Flags.DecimalMode);
                    break;
                case "CLI":
                    ClearStatusFlag(Flags.InterruptDisable);
                    break;
                case "CLV":
                    ClearStatusFlag(Flags.Overflow);
                    break;
                case "CLC":
                    ClearStatusFlag(Flags.Carry);
                    break;
                case "SEC":
                    SetStatusFlag(Flags.Carry);
                    break;
                case "SEI":
                    SetStatusFlag(Flags.InterruptDisable);
                    break;
                case "SED":
                    SetStatusFlag(Flags.DecimalMode);
                    break;

                //Unofficial
                case "AAC":
                    aac(mode);
                    break;
                case "*SAX":
                    aax(mode);
                    break;
                case "ARR":
                    arr(mode);
                    break;
                case "ASR":
                    asr(mode);
                    break;
                case "ATX":
                    atx(mode);
                    break;
                case "AXA":
                    axa(mode);
                    break;
                case "AXS":
                    axs(mode);
                    break;
                case "*DCP":
                    dcp(mode);
                    break;
                case "*ISB":
                    isc(mode);
                    break;
                case "LAR":
                    lar(mode);
                    break;
                case "*LAX":
                    lax(mode);
                    break;
                case "*RLA":
                    rla(mode);
                    break;
                case "*RRA":
                    rra(mode);
                    break;
                case "*SLO":
                    slo(mode);
                    break;
                case "*SRE":
                    sre(mode);
                    break;
                case "SXA":
                    sxa(mode);
                    break;
                case "SYA":
                    sya(mode);
                    break;

            }

            Bus.TickPPUCycles(op.NumCycles);

            if (ProgramCounter == PCCopy) {
                ProgramCounter += (ushort)(op.NumBytes - 1);
            }
        }

        /*
         * Instructions start here
         */
        void adc(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            AddToAcc(value);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void and(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            Accumulator &= value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void asl(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            if (value >> 7 == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            var newVal = (byte)(value << 1);
            MemWrite(address, newVal);
            UpdateZeroAndNegative(newVal);
        }

        void asl_acc() {
            if (Accumulator >> 7 == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            Accumulator = (byte)(Accumulator << 1);
            UpdateZeroAndNegative(Accumulator);
        }

        void branch(bool condition) {
            if (condition) {
                Bus.TickPPUCycles(1);

                sbyte jump = (sbyte)MemRead(ProgramCounter);
                var jumpAddr = (ushort)(ProgramCounter + jump + 1);

                if (((ProgramCounter + 1) & 0xff00) != (jumpAddr & 0xff00)) {
                    Bus.TickPPUCycles(1);
                }
                ProgramCounter = jumpAddr;
            }
        }

        void bit(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var mask = MemRead(address);

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

        void compare(AddressingMode mode, byte value) {
            var (address, pageCross) = GetOperandAddress(mode);
            var memVal = MemRead(address);
            if (memVal <= value) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            UpdateZeroAndNegative((byte)(value - memVal));

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void dec(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            value--;
            MemWrite(address, value);
            UpdateZeroAndNegative(value);
        }

        void dex() {
            RegisterX--;
            UpdateZeroAndNegative(RegisterX);
        }

        void dey() {
            RegisterY--;
            UpdateZeroAndNegative(RegisterY);
        }

        void eor(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            Accumulator ^= value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        byte inc(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            value++;
            MemWrite(address, value);
            UpdateZeroAndNegative(value);
            return value;
        }

        void inx() {
            RegisterX++;
            UpdateZeroAndNegative(RegisterX);
        }

        void iny() {
            RegisterY++;
            UpdateZeroAndNegative(RegisterY);
        }

        void jmp(AddressingMode mode) {
            if (mode == AddressingMode.Indirect) {
                jmp_indirect();
            } else {
                var address = MemReadShort(ProgramCounter);
                ProgramCounter = address;
            }
        }

        void jmp_indirect() {
            var address = MemReadShort(ProgramCounter);
            // 6502 bug mode with with page boundary:
            //  if address $3000 contains $40, $30FF contains $80, and $3100 contains $50,
            // the result of JMP ($30FF) will be a transfer of control to $4080 rather than $5080 as you intended
            // i.e. the 6502 took the low byte of the address from $30FF and the high byte from $3000
            ushort indirectRef;
            if ((address & 0x00ff) == 0x00ff) {
                var loAddr = MemRead(address);
                var hiAddr = MemRead((ushort)(address & 0xff00));
                indirectRef = (ushort)((hiAddr << 8) | loAddr);
            } else {
                indirectRef = MemReadShort(address);
            }
            ProgramCounter = indirectRef;
        }

        void jsr() {
            StackPushShort((ushort)(ProgramCounter + 2 - 1));
            var addr = MemReadShort(ProgramCounter);
            ProgramCounter = addr;
        }

        void lda(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            Accumulator = value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void ldx(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            RegisterX = value;
            UpdateZeroAndNegative(RegisterX);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void ldy(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            RegisterY = value;
            UpdateZeroAndNegative(RegisterY);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void lsr(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            if ((value & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            var newVal = (byte)(value >> 1);
            MemWrite(address, newVal);
            UpdateZeroAndNegative(newVal);
        }

        void lsr_acc() {
            if ((Accumulator & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }

            Accumulator = (byte)(Accumulator >> 1);
            UpdateZeroAndNegative(Accumulator);
        }

        void ora(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            Accumulator |= value;
            UpdateZeroAndNegative(Accumulator);

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void pha() {
            StackPush(Accumulator);
        }

        void php() {
            var flags = Status;
            flags |= Flags.Break;
            flags |= Flags.Break2;
            StackPush((byte)flags);
        }

        void pla() {
            Accumulator = StackPop();
            UpdateZeroAndNegative(Accumulator);
        }

        void plp() {
            byte statusBits = StackPop();
            Status = (Flags)statusBits;
            ClearStatusFlag(Flags.Break);
            SetStatusFlag(Flags.Break2);
        }

        void rol(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            var oldCarry = Status.HasFlag(Flags.Carry);

            if ((value >> 7) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            value = (byte)(value << 1);
            if (oldCarry) {
                value |= 1;
            }
            MemWrite(address, value);
            UpdateNegative(value);

        }

        void rol_acc() {
            var value = Accumulator;
            var oldCarry = Status.HasFlag(Flags.Carry);

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
        void ror(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            var oldCarry = Status.HasFlag(Flags.Carry);

            if ((value & 1) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            value = (byte)(value >> 1);
            if (oldCarry) {
                value |= 0b10000000;
            }
            MemWrite(address, value);
            UpdateNegative(value);

        }

        void ror_acc() {
            var value = Accumulator;
            var oldCarry = Status.HasFlag(Flags.Carry);

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

        void rti() {
            byte statusBits = StackPop();
            Status = (Flags)statusBits;
            ClearStatusFlag(Flags.Break);
            SetStatusFlag(Flags.Break2);
            ProgramCounter = StackPopShort();
        }

        void rts() {
            var addr = (ushort)(StackPopShort() + 1);
            ProgramCounter = addr;
        }

        void sbc(AddressingMode mode) {
            var (address, pageCross) = GetOperandAddress(mode);
            var value = MemRead(address);
            AddToAcc((byte)(byte.MaxValue - value));

            if (pageCross) {
                Bus.TickPPUCycles(1);
            }
        }

        void sta(AddressingMode mode){
            var (address, _) = GetOperandAddress(mode);
            MemWrite(address, Accumulator);
        }

        void stx(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            MemWrite(address, RegisterX);
        }

        void sty(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            MemWrite(address, RegisterY);
        }

        void tax() {
            RegisterX = Accumulator;
            UpdateZeroAndNegative(RegisterX);
        }

        void tay() {
            RegisterY = Accumulator;
            UpdateZeroAndNegative(RegisterY);
        }

        void tsx() {
            RegisterX = StackPointer;
            UpdateZeroAndNegative(RegisterX);
        }

        void txa() {
            Accumulator = RegisterX;
            UpdateZeroAndNegative(Accumulator);
        }

        void txs() {
            StackPointer = RegisterX;
        }

        void tya() {
            Accumulator = RegisterY;
            UpdateZeroAndNegative(Accumulator);
        }

        /* 
         * Here be ILLEGAL instructions
         * 👮🏻‍
         */

        void aac(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator &= value;
            UpdateZeroAndNegative(value);

            if (Status.HasFlag(Flags.Negative)) {
                SetStatusFlag(Flags.Carry);
            }
        }

        void aax(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = (byte)(RegisterX & Accumulator);
            MemWrite(address, value);
            //UpdateZeroAndNegative(value);
        }

        void arr(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            Accumulator &= value;
            ror_acc();
            UpdateZeroAndNegative(Accumulator);

            var res = Accumulator;
            var six = (res & 0b01000000) > 0;
            var five = (res & 0b00100000) > 0;

            if ((six && five) || (six && !five)) {
                SetStatusFlag(Flags.Carry);
                ClearStatusFlag(Flags.Overflow);
            } else if((!six && !five) || (!six && five)) {
                SetStatusFlag(Flags.Overflow);
                ClearStatusFlag(Flags.Carry);
            }
        }
                       
        void asr(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator &= value;
            ror_acc();
        }

        void atx(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator &= value;
            RegisterX = Accumulator;
            UpdateZeroAndNegative(RegisterX);
        }

        void axa(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            byte result = (byte)(RegisterX & Accumulator);
            result &= 0b111;
            MemWrite(address, result);
        }

        void axs(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            RegisterX &= Accumulator;
            RegisterX -= value;
            if ((value >> 7 ) == 1) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            UpdateZeroAndNegative(RegisterX);
        }

        void dcp(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            value -= 1;
            MemWrite(address, value);

            if (value <= Accumulator) {
                SetStatusFlag(Flags.Carry);
            } else {
                ClearStatusFlag(Flags.Carry);
            }
            UpdateZeroAndNegative((byte)(Accumulator - value));
        }

        void isc(AddressingMode mode) {
            var value = inc(mode);
            AddToAcc((byte)(byte.MaxValue - value));
        }

        //fn isc(&mut self, mode: &AddressingMode) {
        //    let value = self.inc(&mode);
        //    self.add_to_acc((value as i8).wrapping_neg().wrapping_sub(1) as u8);
        //}

        void lar(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            byte res = (byte)(value & StackPointer);
            Accumulator = res;
            RegisterX = res;
            StackPointer = res;
            UpdateZeroAndNegative(res);
        }

        void lax(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator = value;
            RegisterX = value;
            UpdateZeroAndNegative(value);
        }

        void rla(AddressingMode mode) {
            rol(mode);

            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator &= value;
            UpdateZeroAndNegative(Accumulator);
        }

        void rra(AddressingMode mode) {
            ror(mode);
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);
            AddToAcc(value);
        }

        void slo(AddressingMode mode) {
            asl(mode);

            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator |= value;
            UpdateZeroAndNegative(Accumulator);
        }

        void sre(AddressingMode mode) {
            lsr(mode);

            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            Accumulator ^= value;
            UpdateZeroAndNegative(Accumulator);
        }

        void sxa(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            var result = (byte)(RegisterX & (value & 0b11110000 + 1));
            MemWrite(address, result);
        }

        void sya(AddressingMode mode) {
            var (address, _) = GetOperandAddress(mode);
            var value = MemRead(address);

            var result = (byte)(RegisterY & (value & 0b11110000 + 1));
            MemWrite(address, result);
        }
    }
}
