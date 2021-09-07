using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Rom {
    [Flags]
    public enum ControlRegisterStatus {
        Empty                   = 0,
        NameTable1              = 1,
        NameTable2              = 1 << 1,
        VramAddIncrement        = 1 << 2,
        SpritePatternAddr       = 1 << 3,
        BackgroundPatternAddr   = 1 << 4,
        SpriteSize              = 1 << 5,
        MasterSlaveSelect       = 1 << 6,
        GenerateNMI             = 1 << 7
    }

    public class ControlRegister {
        ControlRegisterStatus Status;

        public ControlRegister() {
            Status = ControlRegisterStatus.Empty;
        }

        public byte GetVramAddrIncrement() {
            if (Status.HasFlag(ControlRegisterStatus.VramAddIncrement)) {
                return 32;
            } else {
                return 1;
            }
        }

        public ushort GetBackgroundPatternAddr() {
            if (Status.HasFlag(ControlRegisterStatus.VramAddIncrement)) {
                return 0x1000;
            } else {
                return 0;
            }
        }

        public ushort GetSpritePatternAddr() {
            if (Status.HasFlag(ControlRegisterStatus.VramAddIncrement)) {
                return 0x1000;
            } else {
                return 0;
            }
        }

        //public ushort GetSpriteSize() {

        //}

        public bool ShouldGenerateVBlank() {
            return Status.HasFlag(ControlRegisterStatus.GenerateNMI);
        }

        public void Update(byte data) {
            Status = (ControlRegisterStatus)data;
        }
    }
}
