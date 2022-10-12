using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Rom {
    public struct AddrRegister {
        (byte Hi, byte Lo) Value;
        bool HiPtr;

        public AddrRegister() {
            Value = (0, 0);
            HiPtr = true;
        }

        void Set(ushort data) {
            Value.Hi = (byte)(data >> 8);
            Value.Lo = (byte)(data & 0xff);
        }

        public ushort Get() {
            return (ushort)((ushort)(Value.Hi << 8) | Value.Lo);
        }

        public void Update(byte data) {
            if (HiPtr) {
                Value.Hi = data;
            } else {
                Value.Lo = data;
            }

            if (Get() > 0x3fff) {
                Set((ushort)(Get() & 0b11111111111111));
            }

            HiPtr = !HiPtr;
        }
        
        public void Increment(byte inc) {
            byte lo = Value.Lo;
            Value.Lo += inc;

            // If it is smaller, it must have wrapped
            if (lo > Value.Lo) {
                Value.Hi++;
            }

            if (Get() > 0x3fff) {
                Set((ushort)(Get() & 0b11111111111111));
            }
        }

        public void ResetLatch() {
            HiPtr = true;
        }
    }
}
