using System;
using System.Collections.Generic;
using System.Linq;
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

    public class MaskRegister {
        MaskRegisterStatus Status;

        public MaskRegister() {
            Status = MaskRegisterStatus.Empty;
        }

        public bool GetGreyscale() {
            return ((int)Status & 1) > 0;
        }
        public bool GetBackgroundLeftColumn() {
            return ((int)Status & (1 << 1)) > 0;
        }
        public bool GetBackground() {
            return ((int)Status & (1 << 3)) > 0;
        }
        public bool GetSpriteLeftColumn() {
            return ((int)Status & (1 << 2)) > 0;
        }
        public bool GetSprite() {
            return ((int)Status & (1 << 4)) > 0;
        }
        public IEnumerable<Color> GetEmphasis() {
            var colors = new List<Color>();

            if (Status.HasFlag(MaskRegisterStatus.EmphasisRed)) {
                colors.Add(Color.Red);
            }
            if (Status.HasFlag(MaskRegisterStatus.EmphasisGreen)) {
                colors.Add(Color.Green);
            }
            if (Status.HasFlag(MaskRegisterStatus.EmphasisBlue)) {
                colors.Add(Color.Blue);
            }

            return colors;
        }
        public void Update(byte data) {
            Status = (MaskRegisterStatus)data;
        }
    }
}
