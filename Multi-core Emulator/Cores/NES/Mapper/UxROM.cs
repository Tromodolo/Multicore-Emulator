using NesEmu.Rom;

namespace NesEmu.Mapper {
    public struct UxROM : IMapper {
        Rom.Rom CurrentRom;

        const ushort BANK_SIZE = 0x4000;

        byte CurrentBank;
        int LastBankAddr;

        byte[] PrgRom;

        public void RegisterRom(Rom.Rom rom) {
            CurrentRom = rom;
            PrgRom = rom.PrgRom;
            CurrentBank = 0;
            LastBankAddr = PrgRom.Length - BANK_SIZE;
        }

        public byte CpuRead(ushort address, out bool handled) {
            handled = false;
            if (address >= 0x8000 && address < 0xC000) {
                handled = true;
                return PrgRom[(CurrentBank * BANK_SIZE) + (address - 0x8000)];
            } else if (address >= 0xC000 && address <= 0xFFFF) {
                handled = true;
                return PrgRom[LastBankAddr + (address - 0xC000)];
            }
            return 0;
        }

        public void CpuWrite(ushort address, byte value, out bool handled) {
            handled = false;
            if (address >= 0x8000 && address <= 0xffff) {
                handled = true;
                CurrentBank = (byte)(value & 0b1111);
            }
        }

        public byte PPURead(ushort address, out bool handled) {
            handled = false;
            return 0;
        }

        public void PPUWrite(ushort address, byte value, out bool handled) {
            handled = false;
        }

        public ScreenMirroring GetMirroring() {
            return CurrentRom.Mirroring;
        }

        public void Persist() {
            return;
        }

        public void Save(BinaryWriter writer) {
            writer.Write(CurrentBank);
            writer.Write(LastBankAddr);
            writer.Write(PrgRom);
        }

        public void Load(BinaryReader reader) {
            CurrentBank = reader.ReadByte();
            LastBankAddr = reader.ReadInt32();
            PrgRom = reader.ReadBytes(PrgRom.Length);
        }
    }
}
