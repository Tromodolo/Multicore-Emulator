using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    public class ScrollRegister {
        (byte X, byte Y) Value;
        bool WriteY;

        public ScrollRegister() {
            Value = (0, 0);
            WriteY = false;
        }

        public byte GetX() {
            return Value.X;
        }

        public byte GetY() {
            return Value.Y;
        }

        public void Update(byte data) {
            if (!WriteY) {
                Value.X = data;
            } else {
                Value.Y = data;
            }

            WriteY = !WriteY;
        }

        public void ResetLatch() {
            WriteY = false;
        }
    }
}
