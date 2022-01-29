using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    public class ScrollRegister {
        (byte X, byte Y) Value;
        bool Latch;

        public ScrollRegister() {
            Value = (0, 0);
            Latch = false;
        }

        public byte GetX() {
            return Value.X;
        }

        public byte GetY() {
            return Value.Y;
        }

        public void Update(byte data) {
            if (!Latch) {
                Value.X = data;
            } else {
                Value.Y = data;
            }

            Latch = !Latch;
        }

        public void ResetLatch() {
            Latch = false;
        }
    }
}
