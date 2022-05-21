using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Mapper {
    public class NROM : IMapper {
        Rom.Rom CurrentRom;

        byte[] ChrRom;
        byte[] PrgRom;
        bool Is256;
        bool Handled;

        public void RegisterRom(Rom.Rom rom) {
            CurrentRom = rom;

            ChrRom = rom.ChrRom;
            PrgRom = rom.PrgRom;
            Is256 = PrgRom.Length > 0x4000;
        }

        public bool DidMap() {
            return Handled;
        }

        public byte CpuRead(ushort address) {
            Handled = false;

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

                        Handled = true;
                        return *(ptr + address);
                    }
                }
            }
            return 0;
        }

        public void CpuWrite(ushort address, byte value) {
            Handled = false;

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

                        Handled = true;
                        *(ptr + address) = value;
                    }
                }
            }
        }

        public byte PPURead(ushort address) {
            Handled = false;

            if (address >= 0 && address <= 0x1999) {
                Handled = true;
                return ChrRom[address];
            }
            return 0;
        }

        public void PPUWrite(ushort address, byte value) {
            Handled = false;
        }
    }
}
