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

        public ushort GetNameTableAddress() {
            var status = (byte)Status;
            return (status & 0b11) switch {
                0 => 0x2000,
                1 => 0x2400,
                2 => 0x2800,
                3 => 0x2c00,
                _ => 0x2000,
            };
        }

        public byte GetVramAddrIncrement() {
            if (((int)Status & (1 << 2)) > 1) {
                return 32;
            } else {
                return 1;
            }
        }

        public ushort GetBackgroundPatternAddr() {
            if (((int)Status & (1 << 4)) > 1) {
                return 0x1000;
            } else {
                return 0;
            }
        }

        public ushort GetSpritePatternAddr() {
            if (((int)Status & (1 << 3)) > 1) {
                return 0x1000;
            } else {
                return 0;
            }
        }

        public byte GetSpriteSize() {
            if (((int)Status & (1 << 5)) > 1) {
                return 16;
            } else {
                return 8;
            }
        }

        public byte MasterSlaveSelect() {
            if (((int)Status & (1 << 6)) > 1) {
                return 1;
            } else {
                return 0;
            }
        }

        public bool ShouldGenerateVBlank() {
            return ((int)Status & (1 << 7)) > 1;
        }

        public void Update(byte data) {
            Status = (ControlRegisterStatus)data;
        }
    }
}
