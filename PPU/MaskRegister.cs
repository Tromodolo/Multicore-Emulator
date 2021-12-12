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
            return Status.HasFlag(MaskRegisterStatus.Greyscale);
        }
        public bool GetBackgroundLeftColumn() {
            return Status.HasFlag(MaskRegisterStatus.BackgroundLeftColumnEnable);
        }
        public bool GetBackground() {
            return Status.HasFlag(MaskRegisterStatus.BackgroundEnable);
        }
        public bool GetSpriteLeftColumn() {
            return Status.HasFlag(MaskRegisterStatus.SpriteLeftColumnEnable);
        }
        public bool GetSprite() {
            return Status.HasFlag(MaskRegisterStatus.SpriteEnable);
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
