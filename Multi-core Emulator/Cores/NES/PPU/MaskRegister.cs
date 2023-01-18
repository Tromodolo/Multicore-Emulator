using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    [Flags]
    public enum MaskRegisterStatus {
        Empty                       = 0,
        Greyscale                   = 1,
        BackgroundLeftColumnEnable  = 1 << 1,
        SpriteLeftColumnEnable      = 1 << 2,
        BackgroundEnable            = 1 << 3,
        SpriteEnable                = 1 << 4,
        EmphasisRed                 = 1 << 5,
        EmphasisGreen               = 1 << 6,
        EmphasisBlue                = 1 << 7
    }

    public enum Color {
        Red,
        Blue,
        Green
    }

    public struct MaskRegister {
        byte Status;

        public MaskRegister() {
            Status = 0;
        }

        public bool GetGreyscale() {
            return (Status & 1) > 0;
        }

        public bool GetBackgroundLeftColumn() {
            return (Status & (1 << 1)) > 0;
        }

        public bool GetBackground() {
            return (Status & (1 << 3)) > 0;
        }

        public bool GetSpriteLeftColumn() {
            return (Status & (1 << 2)) > 0;
        }

        public bool GetSprite() {
            return (Status & (1 << 4)) > 0;
        }

        public IEnumerable<Color> GetEmphasis() {
            var colors = new List<Color>();

            if ((Status & (1 << 5)) > 0) {
                colors.Add(Color.Red);
            }
            if ((Status & (1 << 6)) > 0) {
                colors.Add(Color.Green);
            }
            if ((Status & (1 << 7)) > 0) {
                colors.Add(Color.Blue);
            }

            return colors;
        }

        public byte Get() {
            return Status;
        }

        public void Update(byte data) {
            Status = data;
        }
    }
}
