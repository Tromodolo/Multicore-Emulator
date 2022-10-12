using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

    public struct ControlRegister {
        byte Status;

        public ControlRegister() {
            //Status = ControlRegisterStatus.Empty;
            Status = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetVramAddrIncrement() {
            if ((Status & (1 << 2)) > 1) {
                return 32;
            } else {
                return 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetBackgroundPatternAddr() {
            if ((Status & (1 << 4)) > 1) {
                return 0x1000;
            } else {
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetSpritePatternAddr() {
            if ((Status & (1 << 3)) > 1) {
                return 0x1000;
            } else {
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte GetSpriteSize() {
            if ((Status & (1 << 5)) > 1) {
                return 16;
            } else {
                return 8;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MasterSlaveSelect() {
            if ((Status & (1 << 6)) > 1) {
                return 1;
            } else {
                return 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldGenerateVBlank() {
            return (Status & (1 << 7)) > 1;
        }

        public byte Get() {
            return Status;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Update(byte data) {
            Status = data;
        }
    }
}
