namespace NesEmu.CPU {
    public partial class NesCpu {
        public void StackPush(byte value) {
            Bus.MemWrite((ushort)(StackStart + StackPointer), value);
            StackPointer--;
        }
        
        public byte StackPop() {
            StackPointer++;
            byte value = Bus.MemRead((ushort)(StackStart + StackPointer));
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
