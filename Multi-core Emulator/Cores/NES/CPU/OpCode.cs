namespace NesEmu.CPU {
    public struct OpCode {
        public readonly Op Name;
        public readonly byte Code;
        public readonly byte NumBytes;
        public readonly byte NumCycles;
        public readonly AddressingMode Mode;

        public OpCode(byte code, Op name, byte numBytes, byte numCycles, AddressingMode mode) {
            Code = code;
            Name = name;
            NumBytes = numBytes;
            NumCycles = numCycles;
            Mode = mode;
        }
    }

    public enum Op {
        BRK,
        ORA,
        KIL,
        _SLO,
        _NOP,
        ASL,
        PHP,
        ASL_ACC,
        AAC,
        BPL,
        CLC,
        JSR,
        AND,
        _RLA,
        BIT,
        ROL,
        PLP,
        ROL_ACC,
        BMI,
        SEC,
        RTI,
        EOR,
        _SRE,
        LSR,
        PHA,
        LSR_ACC,
        ASR,
        JMP,
        BVC,
        CLI,
        RTS,
        ADC,
        _RRA,
        ROR,
        PLA,
        ROR_ACC,
        ARR,
        BVS,
        SEI,
        STA,
        _SAX,
        STY,
        STX,
        DEY,
        TXA,
        XAA,
        BCC,
        AXA,
        TYA,
        TXS,
        XAS,
        SYA,
        SXA,
        LDY,
        LDA,
        LDX,
        _LAX,
        TAY,
        TAX,
        ATX,
        BCS,
        CLV,
        TSX,
        LAR,
        CPY,
        CMP,
        _DCP,
        DEC,
        INY,
        DEX,
        AXS,
        BNE,
        CLD,
        CPX,
        SBC,
        _ISB,
        INC,
        INX,
        NOP,
        _SBC,
        BEQ,
        SED,
    }
}
