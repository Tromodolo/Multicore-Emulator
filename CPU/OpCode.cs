using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {
    public class OpCode {
        public string Name;
        public byte Code;
        public byte NumBytes;
        public byte NumCycles;
        public AddressingMode Mode;

        public OpCode(byte code, string name, byte numBytes, byte numCycles, AddressingMode mode) {
            Code = code;
            Name = name;
            NumBytes = numBytes;
            NumCycles = numCycles;
            Mode = mode;
        }
    }
}
