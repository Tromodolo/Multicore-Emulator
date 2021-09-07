using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    public class ScrollRegister {
        (byte X, byte Y) Value;
        bool WriteX;

        public ScrollRegister() {
            Value = (0, 0);
            WriteX = true;
        }

        public byte GetX() {
            return Value.X;
        }

        public byte GetY() {
            return Value.Y;
        }

        public void Update(byte data) {
            if (WriteX) {
                Value.X = data;
            } else {
                Value.Y = data;
            }

            WriteX = !WriteX;
        }

        public void ResetLatch() {
            WriteX = true;
        }
    }
}
