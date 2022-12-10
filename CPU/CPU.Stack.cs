using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.CPU {
    public partial class NesCpu {
        public void StackPush(byte value) {
            MemWrite((ushort)(StackStart + StackPointer), value);
            StackPointer--;
        }
        
        public byte StackPop() {
            StackPointer++;
            byte value = MemRead((ushort)(StackStart + StackPointer));
            return value;
        }

        public void StackPushShort(ushort value) {
            var hi = (byte)(value >> 8);
            var lo = (byte)(value & 0xff);
            StackPush(hi);
            StackPush(lo);
        }
        
        public ushort StackPopShort() {
            byte lo = StackPop();
            byte hi = StackPop();
            return (ushort)(hi << 8 | lo);
        }
    }
}
