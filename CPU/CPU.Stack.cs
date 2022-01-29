using System;
using System.Collections.Generic;
using System.Linq;
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
            var value = MemRead((ushort)(StackStart + StackPointer));
            return value;
        }

        public void StackPushShort(ushort value) {
            byte hi = (byte)(value >> 8);
            byte lo = (byte)(value & 0xff);
            StackPush(hi);
            StackPush(lo);
        }
        public ushort StackPopShort() {
            var lo = StackPop();
            var hi = StackPop();
            return (ushort)(hi << 8 | lo);
        }
    }
}
