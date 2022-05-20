using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {
    public partial class NesCpu {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MemWrite(ushort address, byte value) {
            Bus.MemWrite(address, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MemRead(ushort address) {
            return Bus.MemRead(address);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MemWriteShort(ushort address, ushort value) {
            Bus.MemWriteShort(address, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort MemReadShort(ushort address) {
            return Bus.MemReadShort(address);
        }
    }
}
