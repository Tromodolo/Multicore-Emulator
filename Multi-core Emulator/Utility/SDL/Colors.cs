using System;
namespace MultiCoreEmulator.Utility.SDL;

public class Colors {
    public struct ColorSpec {
        internal int r;
        internal int g;
        internal int b;

        public ColorSpec(int r, int g, int b) {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public bool Equals(ColorSpec other) {
            return r == other.r
                && b == other.b
                && g == other.g;
        }

        public static implicit operator ColorSpec(uint u) {
            return new ColorSpec(
                (int)(u >> 16 & 0xFF),
                (int)(u >> 8 & 0xFF),
                (int)(u & 0xFF)
            );
        }

        public static implicit operator ColorSpec((byte r, byte g, byte b) color) {
            return new ColorSpec(
                color.r,
                color.g,
                color.b
            );
        }
    }
    public static ColorSpec Empty = new ColorSpec(-1, -1, -1);

    public static ColorSpec White = new ColorSpec(255, 255, 255);
    public static ColorSpec Black = new ColorSpec(0, 0, 0);
    public static ColorSpec Gray = new ColorSpec(128, 128, 128);
    public static ColorSpec Green = new ColorSpec(118, 150, 86);
    public static ColorSpec Tan = new ColorSpec(238, 238, 210);
}
