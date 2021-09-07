using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {
    public class OpCode {
        public string Name { get; set; }
        public byte Code { get; set; }
        public byte NumBytes { get; set; }
        public byte NumCycles { get; set; }
        public AddressingMode Mode { get; set; }

        public OpCode(byte code, string name, byte numBytes, byte numCycles, AddressingMode mode) {
            Code = code;
            Name = name;
            NumBytes = numBytes;
            NumCycles = numCycles;
            Mode = mode;
        }
    }
}
