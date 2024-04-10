using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace MultiCoreEmulator.Cores.GBC {
    [Flags]
    internal enum CpuFlags {
        Empty       = 0,
        Carry       = 1 << 4,
        HalfCarry   = 1 << 5,
        Subtraction = 1 << 6,
        Zero        = 1 << 7,
    }

    internal partial class CPU {
        // Registers
        byte A;
        CpuFlags F;

        byte B;
        byte C;

        byte D;
        byte E;

        byte H;
        byte L;

        ushort SP;
        ushort PC;

        int CyclesLeft;

        public bool IsHalted;

        // public bool InterruptsEnabled;
        // public bool InterruptEnableScheduled;

        // temp variables used for instructions
        // usually reading/calculations, so no
        // allocations are done when not needed
        byte opcode;
        byte prefix;

        ushort address;

        byte n;
        sbyte signed_n;

        byte lsb;
        byte msb;

        int condition;
        int carry;
        int result;

        FileStream fs;
        StreamWriter sw;

        ulong cy;

        public CPU(string fileName) {
            Reset();

            fs = new FileStream($"{fileName}.log", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            sw = new StreamWriter(fs);
        }

        private void Reset() {
            // A = 0x11;
            A = 0x01;
            // F = CpuFlags.Zero;
            F = (CpuFlags)0xb0;

            B = 0x00;
            C = 0x13;

            D = 0x00;
            E = 0xd8;

            H = 0x01;
            L = 0x4d;

            PC = 0x0100;
            SP = 0xFFFE;

            cy = 0;
        }

        public bool FlagIsSet(int cc) {
            switch (cc) {
                case 0: // NZ
                    return !F.HasFlag(CpuFlags.Zero);
                case 1: // Z
                    return F.HasFlag(CpuFlags.Zero);
                case 2: // NC
                    return !F.HasFlag(CpuFlags.Carry);
                case 3: // C
                    return F.HasFlag(CpuFlags.Carry);
                default:
                    return false;
            }
        }

        public ushort GetResetAddr(byte opcode) {
            switch ((BaseInstructions)opcode) {
                case BaseInstructions.RST_00H:
                    return 0x0;
                case BaseInstructions.RST_08H:
                    return 0x8;
                case BaseInstructions.RST_10H:
                    return 0x10;
                case BaseInstructions.RST_18H:
                    return 0x18;
                case BaseInstructions.RST_20H:
                    return 0x20;
                case BaseInstructions.RST_28H:
                    return 0x28;
                case BaseInstructions.RST_30H:
                    return 0x30;
                case BaseInstructions.RST_38H:
                    return 0x38;
                default:
                    return 0;
            }
        }

        //16-bit registers that are combined of the previous ones
        public ushort AF {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (ushort)((A << 8) | (byte)F);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                A = (byte)(value >> 8);
                F = (CpuFlags)(value & 0xF0);
            }
        }

        public ushort BC {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (ushort)((B << 8) | C);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                B = (byte)(value >> 8);
                C = (byte)(value & 0xFF);
            }
        }

        public ushort DE {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (ushort)((D << 8) | E);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                D = (byte)(value >> 8);
                E = (byte)(value & 0xFF);
            }
        }

        public ushort HL {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (ushort)((H << 8) | L);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                H = (byte)(value >> 8);
                L = (byte)(value & 0xFF);
            }
        }

        public void Clock(Board board) {
            if (--CyclesLeft > 0) {
                return;
            }

            board.TickInterruptTimer();

            // if (cy == 4864292) {
            if (PC == 0xC005) {
                var x = 2;
                x++;
            }

            if (IsHalted && !board.PeekPendingInterrupt()) {
                CyclesLeft = 1 * 4;
                cy += (ulong)(CyclesLeft / 2);
                return;
            }
            IsHalted = false;
            if (board.HasPendingInterrupt()) {
                board.SetInterruptEnabledUnsafe(false);

                ushort handlerAddress = 0;
                switch (board.GetPendingInterrupt()) {
                    case InterruptType.VBlank:
                        board.ClearInterrupt(InterruptType.VBlank);
                        handlerAddress = 0x40;
                        break;
                    case InterruptType.LCD:
                        board.ClearInterrupt(InterruptType.LCD);
                        handlerAddress = 0x48;
                        break;
                    case InterruptType.Timer:
                        board.ClearInterrupt(InterruptType.Timer);
                        handlerAddress = 0x50;
                        break;
                    case InterruptType.Serial:
                        handlerAddress = 0x58;
                        board.ClearInterrupt(InterruptType.Serial);
                        break;
                    case InterruptType.Joypad:
                        handlerAddress = 0x60;
                        board.ClearInterrupt(InterruptType.Joypad);
                        break;
                    case InterruptType.None:
                        throw new Exception("Shouldn't even be possible");
                }

                StackWriteUShort(board, PC);
                PC = handlerAddress;

                // Convert M-cycles to T-cycles
                CyclesLeft = 5 * 4;
                cy += (ulong)(CyclesLeft / 2);
                return;
            }

            sw.Write($"{PC:X4}:  00        nop                 A:{A:x2} F:{(int)F:x2} B:{B:x2} C:{C:x2} D:{D:x2} E:{E:x2} H:{H:x2} L:{L:x2} LY:{board.Display.LY:x2} SP:{SP:x4}  Cy:{cy}\r\n");
            sw.Flush();

            // sw.Write($"{PC:X4}:");
            // sw.BaseStream.Position += 30;
            // sw.Write($"A:{A:x2} F:{(int)F:x2} B:{B:x2} C:{C:x2} D:{D:x2} E:{E:x2} H:{H:x2} L:{L:x2} LY:{board.Display.LY:x2} SP:{SP:x4} Cy:{cy}\r\n");
            // sw.Flush();

            opcode = board.Read(PC++);
            ProcessInstruction(board);

            // Convert M-cycles to T-cycles
            CyclesLeft *= 4;
            cy += (ulong)(CyclesLeft / 2);
        }

        void ProcessInstruction(Board board) {
            switch ((BaseInstructions)opcode) {
                // HALT
                case BaseInstructions.HALT:
                    IsHalted = true;
                    break;
                case BaseInstructions.STOP_d8:
                    IsHalted = true;
                    break;

                case BaseInstructions.DI: {
                    board.SetInterruptEnabled(false);
                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.EI: {
                    board.SetInterruptEnabled(true);
                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.NOP: {
                    CyclesLeft = 1;
                    break;
                }

                // 8-bit load instructions

                // Immediate to register
                // LD r, n
                case BaseInstructions.LD_A_d8:
                    A = board.Read(PC++);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_B_d8:
                    B = board.Read(PC++);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_C_d8:
                    C = board.Read(PC++);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_D_d8:
                    D = board.Read(PC++);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_E_d8:
                    E = board.Read(PC++);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_H_d8:
                    H = board.Read(PC++);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_L_d8:
                    L = board.Read(PC++);
                    CyclesLeft = 2;
                    break;

                // Register to register
                // LD r,r'
                case BaseInstructions.LD_A_A:
                    A = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_A_B:
                    A = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_A_C:
                    A = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_A_D:
                    A = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_A_E:
                    A = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_A_H:
                    A = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_A_L:
                    A = L;
                    CyclesLeft = 1;
                    break;

                case BaseInstructions.LD_B_A:
                    B = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_B_B:
                    B = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_B_C:
                    B = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_B_D:
                    B = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_B_E:
                    B = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_B_H:
                    B = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_B_L:
                    B = L;
                    CyclesLeft = 1;
                    break;

                case BaseInstructions.LD_C_A:
                    C = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_C_B:
                    C = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_C_C:
                    C = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_C_D:
                    C = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_C_E:
                    C = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_C_H:
                    C = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_C_L:
                    C = L;
                    CyclesLeft = 1;
                    break;

                case BaseInstructions.LD_D_A:
                    D = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_D_B:
                    D = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_D_C:
                    D = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_D_D:
                    D = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_D_E:
                    D = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_D_H:
                    D = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_D_L:
                    D = L;
                    CyclesLeft = 1;
                    break;

                case BaseInstructions.LD_E_A:
                    E = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_E_B:
                    E = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_E_C:
                    E = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_E_D:
                    E = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_E_E:
                    E = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_E_H:
                    E = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_E_L:
                    E = L;
                    CyclesLeft = 1;
                    break;

                case BaseInstructions.LD_H_A:
                    H = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_H_B:
                    H = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_H_C:
                    H = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_H_D:
                    H = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_H_E:
                    H = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_H_H:
                    H = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_H_L:
                    H = L;
                    CyclesLeft = 1;
                    break;

                case BaseInstructions.LD_L_A:
                    L = A;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_L_B:
                    L = B;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_L_C:
                    L = C;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_L_D:
                    L = D;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_L_E:
                    L = E;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_L_H:
                    L = H;
                    CyclesLeft = 1;
                    break;
                case BaseInstructions.LD_L_L:
                    L = L;
                    CyclesLeft = 1;
                    break;

                // Memory to register
                // LD r, (HL)
                case BaseInstructions.LD_A_HL:
                    A = board.Read(HL);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_B_HL:
                    B = board.Read(HL);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_C_HL:
                    C = board.Read(HL);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_D_HL:
                    D = board.Read(HL);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_E_HL:
                    E = board.Read(HL);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_H_HL:
                    H = board.Read(HL);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_L_HL:
                    L = board.Read(HL);
                    CyclesLeft = 2;
                    break;

                // Register to memory
                // LD (HL), r
                case BaseInstructions.LD_HL_A:
                    board.Write(HL, A);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_HL_B:
                    board.Write(HL, B);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_HL_C:
                    board.Write(HL, C);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_HL_D:
                    board.Write(HL, D);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_HL_E:
                    board.Write(HL, E);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_HL_H:
                    board.Write(HL, H);
                    CyclesLeft = 2;
                    break;
                case BaseInstructions.LD_HL_L:
                    board.Write(HL, L);
                    CyclesLeft = 2;
                    break;

                // Immediate to memory
                // LD (HL), n
                case BaseInstructions.LD_HL_d8:
                    board.Write(HL, board.Read(PC++));
                    CyclesLeft = 3;
                    break;

                // Indirect to accumulator
                // LD A, (BC)
                case BaseInstructions.LD_A_BC:
                    A = board.Read(BC);
                    CyclesLeft = 2;
                    break;
                // LD A, (DE)
                case BaseInstructions.LD_A_DE:
                    A = board.Read(DE);
                    CyclesLeft = 2;
                    break;

                // Accumulator to indirect
                // LD(BC), A
                case BaseInstructions.LD_BC_A:
                    board.Write(BC, A);
                    CyclesLeft = 2;
                    break;
                // LD (DE), A
                case BaseInstructions.LD_DE_A:
                    board.Write(DE, A);
                    CyclesLeft = 2;
                    break;

                // Load direct address to accumulator
                // LD A, (nn)
                case BaseInstructions.LD_A_A16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);
                    A = board.Read(address);
                    CyclesLeft = 4;
                    break;
                }

                // Load accumulator to direct address
                // LD (nn), A
                case BaseInstructions.LD_a16_A: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);
                    board.Write(address, A);
                    CyclesLeft = 4;
                    break;
                }

                // Load indirect 0xFF00 + C to accumulator
                // LDH A, (C)
                case BaseInstructions.LD_A_C_INDIRECT: {
                    address = (ushort)(0xFF00 | C);
                    A = board.Read(address);
                    CyclesLeft = 2;
                    break;
                }

                // Load accumulator to 0xFF00 + C
                // LDH (C), A
                case BaseInstructions.LD_C_A_INDIRECT: {
                    address = (ushort)(0xFF00 | C);
                    board.Write(address, A);
                    CyclesLeft = 2;
                    break;
                }

                // Load direct 0xFF00 + n to accumulator
                // LDH A, (n)
                case BaseInstructions.LDH_A_a8: {
                    n = board.Read(PC++);
                    A = board.Read((ushort)(0xFF00 | n));
                    CyclesLeft = 3;
                    break;
                }

                // Load accumulator to 0xFF00 + n
                // LDH (n), A
                case BaseInstructions.LDH_a8_A: {
                    n = board.Read(PC++);
                    board.Write((ushort)(0xFF00 | n), A);
                    CyclesLeft = 3;
                    break;
                }

                // Load HL in memory decremented to accumulator
                // LD A, (HL-)
                case BaseInstructions.LD_A_HL_DECR:
                    A = board.Read(HL--);
                    CyclesLeft = 2;
                    break;

                // Load accumulator to HL in memory decremented
                // LD (HL-), A
                case BaseInstructions.LD_HL_DECR_A:
                    board.Write(HL--, A);
                    CyclesLeft = 2;
                    break;

                // Load HL in memory decremented to accumulator
                // LD A, (HL+)
                case BaseInstructions.LD_A_HL_INCR:
                    A = board.Read(HL++);
                    CyclesLeft = 2;
                    break;

                // Load accumulator to HL in memory decremented
                // LD (HL+), A
                case BaseInstructions.LD_HL_INCR_A:
                    board.Write(HL++, A);
                    CyclesLeft = 2;
                    break;

                // 16-bit load instructions

                // Load memory to register combo
                // LD BC, nn
                case BaseInstructions.LD_BC_d16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    BC = (ushort)((msb << 8) | lsb);
                    CyclesLeft = 3;
                    break;
                }

                // LD DE, nn
                case BaseInstructions.LD_DE_d16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    DE = (ushort)((msb << 8) | lsb);
                    CyclesLeft = 3;
                    break;
                }

                // LD HL, nn
                case BaseInstructions.LD_HL_d16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    HL = (ushort)((msb << 8) | lsb);
                    CyclesLeft = 3;
                    break;
                }

                // LD SP, nn
                case BaseInstructions.LD_SP_d16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    SP = (ushort)((msb << 8) | lsb);
                    CyclesLeft = 3;
                    break;
                }

                // Write stack pointer to memory address
                // LD (nn), SP
                case BaseInstructions.LD_a16_SP: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);

                    board.Write(address, (byte)(SP & 0xFF));
                    board.Write((ushort)(address + 1), (byte)((SP >> 8) & 0xFF));

                    CyclesLeft = 5;
                    break;
                }

                // Load HL into stack pointer
                // LD SP, HL
                case BaseInstructions.LD_SP_HL: {
                    SP = HL;
                    CyclesLeft = 2;
                    break;
                }

                // Push a 16-bit register to the stack
                // PUSH BC
                case BaseInstructions.PUSH_BC: {
                    StackWriteUShort(board, BC);
                    // StackWriteByte(board, B);
                    // StackWriteByte(board, C);
                    CyclesLeft = 4;
                    break;
                }

                // PUSH DE
                case BaseInstructions.PUSH_DE: {
                    StackWriteUShort(board, DE);
                    // StackWriteByte(board, D);
                    // StackWriteByte(board, E);
                    CyclesLeft = 4;
                    break;
                }

                // PUSH HL
                case BaseInstructions.PUSH_HL: {
                    StackWriteUShort(board, HL);
                    // StackWriteByte(board, H);
                    // StackWriteByte(board, L);
                    CyclesLeft = 4;
                    break;
                }

                // PUSH AF
                case BaseInstructions.PUSH_AF: {
                    StackWriteUShort(board, AF);
                    // StackWriteByte(board, A);
                    // StackWriteByte(board, (byte)F);
                    CyclesLeft = 4;
                    break;
                }

                // Pop stack to a 16-bit register
                // POP BC
                case BaseInstructions.POP_BC: {
                    BC = StackReadUShort(board);
                    // C = StackReadByte(board);
                    // B = StackReadByte(board);
                    CyclesLeft = 3;
                    break;
                }

                // POP DE
                case BaseInstructions.POP_DE: {
                    DE = StackReadUShort(board);
                    // E = StackReadByte(board);
                    // D = StackReadByte(board);
                    CyclesLeft = 3;
                    break;
                }

                // POP HL
                case BaseInstructions.POP_HL: {
                    HL = StackReadUShort(board);
                    // L = StackReadByte(board);
                    // H = StackReadByte(board);
                    CyclesLeft = 3;
                    break;
                }

                // POP AF
                case BaseInstructions.POP_AF: {
                    AF = StackReadUShort(board);
                    // F = (CpuFlags)StackReadByte(board);
                    // A = StackReadByte(board);
                    // also set flags
                    CyclesLeft = 3;
                    break;
                }

                // 8-bit arithmetic and logical instructions

                // Adds register to A
                case BaseInstructions.ADD_A_A: { // A
                    SetCarry(A, A);
                    SetHalfCarry(A, A);
                    A += A;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADD_A_B: {// B
                    SetCarry(A, B);
                    SetHalfCarry(A, B);
                    A += B;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADD_A_C: {// C
                    SetCarry(A, C);
                    SetHalfCarry(A, C);
                    A += C;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADD_A_D: {// D
                    SetCarry(A, D);
                    SetHalfCarry(A, D);
                    A += D;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADD_A_E: {// E
                    SetCarry(A, E);
                    SetHalfCarry(A, E);
                    A += E;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADD_A_H: {// H
                    SetCarry(A, H);
                    SetHalfCarry(A, H);
                    A += H;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADD_A_L: {// L
                    SetCarry(A, L);
                    SetHalfCarry(A, L);
                    A += L;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }

                // ADD (HL)
                // Adds add data from address specified by HL to acc
                case BaseInstructions.ADD_A_HL: {
                    n = board.Read(HL);
                    SetCarry(A, n);
                    SetHalfCarry(A, n);
                    A += n;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }

                // ADD n
                // Adds immediate data to accumulator
                case BaseInstructions.ADD_A_d8: {
                    n = board.Read(PC++);
                    SetCarry(A, n);
                    SetHalfCarry(A, n);
                    A += n;
                    SetZero(A == 0);
                    SetSubtraction(false);
                    CyclesLeft = 2;
                    break;
                }

                // ADC A-L
                // Add with carry to accumulator
                case BaseInstructions.ADC_A_A: { // A
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, A, carry);
                    SetHalfCarry(A, A, carry);
                    A += (byte)(A + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADC_A_B: { // B
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, B, carry);
                    SetHalfCarry(A, B, carry);
                    A += (byte)(B + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADC_A_C: { // C
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, C, carry);
                    SetHalfCarry(A, C, carry);
                    A += (byte)(C + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADC_A_D: { // D
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, D, carry);
                    SetHalfCarry(A, D, carry);
                    A += (byte)(D + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADC_A_E: { // E
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, E, carry);
                    SetHalfCarry(A, E, carry);
                    A += (byte)(E + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADC_A_H: { // H
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, H, carry);
                    SetHalfCarry(A, H, carry);
                    A += (byte)(H + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.ADC_A_L: { // L
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, L, carry);
                    SetHalfCarry(A, L, carry);
                    A += (byte)(L + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }

                // ADC (HL)
                // Adds add data from address specified by HL to acc
                // with carry
                case BaseInstructions.ADC_A_HL: {
                    n = board.Read(HL);

                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, n, carry);
                    SetHalfCarry(A, n, carry);
                    A += (byte)(n + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }

                // ADC n
                // Adds to the 8-bit A register, the carry flag and the immediate data n,
                // and stores the result back into the A register.
                case BaseInstructions.ADC_A_d8: {
                    n = board.Read(PC++);
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetHalfCarry(A, n, carry);
                    SetCarry(A, n, carry);
                    A += (byte)(n + carry);
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }

                // SUB r: Subtract (register)
                // Subtracts from the 8-bit A register, the 8-bit register r,
                // and stores the result back into the A register.
                case BaseInstructions.SUB_A: {
                    SetHalfCarry(A, -A);
                    SetCarry(A, -A);
                    A -= A;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SUB_B: {
                    SetHalfCarry(A, -B);
                    SetCarry(A, -B);
                    A -= B;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SUB_C: {
                    SetHalfCarry(A, -C);
                    SetCarry(A, -C);
                    A -= C;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SUB_D: {
                    SetHalfCarry(A, -D);
                    SetCarry(A, -D);
                    A -= D;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SUB_E: {
                    SetHalfCarry(A, -E);
                    SetCarry(A, -E);
                    A -= E;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SUB_H: {
                    SetHalfCarry(A, -H);
                    SetCarry(A, -H);
                    A -= H;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SUB_L: {
                    SetHalfCarry(A, -L);
                    SetCarry(A, -L);
                    A -= L;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.SUB_HL_INDIRECT: {
                    n = board.Read(HL);

                    SetHalfCarry(A, -n);
                    SetCarry(A, -n);
                    A -= n;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.SUB_d8: {
                    n = board.Read(PC++);

                    SetHalfCarry(A, -n);
                    SetCarry(A, -n);
                    A -= n;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.SBC_A_A: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -A, -carry);
                    SetHalfCarry(A, -A, -carry);
                    A -= (byte)(A + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SBC_A_B: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -B, -carry);
                    SetHalfCarry(A, -B, -carry);
                    A -= (byte)(B + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SBC_A_C: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -C, -carry);
                    SetHalfCarry(A, -C, -carry);
                    A -= (byte)(C + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SBC_A_D: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -D, -carry);
                    SetHalfCarry(A, -D, -carry);
                    A -= (byte)(D + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SBC_A_E: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -E, -carry);
                    SetHalfCarry(A, -E, -carry);
                    A -= (byte)(E + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SBC_A_H: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -H, -carry);
                    SetHalfCarry(A, -H, -carry);
                    A -= (byte)(H + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.SBC_A_L: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -L, -carry);
                    SetHalfCarry(A, -L, -carry);
                    A -= (byte)(L + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.SBC_A_d8: {
                    n = board.Read(PC++);
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetHalfCarry(A, -n, -carry);
                    SetCarry(A, -n, -carry);
                    A -= (byte)(n + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.SBC_A_HL_INDIRECT: {
                    n = board.Read(HL);
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry(A, -n, -carry);
                    SetHalfCarry(A, -n, -carry);
                    A -= (byte)(n + carry);
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.CP_A: {
                    result = A - A;

                    SetCarry(A, -A);
                    SetHalfCarry(A, -A);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.CP_B: {
                    result = A - B;

                    SetCarry(A, -B);
                    SetHalfCarry(A, -B);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.CP_C: {
                    result = A - C;

                    SetCarry(A, -C);
                    SetHalfCarry(A, -C);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.CP_D: {
                    result = A - D;

                    SetCarry(A, -D);
                    SetHalfCarry(A, -D);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.CP_E: {
                    result = A - E;

                    SetCarry(A, -E);
                    SetHalfCarry(A, -E);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.CP_H: {
                    result = A - H;

                    SetCarry(A, -H);
                    SetHalfCarry(A, -H);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.CP_L: {
                    result = A - L;

                    SetCarry(A, -L);
                    SetHalfCarry(A, -L);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.CP_HL_INDIRECT: {
                    n = board.Read(HL);
                    result = A - n;

                    SetCarry(A, -n);
                    SetHalfCarry(A, -n);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.CP_D8: {
                    n = board.Read(PC++);
                    result = A - n;

                    SetCarry(A, -n);
                    SetHalfCarry(A, -n);
                    SetZero(result == 0);
                    SetSubtraction(true);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.INC_A: {
                    SetHalfCarry(A, 1);
                    A++;
                    SetZero(A == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.INC_B: {
                    SetHalfCarry(B, 1);
                    B++;
                    SetZero(B == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.INC_C: {
                    SetHalfCarry(C, 1);
                    C++;
                    SetZero(C == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.INC_D: {
                    SetHalfCarry(D, 1);
                    D++;
                    SetZero(D == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.INC_E: {
                    SetHalfCarry(E, 1);
                    E++;
                    SetZero(E == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.INC_H: {
                    SetHalfCarry(H, 1);
                    H++;
                    SetZero(H == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.INC_L: {
                    SetHalfCarry(L, 1);
                    L++;
                    SetZero(L == 0);
                    SetSubtraction(false);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.INC_HL_INDIRECT: {
                    byte data = board.Read(HL);

                    SetHalfCarry(data, 1);
                    data++;
                    SetZero(data == 0);
                    SetSubtraction(false);

                    board.Write(HL, data);
                    CyclesLeft = 3;
                    break;
                }

                case BaseInstructions.DEC_A: {
                    SetHalfCarry(A, -1);
                    A--;
                    SetZero(A == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.DEC_B: {
                    if (cy == 80386) {
                        var y = 2;
                    }

                    SetHalfCarry(B, -1);
                    B--;
                    SetZero(B == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.DEC_C: {
                    SetHalfCarry(C, -1);
                    C--;
                    SetZero(C == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.DEC_D: {
                    SetHalfCarry(D, -1);
                    D--;
                    SetZero(D == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.DEC_E: {
                    SetHalfCarry(E, -1);
                    E--;
                    SetZero(E == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.DEC_H: {
                    SetHalfCarry(H, -1);
                    H--;
                    SetZero(H == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.DEC_L: {
                    SetHalfCarry(L, -1);
                    L--;
                    SetZero(L == 0);
                    SetSubtraction(true);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.DEC_HL_INDIRECT: {
                    byte data = board.Read(HL);

                    SetHalfCarry(data, -1);
                    data--;
                    SetZero(data == 0);
                    SetSubtraction(true);

                    board.Write(HL, data);
                    CyclesLeft = 3;
                    break;
                }

                case BaseInstructions.AND_A: {
                    A &= A;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.AND_B: {
                    A &= B;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.AND_C: {
                    A &= C;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.AND_D: {
                    A &= D;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.AND_E: {
                    A &= E;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.AND_H: {
                    A &= H;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.AND_L: {
                    A &= L;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.AND_HL: {
                    n = board.Read(HL);
                    A &= n;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.AND_d8: {
                    n = board.Read(PC++);
                    A &= n;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.OR_A: {
                    A |= A;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.OR_B: {
                    A |= B;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.OR_C: {
                    A |= C;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.OR_D: {
                    A |= D;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.OR_E: {
                    A |= E;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.OR_H: {
                    A |= H;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.OR_L: {
                    A |= L;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.OR_HL_INDIRECT: {
                    n = board.Read(HL);
                    A |= n;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.OR_d8: {
                    n = board.Read(PC++);
                    A |= n;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.XOR_A: {
                    A ^= A;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.XOR_B: {
                    A ^= B;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.XOR_C: {
                    A ^= C;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.XOR_D: {
                    A ^= D;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.XOR_E: {
                    A ^= E;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.XOR_H: {
                    A ^= H;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.XOR_L: {
                    A ^= L;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.XOR_HL_INDIRECT: {
                    n = board.Read(HL);
                    A ^= n;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.XOR_d8: {
                    n = board.Read(PC++);
                    A ^= n;

                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetCarry(0, 0);
                    SetHalfCarry(0, 0);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.CCF: {
                    SetSubtraction(false);
                    SetHalfCarry(0, 0);
                    if (IsSet(CpuFlags.Carry)) {
                        SetCarry(false);
                    } else {
                        SetCarry(true);
                    }

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.SCF: {
                    SetSubtraction(false);
                    SetHalfCarry(0, 0);
                    SetCarry(true);

                    CyclesLeft = 1;
                    break;
                }

                // https://blog.ollien.com/posts/gb-daa/
                case BaseInstructions.DAA: {
                    byte correction = 0;
                    bool subtraction = (F & CpuFlags.Subtraction) > 0;

                    if ((!subtraction && (A & 0xF) > 0x09) || (F & CpuFlags.HalfCarry) > 0) {
                        correction |= 0x06;
                    }
                    if ((!subtraction && A > 0x99) || (F & CpuFlags.Carry) > 0) {
                        correction |= 0x60;
                        SetCarry(true);
                    }

                    if (subtraction) {
                        A -= correction;
                    } else {
                        A += correction;
                    }
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.CPL: {
                    A = (byte)~A;

                    SetSubtraction(true);
                    SetHalfCarry(0b1000, 0b1000);

                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.JP_a16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);
                    PC = address;
                    CyclesLeft = 4;
                    break;
                }

                case BaseInstructions.JP_HL: {
                    PC = HL;
                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.JP_NZ_a16:
                case BaseInstructions.JP_NC_a16:
                case BaseInstructions.JP_Z_a16:
                case BaseInstructions.JP_C_a16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);

                    condition = (opcode & 0b00011000) >> 3;
                    if (FlagIsSet(condition)) {
                        PC = address;
                        CyclesLeft = 4;
                    } else {
                        CyclesLeft = 3;
                    }
                    break;
                }

                case BaseInstructions.JR_r8: {
                    sbyte e = (sbyte)board.Read(PC++);
                    PC = (ushort)(PC + e);
                    CyclesLeft = 3;
                    break;
                }

                case BaseInstructions.JR_NZ_r8:
                case BaseInstructions.JR_NC_r8:
                case BaseInstructions.JR_Z_r8:
                case BaseInstructions.JR_C_r8: {
                    sbyte e = (sbyte)board.Read(PC++);

                    condition = (opcode & 0b00011000) >> 3;
                    if (FlagIsSet(condition)) {
                        PC = (ushort)(PC + e);
                        CyclesLeft = 3;
                    } else {
                        CyclesLeft = 2;
                    }
                    break;
                }

                case BaseInstructions.CALL_a16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);

                    StackWriteUShort(board, PC);

                    PC = address;

                    CyclesLeft = 6;
                    break;
                }

                case BaseInstructions.CALL_NZ_a16:
                case BaseInstructions.CALL_NC_a16:
                case BaseInstructions.CALL_Z_a16:
                case BaseInstructions.CALL_C_a16: {
                    lsb = board.Read(PC++);
                    msb = board.Read(PC++);
                    address = (ushort)((msb << 8) | lsb);

                    condition = (opcode & 0b00011000) >> 3;
                    if (FlagIsSet(condition)) {
                        StackWriteUShort(board, PC);

                        PC = address;
                        CyclesLeft = 6;
                    } else {
                        CyclesLeft = 3;
                    }
                    break;
                }

                case BaseInstructions.RET: {
                    address = StackReadUShort(board);
                    PC = address;
                    CyclesLeft = 4;
                    break;
                }

                case BaseInstructions.RET_C:
                case BaseInstructions.RET_Z:
                case BaseInstructions.RET_NC:
                case BaseInstructions.RET_NZ: {
                    condition = (opcode & 0b00011000) >> 3;
                    if (FlagIsSet(condition)) {
                        address = StackReadUShort(board);
                        PC = address;
                        CyclesLeft = 5;
                    } else {
                        CyclesLeft = 2;
                    }
                    break;
                }

                case BaseInstructions.RETI: {
                    board.SetInterruptEnabled(true);
                    goto case BaseInstructions.RET;
                }

                case BaseInstructions.RST_00H:
                case BaseInstructions.RST_08H:
                case BaseInstructions.RST_10H:
                case BaseInstructions.RST_18H:
                case BaseInstructions.RST_20H:
                case BaseInstructions.RST_28H:
                case BaseInstructions.RST_30H:
                case BaseInstructions.RST_38H: {
                    var resetAddr = GetResetAddr(opcode);
                    StackWriteUShort(board, PC);
                    PC = resetAddr;
                    CyclesLeft = 4;
                    break;
                }

                case BaseInstructions.ADD_HL_BC: {
                    SetUShortCarry(HL, BC);
                    SetUShortHalfCarry(HL, BC);
                    HL += BC;
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.ADD_HL_DE: {
                    SetUShortCarry(HL, DE);
                    SetUShortHalfCarry(HL, DE);
                    HL += DE;
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.ADD_HL_HL: {
                    SetUShortCarry(HL, HL);
                    SetUShortHalfCarry(HL, HL);
                    HL += HL;
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.ADD_HL_SP: {
                    SetUShortCarry(HL, SP);
                    SetUShortHalfCarry(HL, SP);
                    HL += SP;
                    SetSubtraction(false);

                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.INC_BC: {
                    BC++;
                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.INC_DE: {
                    DE++;
                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.INC_HL: {
                    HL++;
                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.INC_SP: {
                    ++SP;
                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.DEC_BC: {
                    BC--;
                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.DEC_DE: {
                    DE--;
                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.DEC_HL: {
                    HL--;
                    CyclesLeft = 2;
                    break;
                }
                case BaseInstructions.DEC_SP: {
                    SP--;
                    CyclesLeft = 2;
                    break;
                }

                case BaseInstructions.ADD_SP_r8: {
                    signed_n = (sbyte)board.Read(PC++);
                    SetHalfCarry(((SP & 0xF) + (signed_n & 0xF) & 0x10) != 0);
                    SetCarry(((SP & 0xFF) + (signed_n & 0xFF) & 0x100) != 0);
                    SP = (ushort)(SP + signed_n);
                    SetZero(false);
                    SetSubtraction(false);
                    CyclesLeft = 4;
                    break;
                }

                case BaseInstructions.LD_HL_SP_INCR_r8: {
                    signed_n = (sbyte)board.Read(PC++);
                    result = (ushort)(SP + signed_n);
                    HL = (ushort)result;

                    SetHalfCarry(((SP & 0xF) + (signed_n & 0xF) & 0x10) != 0);
                    SetCarry(((SP & 0xFF) + (signed_n & 0xFF) & 0x100) != 0);
                    SetZero(false);
                    SetSubtraction(false);
                    CyclesLeft = 3;
                    break;
                }

                case BaseInstructions.RL_C_A: {
                    SetCarry((A & 0x80) > 0);
                    A = (byte)(A << 1 | A >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(false);
                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.RL_A: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((A & 0x80) > 0);
                    A = (byte)(A << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(false);
                    CyclesLeft = 1;
                    break;
                }
                case BaseInstructions.RR_C_A: {
                    SetCarry((A & 0x01) > 0);
                    A = (byte)(A >> 1 | A << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(false);
                    CyclesLeft = 1;
                    break;
                    // carry = A & 0x01;
                    // A = (byte)(A >> 1);
                    // if (carry > 0) {
                    //     A += 0x80;
                    // }
                    // SetCarry(A, 0);
                    // CyclesLeft = 1;
                    // break;
                }
                case BaseInstructions.RR_A: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((A & 0x01) > 0);
                    A = (byte)(A >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(false);
                    CyclesLeft = 1;
                    break;
                }

                case BaseInstructions.PREFIX: {
                    ProcessPrefix(board);
                    break;
                }

                case BaseInstructions.ILLEGAL_E3:
                case BaseInstructions.ILLEGAL_E4:
                case BaseInstructions.ILLEGAL_F4:
                case BaseInstructions.ILLEGAL_DB:
                case BaseInstructions.ILLEGAL_DD:
                case BaseInstructions.ILLEGAL_EB:
                case BaseInstructions.ILLEGAL_EC:
                case BaseInstructions.ILLEGAL_ED:
                case BaseInstructions.ILLEGAL_FC:
                case BaseInstructions.ILLEGAL_FD:
                case BaseInstructions.ILLEGAL_D3: {
                    break;
                }
            }
        }

        void ProcessPrefix(Board board) {
            prefix = board.Read(PC++);
            switch ((PrefixInstructions)prefix) {
                case PrefixInstructions.RLC_A: {
                    SetCarry((A & 0x80) > 0);
                    A = (byte)(A << 1 | A >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RLC_B: {
                    SetCarry((B & 0x80) > 0);
                    B = (byte)(B << 1 | B >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RLC_C: {
                    SetCarry((C & 0x80) > 0);
                    C = (byte)(C << 1 | C >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RLC_D: {
                    SetCarry((D & 0x80) > 0);
                    D = (byte)(D << 1 | D >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RLC_E: {
                    SetCarry((E & 0x80) > 0);
                    E = (byte)(E << 1 | E >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RLC_H: {
                    SetCarry((H & 0x80) > 0);
                    H = (byte)(H << 1 | H >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RLC_L: {
                    SetCarry((L & 0x80) > 0);
                    L = (byte)(L << 1 | L >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RRC_A: {
                    SetCarry((A & 0x01) > 0);
                    A = (byte)(A >> 1 | A << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RRC_B: {
                    SetCarry((B & 0x01) > 0);
                    B = (byte)(B >> 1 | B << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RRC_C: {
                    SetCarry((C & 0x01) > 0);
                    C = (byte)(C >> 1 | C << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RRC_D: {
                    SetCarry((D & 0x01) > 0);
                    D = (byte)(D >> 1 | D << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RRC_E: {
                    SetCarry((E & 0x01) > 0);
                    E = (byte)(E >> 1 | E << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RRC_H: {
                    SetCarry((H & 0x01) > 0);
                    H = (byte)(H >> 1 | H << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RRC_L: {
                    SetCarry((L & 0x01) > 0);
                    L = (byte)(L >> 1 | L << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RL_A: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((A & 0x80) > 0);
                    A = (byte)(A << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RL_B: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((B & 0x80) > 0);
                    B = (byte)(B << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RL_C: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((C & 0x80) > 0);
                    C = (byte)(C << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RL_D: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((D & 0x80) > 0);
                    D = (byte)(D << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RL_E: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((E & 0x80) > 0);
                    E = (byte)(E << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RL_H: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((H & 0x80) > 0);
                    H = (byte)(H << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RL_L: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    SetCarry((L & 0x80) > 0);
                    L = (byte)(L << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RR_A: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((A & 0x01) > 0);
                    A = (byte)(A >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RR_B: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((B & 0x01) > 0);
                    B = (byte)(B >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RR_C: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((C & 0x01) > 0);
                    C = (byte)(C >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RR_D: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((D & 0x01) > 0);
                    D = (byte)(D >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RR_E: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((E & 0x01) > 0);
                    E = (byte)(E >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RR_H: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((H & 0x01) > 0);
                    H = (byte)(H >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RR_L: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    SetCarry((L & 0x01) > 0);
                    L = (byte)(L >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SLA_A: {
                    SetCarry((A & 0x80) > 0);
                    A = (byte)(A << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_B: {
                    SetCarry((B & 0x80) > 0);
                    B = (byte)(B << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_C: {
                    SetCarry((C & 0x80) > 0);
                    C = (byte)(C << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_D: {
                    SetCarry((D & 0x80) > 0);
                    D = (byte)(D << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_E: {
                    SetCarry((E & 0x80) > 0);
                    E = (byte)(E << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_H: {
                    SetCarry((H & 0x80) > 0);
                    H = (byte)(H << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_L: {
                    SetCarry((L & 0x80) > 0);
                    L = (byte)(L << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SRA_A: {
                    SetCarry((A & 0x01) > 0);
                    A = (byte)(A & 0x80 | A >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRA_B: {
                    SetCarry((B & 0x01) > 0);
                    B = (byte)(B & 0x80 | B >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRA_C: {
                    SetCarry((C & 0x01) > 0);
                    C = (byte)(C & 0x80 | C >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRA_D: {
                    SetCarry((D & 0x01) > 0);
                    D = (byte)(D & 0x80 | D >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRA_E: {
                    SetCarry((E & 0x01) > 0);
                    E = (byte)(E & 0x80 | E >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRA_H: {
                    SetCarry((H & 0x01) > 0);
                    H = (byte)(H & 0x80 | H >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRA_L: {
                    SetCarry((L & 0x01) > 0);
                    L = (byte)(L & 0x80 | L >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SWAP_A: {
                    A = (byte)(A >> 4 | A << 4);
                    SetZero(A == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SWAP_B: {
                    B = (byte)(B >> 4 | B << 4);
                    SetZero(B == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SWAP_C: {
                    C = (byte)(C >> 4 | C << 4);
                    SetZero(C == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SWAP_D: {
                    D = (byte)(D >> 4 | D << 4);
                    SetZero(D == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SWAP_E: {
                    E = (byte)(E >> 4 | E << 4);
                    SetZero(E == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SWAP_H: {
                    H = (byte)(H >> 4 | H << 4);
                    SetZero(H == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SWAP_L: {
                    L = (byte)(L >> 4 | L << 4);
                    SetZero(L == 0);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SRL_A: {
                    SetCarry((A & 0x01) > 0);
                    A = (byte)(A >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(A == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRL_B: {
                    SetCarry((B & 0x01) > 0);
                    B = (byte)(B >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(B == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRL_C: {
                    SetCarry((C & 0x01) > 0);
                    C = (byte)(C >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(C == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRL_D: {
                    SetCarry((D & 0x01) > 0);
                    D = (byte)(D >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(D == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRL_E: {
                    SetCarry((E & 0x01) > 0);
                    E = (byte)(E >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(E == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRL_H: {
                    SetCarry((H & 0x01) > 0);
                    H = (byte)(H >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(H == 0);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SRL_L: {
                    SetCarry((L & 0x01) > 0);
                    L = (byte)(L >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(L == 0);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_0_A: {
                    SetZero((A & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_0_B: {
                    SetZero((B & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_0_C: {
                    SetZero((C & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_0_D: {
                    SetZero((D & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_0_E: {
                    SetZero((E & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_0_H: {
                    SetZero((H & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_0_L: {
                    SetZero((L & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_1_A: {
                    SetZero((A & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_1_B: {
                    SetZero((B & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_1_C: {
                    SetZero((C & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_1_D: {
                    SetZero((D & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_1_E: {
                    SetZero((E & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_1_H: {
                    SetZero((H & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_1_L: {
                    SetZero((L & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_2_A: {
                    SetZero((A & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_2_B: {
                    SetZero((B & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_2_C: {
                    SetZero((C & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_2_D: {
                    SetZero((D & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_2_E: {
                    SetZero((E & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_2_H: {
                    SetZero((H & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_2_L: {
                    SetZero((L & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_3_A: {
                    SetZero((A & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_3_B: {
                    SetZero((B & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_3_C: {
                    SetZero((C & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_3_D: {
                    SetZero((D & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_3_E: {
                    SetZero((E & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_3_H: {
                    SetZero((H & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_3_L: {
                    SetZero((L & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_4_A: {
                    SetZero((A & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_4_B: {
                    SetZero((B & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_4_C: {
                    SetZero((C & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_4_D: {
                    SetZero((D & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_4_E: {
                    SetZero((E & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_4_H: {
                    SetZero((H & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_4_L: {
                    SetZero((L & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_5_A: {
                    SetZero((A & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_5_B: {
                    SetZero((B & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_5_C: {
                    SetZero((C & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_5_D: {
                    SetZero((D & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_5_E: {
                    SetZero((E & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_5_H: {
                    SetZero((H & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_5_L: {
                    SetZero((L & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_6_A: {
                    SetZero((A & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_6_B: {
                    SetZero((B & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_6_C: {
                    SetZero((C & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_6_D: {
                    SetZero((D & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_6_E: {
                    SetZero((E & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_6_H: {
                    SetZero((H & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_6_L: {
                    SetZero((L & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.BIT_7_A: {
                    SetZero((A & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_7_B: {
                    SetZero((B & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_7_C: {
                    SetZero((C & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_7_D: {
                    SetZero((D & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_7_E: {
                    SetZero((E & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_7_H: {
                    SetZero((H & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.BIT_7_L: {
                    SetZero((L & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_0_A: {
                    A &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_0_B: {
                    B &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_0_C: {
                    C &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_0_D: {
                    D &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_0_E: {
                    E &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_0_H: {
                    H &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_0_L: {
                    L &= 0b11111110;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_1_A: {
                    A &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_1_B: {
                    B &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_1_C: {
                    C &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_1_D: {
                    D &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_1_E: {
                    E &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_1_H: {
                    H &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_1_L: {
                    L &= 0b11111101;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_2_A: {
                    A &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_2_B: {
                    B &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_2_C: {
                    C &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_2_D: {
                    D &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_2_E: {
                    E &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_2_H: {
                    H &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_2_L: {
                    L &= 0b11111011;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_3_A: {
                    A &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_3_B: {
                    B &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_3_C: {
                    C &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_3_D: {
                    D &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_3_E: {
                    E &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_3_H: {
                    H &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_3_L: {
                    L &= 0b11110111;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_4_A: {
                    A &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_4_B: {
                    B &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_4_C: {
                    C &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_4_D: {
                    D &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_4_E: {
                    E &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_4_H: {
                    H &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_4_L: {
                    L &= 0b11101111;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_5_A: {
                    A &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_5_B: {
                    B &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_5_C: {
                    C &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_5_D: {
                    D &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_5_E: {
                    E &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_5_H: {
                    H &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_5_L: {
                    L &= 0b11011111;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_6_A: {
                    A &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_6_B: {
                    B &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_6_C: {
                    C &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_6_D: {
                    D &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_6_E: {
                    E &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_6_H: {
                    H &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_6_L: {
                    L &= 0b10111111;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RES_7_A: {
                    A &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_7_B: {
                    B &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_7_C: {
                    C &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_7_D: {
                    D &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_7_E: {
                    E &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_7_H: {
                    H &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.RES_7_L: {
                    L &= 0b01111111;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_0_A: {
                    A |= 0x01;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_0_B: {
                    B |= 0x01;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_0_C: {
                    C |= 0x01;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_0_D: {
                    D |= 0x01;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_0_E: {
                    E |= 0x01;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_0_H: {
                    H |= 0x01;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_0_L: {
                    L |= 0x01;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_1_A: {
                    A |= 0x02;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_1_B: {
                    B |= 0x02;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_1_C: {
                    C |= 0x02;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_1_D: {
                    D |= 0x02;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_1_E: {
                    E |= 0x02;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_1_H: {
                    H |= 0x02;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_1_L: {
                    L |= 0x02;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_2_A: {
                    A |= 0x04;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_2_B: {
                    B |= 0x04;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_2_C: {
                    C |= 0x04;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_2_D: {
                    D |= 0x04;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_2_E: {
                    E |= 0x04;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_2_H: {
                    H |= 0x04;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_2_L: {
                    L |= 0x04;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_3_A: {
                    A |= 0x08;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_3_B: {
                    B |= 0x08;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_3_C: {
                    C |= 0x08;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_3_D: {
                    D |= 0x08;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_3_E: {
                    E |= 0x08;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_3_H: {
                    H |= 0x08;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_3_L: {
                    L |= 0x08;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_4_A: {
                    A |= 0x10;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_4_B: {
                    B |= 0x10;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_4_C: {
                    C |= 0x10;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_4_D: {
                    D |= 0x10;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_4_E: {
                    E |= 0x10;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_4_H: {
                    H |= 0x10;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_4_L: {
                    L |= 0x10;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_5_A: {
                    A |= 0x20;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_5_B: {
                    B |= 0x20;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_5_C: {
                    C |= 0x20;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_5_D: {
                    D |= 0x20;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_5_E: {
                    E |= 0x20;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_5_H: {
                    H |= 0x20;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_5_L: {
                    L |= 0x20;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_6_A: {
                    A |= 0x40;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_6_B: {
                    B |= 0x40;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_6_C: {
                    C |= 0x40;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_6_D: {
                    D |= 0x40;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_6_E: {
                    E |= 0x40;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_6_H: {
                    H |= 0x40;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_6_L: {
                    L |= 0x40;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.SET_7_A: {
                    A |= 0x80;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_7_B: {
                    B |= 0x80;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_7_C: {
                    C |= 0x80;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_7_D: {
                    D |= 0x80;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_7_E: {
                    E |= 0x80;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_7_H: {
                    H |= 0x80;
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SET_7_L: {
                    L |= 0x80;
                    CyclesLeft = 2;
                    break;
                }

                case PrefixInstructions.RLC_HL: {
                    var value = board.Read(HL);
                    SetCarry((value & 0x80) > 0);
                    value = (byte)(value << 1 | value >> 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RRC_HL: {
                    var value = board.Read(HL);
                    SetCarry((value & 0x01) > 0);
                    value = (byte)(value >> 1 | value << 7);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RL_HL: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 1;
                    }

                    var value = board.Read(HL);
                    SetCarry((value & 0x80) > 0);
                    value = (byte)(value << 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RR_HL: {
                    carry = 0;
                    if (IsSet(CpuFlags.Carry)) {
                        carry = 128;
                    }

                    var value = board.Read(HL);
                    SetCarry((value & 0x01) > 0);
                    value = (byte)(value >> 1 | carry);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 2;
                    break;
                }
                case PrefixInstructions.SLA_HL: {
                    var value = board.Read(HL);
                    SetCarry((value & 0x80) > 0);
                    value = (byte)(value << 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SRA_HL: {
                    var value = board.Read(HL);
                    SetCarry((value & 0x01) > 0);
                    value = (byte)(value & 0x80 | value >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SWAP_HL: {
                    var value = board.Read(HL);
                    SetZero(value == 0);
                    value = (byte)(value >> 4 | value << 4);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetCarry(false);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SRL_HL: {
                    var value = board.Read(HL);
                    SetCarry((value & 0x01) > 0);
                    value = (byte)(value >> 1);
                    SetSubtraction(false);
                    SetHalfCarry(false);
                    SetZero(value == 0);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_0_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x01) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_1_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x02) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_2_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x04) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_3_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x08) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_4_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x10) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_5_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x20) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_6_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x40) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.BIT_7_HL: {
                    var value = board.Read(HL);
                    SetZero((value & 0x80) == 0);
                    SetSubtraction(false);
                    SetHalfCarry(true);
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_0_HL: {
                    var value = board.Read(HL);
                    value &= 0b11111110;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_1_HL: {
                    var value = board.Read(HL);
                    value &= 0b11111101;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_2_HL: {
                    var value = board.Read(HL);
                    value &= 0b11111011;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_3_HL: {
                    var value = board.Read(HL);
                    value &= 0b11110111;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_4_HL: {
                    var value = board.Read(HL);
                    value &= 0b11101111;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_5_HL: {
                    var value = board.Read(HL);
                    value &= 0b11011111;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_6_HL: {
                    var value = board.Read(HL);
                    value &= 0b10111111;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.RES_7_HL: {
                    var value = board.Read(HL);
                    value &= 0b01111111;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_0_HL: {
                    var value = board.Read(HL);
                    value |= 0x01;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_1_HL: {
                    var value = board.Read(HL);
                    value |= 0x02;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_2_HL: {
                    var value = board.Read(HL);
                    value |= 0x04;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_3_HL: {
                    var value = board.Read(HL);
                    value |= 0x08;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_4_HL: {
                    var value = board.Read(HL);
                    value |= 0x10;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_5_HL: {
                    var value = board.Read(HL);
                    value |= 0x20;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_6_HL: {
                    var value = board.Read(HL);
                    value |= 0x40;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }
                case PrefixInstructions.SET_7_HL: {
                    var value = board.Read(HL);
                    value |= 0x80;
                    board.Write(HL, value);
                    CyclesLeft = 4;
                    break;
                }

                default: {
                    throw new ArgumentOutOfRangeException();
                }
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSet(CpuFlags flag) {
            if ((F & flag) == flag) {
                return true;
            }
            return false;
        }

        public void SetZero(bool set) {
            if (set) {
                F |= CpuFlags.Zero;
            } else {
                F &= ~CpuFlags.Zero;
            }
        }

        public void SetSubtraction(bool set){
            if (set) {
                F |= CpuFlags.Subtraction;
            } else {
                F &= ~CpuFlags.Subtraction;
            }
        }

        public void SetHalfCarry(bool set) {
            if (set) {
                F |= CpuFlags.HalfCarry;
            } else {
                F &= ~CpuFlags.HalfCarry;
            }
        }

        public void SetCarry(bool set) {
            if (set) {
                F |= CpuFlags.Carry;
            } else {
                F &= ~CpuFlags.Carry;
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetHalfCarry(int value, int change, int carryVal = 0) {
            if (change >= 0) {
                SetHalfCarry(((value & 0xF) + (change & 0xF) + carryVal & 0x10) == 0x10);
            } else {
                SetHalfCarry(((value & 0xF) - (-change & 0xF) + carryVal & 0x10) == 0x10);
            }
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetCarry(int value, int change, int carryVal = 0) {
            if (change >= 0) {
                SetCarry(((value & 0xFF) + (change & 0xFF) + carryVal & 0x100) == 0x100);
                // SetCarry((value & 0xFF) + change + carryVal > 0xFF);
            } else {
                SetCarry(((value & 0xFF) - (-change & 0xFF) + carryVal & 0x100) == 0x100);
                // SetCarry((value & 0xFF) + change + carryVal < 0);
            }
        }

        public void SetUShortHalfCarry(int value, int change, int carryVal = 0) {
            if (change >= 0) {
                SetHalfCarry(((value & 0xFFF) + (change & 0xFFF) + carryVal & 0x1000)  == 0x1000);
            } else {
                SetHalfCarry(((value & 0xFFF) - (-change & 0xFFF) + carryVal & 0x1000) == 0x1000);
            }
        }

        public void SetUShortCarry(int value, int change, int carryVal = 0) {
            if (change >= 0) {
                // SetCarry(value + change + carryVal > 0xFFFF);
                SetCarry(((value & 0xFFFF) + (change & 0xFFFF) + carryVal & 0x10000) == 0x10000);
            } else {
                // SetCarry(value + change + carryVal < 0);
                SetCarry(((value & 0xFFFF) - (-change & 0xFFFF) + carryVal & 0x10000) == 0x10000);
            }
        }
    }
}
