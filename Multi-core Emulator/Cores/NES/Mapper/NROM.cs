using NesEmu.Rom;

namespace NesEmu.Mapper {
    public struct NROM : IMapper {
        Rom.Rom CurrentRom;

        byte[] PrgRom;
        bool Is256;

        public void RegisterRom(Rom.Rom rom) {
            CurrentRom = rom;

            PrgRom = rom.PrgRom;
            Is256 = PrgRom.Length > 0x4000;
        }

        public byte CpuRead(ushort address, out bool handled) {
            handled = false;

            if (address >= 0x8000 && address <= 0xffff) {
                unsafe {
                    fixed (byte* ptr = PrgRom) {
                        // Make sure the address lines up with the prg rom
                        address -= 0x8000;

                        if (!Is256) {
                            // If the address is longer than the prg rom, mirror it down
                            if (address >= 0x4000) {
                                address = (ushort)(address % 0x4000);
                            }
                        }

                        handled = true;
                        return *(ptr + address);
                    }
                }
            }
            return 0;
        }

        public void CpuWrite(ushort address, byte value, out bool handled) {
            handled = false;

            if (address >= 0x8000 && address <= 0xffff) {
                unsafe {
                    fixed (byte* ptr = PrgRom) {
                        // Make sure the address lines up with the prg rom
                        address -= 0x8000;

                        if (!Is256) {
                            // If the address is longer than the prg rom, mirror it down
                            if (address >= 0x4000) {
                                address = (ushort)(address % 0x4000);
                            }
                        }

                        handled = true;
                        *(ptr + address) = value;
                    }
                }
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
            writer.Write(PrgRom);
        }

        public void Load(BinaryReader reader) {
            PrgRom = reader.ReadBytes(PrgRom.Length);
        }
    }
}
