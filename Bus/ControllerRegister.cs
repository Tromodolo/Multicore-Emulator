using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Bus {
    public struct ControllerRegister {
        byte CurrentButtons;
        byte ButtonLatch;
    
        public ControllerRegister() {
            CurrentButtons = 0b00000000;
            ButtonLatch = 0;
        }

        public void Update(byte newState) { 
            CurrentButtons = newState;
        }

        public byte GetAllButtons() {
            return CurrentButtons;
        }

        // Order:
        // A, B, Select, Start, Up, Down, Left, Right
        public byte ReadNextButton() {
            byte unmasked =  (byte)(CurrentButtons >> (7 - ButtonLatch));
            ButtonLatch++;
            return (byte)(unmasked & 0b1);
        }

        public void ResetLatch() {
            ButtonLatch = 0;
        }
    }
}
