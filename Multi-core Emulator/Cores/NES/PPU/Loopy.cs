namespace NesEmu.PPU {
    public struct Loopy {
        public byte CoarseX;
        public byte CoarseY;
        public byte NametableX;
        public byte NametableY;
        public byte FineY;

        public void Increment(byte value) {
            ushort current = GetAddress();
            current += value;
            Update(current);
        }

        public void Update(ushort address) {
            FineY =      (byte)((address &  0b111000000000000) >> 12);
            NametableY = (byte)((address &  0b000100000000000) >> 11);
            NametableX = (byte)((address &  0b000010000000000) >> 10);
            CoarseY =    (byte)((address &  0b000001111100000) >> 5);
            CoarseX =    (byte)(address  &  0b000000000011111);
        }

        public ushort GetAddress() {
            return (ushort)(
                FineY << 12 |
                NametableY << 11 |
                NametableX << 10 |
                CoarseY << 5 |
                CoarseX
            );
        }
    }
}
