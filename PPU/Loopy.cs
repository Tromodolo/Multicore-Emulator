using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    internal class Loopy {
        public byte CoarseX {
            get;
            set;
        }

        public byte CoarseY {
            get;
            set;
        }

        public byte Nametable {
            get;
            set;
        }

        public byte FineY {
            get;
            set;
        }

        public ushort Address {
            get {
                return GetAddress();
            }
            set {
                Update(value);
            }
        }

        public void Increment(byte value) {
            var current = GetAddress();
            current += value;
            Update(current);
        }

        public void Update(ushort address) {
            FineY =     (byte)((address & 0b111000000000000) >> 12);
            Nametable = (byte)((address & 0b000110000000000) >> 10);
            CoarseY =   (byte)((address & 0b000001111100000) >> 5);
            CoarseX =   (byte)(address & 0b000000000011111);
        }

        public ushort GetAddress() {
            return (ushort)(
                FineY << 12 |
                Nametable << 10 |
                CoarseY << 5 |
                CoarseX
            );
        }
    }
}
