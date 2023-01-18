using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    [Flags]
    public enum StatusRegisterStatus {
        Empty           = 0,
        SpriteOverflow  = 1 << 5,
        SpriteZeroHit   = 1 << 6,
        VBlank          = 1 << 7,
    }

    public struct StatusRegister {
        StatusRegisterStatus Status;

        public StatusRegister() {
            Status = StatusRegisterStatus.Empty;
        }

        public void SetVBlank(bool enabled) {
            if (enabled) {
                Status |= StatusRegisterStatus.VBlank;
            } else {
                Status &= ~StatusRegisterStatus.VBlank;
            }
        }

        public void SetSpriteOverflow(bool enabled) {
            if (enabled) {
                Status |= StatusRegisterStatus.SpriteOverflow;
            } else {
                Status &= ~StatusRegisterStatus.SpriteOverflow;
            }
        }

        public void SetSpriteZeroHit(bool enabled) {
            if (enabled) {
                Status |= StatusRegisterStatus.SpriteZeroHit;
            } else {
                Status &= ~StatusRegisterStatus.SpriteZeroHit;
            }
        }

        public void ResetVBlank() {
            Status &= ~StatusRegisterStatus.VBlank;
        }

        public bool IsVBlank() {
            return Status.HasFlag(StatusRegisterStatus.VBlank);
        }

        public byte GetSnapshot() {
            return (byte)Status;
        }

        public void Update(byte value) {
            Status = (StatusRegisterStatus)value;
        }
    }
}
