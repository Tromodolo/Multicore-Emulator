using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {
    public partial class NesCpu {
        public void MemWrite(ushort address, byte value) {
            Bus.MemWrite(address, value);
        }
        public byte MemRead(ushort address) {
            return Bus.MemRead(address);
        }

        public void MemWriteShort(ushort address, ushort value) {
            Bus.MemWriteShort(address, value);
        }
        public ushort MemReadShort(ushort address) {
            return Bus.MemReadShort(address);
        }
    }
}
