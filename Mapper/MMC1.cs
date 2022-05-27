using NesEmu.Rom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Mapper {
    public class MMC1 : IMapper {
        Rom.Rom CurrentRom;

        const ushort PRG_SIZE = 0x4000;
        const ushort CHR_SIZE = 0x1000;

        byte[] ChrRom;
        byte[] PrgRom;
        byte[] PrgRam;

        bool HasPrgRam;

        int PrgRomBank1;
        int PrgRomBank2;
        int ChrRomBank1;
        int ChrRomBank2;

        int ChrRomOffset1;
        int ChrRomOffset2;

        int PrgMode;
        int ChrMode;

        int ShiftRegister;
        int ShiftCount;

        int CurrentPC;
        int LastPC;

        int LastControl;

        ScreenMirroring CurrentMirroring;

        bool Handled;

        public void RegisterRom(Rom.Rom rom) {
            CurrentRom = rom;

            ChrRom = rom.ChrRom;
            PrgRom = rom.PrgRom;
            PrgRam = rom.PrgRam;
            CurrentMirroring = rom.Mirroring;
            HasPrgRam = PrgRam.Length > 0;

            PrgRomBank1 = 0;
            PrgRomBank2 = (PrgRom.Length - 0x4000) / 0x4000;

            ChrRomBank1 = 0;
            ChrRomBank2 = 1;

            ShiftRegister = 0;
            ShiftCount = 0;
        }

        public bool DidMap() {
            return Handled;
        }

        public void SetProgramCounter(int pc) {
            CurrentPC = pc;
        }

        public void SetScanline(int scanline) { }

        public byte CpuRead(ushort address) {
            Handled = false;
            if (address >= 0x6000 && address < 0x8000) {
                Handled = true;
                return PrgRam[address - 0x6000];
            } else if (address >= 0x8000 && address < 0xC000) {
                Handled = true;
                return PrgRom[(PrgRomBank1 * PRG_SIZE) + (address - 0x8000)];
            } else if (address >= 0xC000 && address <= 0xFFFF) {
                Handled = true;
                return PrgRom[(PrgRomBank2 * PRG_SIZE) + (address - 0xC000)];
            }
            return 0;
        }

        public void CpuWrite(ushort address, byte value) {
            Handled = false;

            if (address >= 0x6000 && address < 0x8000) {
                Handled = true;
                PrgRam[address - 0x6000] = value;
            } else if (address >= 0x8000 && address <= 0xFFFF) {
                if (LastPC == CurrentPC) {
                    return;
                }
                Handled = true;
                LastPC = CurrentPC;

                if (((value >> 7) & 1) == 1) {
                    //Console.WriteLine($"{address.ToString("X")} {Convert.ToString(value, 2).PadLeft(8, '0')} SR: {ShiftRegister}   SC: {ShiftCount} RESET");
                    ShiftRegister = 0;
                    ShiftCount = 0;

                    SetControl(LastControl | 0x0c);
                } else {
                    if (ShiftCount == 4) {
                        //Console.WriteLine($"{address.ToString("X")} {Convert.ToString(value, 2).PadLeft(8, '0')} SR: {ShiftRegister}   SC: {ShiftCount} REGISTER");
                        ShiftRegister >>= 1;
                        ShiftRegister |= ((value & 1) << 4);

                        value = (byte)(ShiftRegister & 0b11111);
                        ShiftRegister = 0;
                        ShiftCount = 0;

                        if (address >= 0x8000 && address <= 0x9FFF)  { // Control
                            SetControl(value);
                        } else if (address >= 0xA000 && address <= 0xBFFF) { // CHR bank 0
                            ChrRomBank1 = value;
                        } else if (address >= 0xC000 && address <= 0xDFFF) { // CHR bank 1
                            ChrRomBank2 = value;
                        } else if (address >= 0xE000 && address <= 0xFFFF) { // PRG bank
                            UpdatePrgBanks(value);
                        }
                    } else {
                        //Console.WriteLine($"{address.ToString("X")} {Convert.ToString(value, 2).PadLeft(8, '0')} SR: {ShiftRegister}   SC: {ShiftCount}");
                        ShiftRegister >>= 1;
                        ShiftRegister |= ((value & 1) << 4);
                        ShiftCount++;
                    }
                }
            }

            UpdateChrBanks();
        }

        public byte PPURead(ushort address) {
            Handled = false;
            if (address >= 0 && address < 0x1000) {
                Handled = true;
                return ChrRom[ChrRomOffset1 + address];
            } else if (address >= 0x1000 && address <= 0x1FFF) {
                Handled = true;
                return ChrRom[ChrRomOffset2 + (address - 0x1000)];
            }
            return 0;
        }

        public void PPUWrite(ushort address, byte value) {
            Handled = false;
            if (address >= 0 && address < 0x1000) {
                Handled = true;
                ChrRom[ChrRomOffset1 + address] = value;
            } else if (address >= 0x1000 && address <= 0x1FFF) {
                Handled = true;
                ChrRom[ChrRomOffset2 + (address - 0x1000)] = value;
            }
        }

        public ScreenMirroring GetMirroring() {
            return CurrentMirroring;
        }

        private void SetControl(int value) {
            LastControl = value;
            var mirroring = (value & 0b11);
            switch (mirroring) {
                case 0:
                    CurrentMirroring = ScreenMirroring.OneScreenLower;
                    break;
                case 1:
                    CurrentMirroring = ScreenMirroring.OneScreenUpper;
                    break;
                case 2:
                    CurrentMirroring = ScreenMirroring.Vertical;
                    break;
                case 3:
                    CurrentMirroring = ScreenMirroring.Horizontal;
                    break;
            }
            PrgMode = (value & 0b1100) >> 2;
            ChrMode = (value & 0b10000) >> 4;

            UpdateChrBanks();
            UpdatePrgBanks(PrgRomBank1);
        }

        private void UpdateChrBanks() {
            if (ChrMode == 0) {
                var bank1 = ChrRomBank1;
                bank1 &= ~1;
                bank1 *= CHR_SIZE;
                ChrRomOffset1 = bank1;
                ChrRomOffset2 = ChrRomBank1 + CHR_SIZE;
            } else {
                ChrRomOffset1 = ChrRomBank1 * CHR_SIZE;
                ChrRomOffset2 = ChrRomBank2 * CHR_SIZE;
            }
        }

        private void UpdatePrgBanks(int value) {
            HasPrgRam = (value & 0b10000) > 0;
            value &= 0b11111;
            if (PrgMode == 0 || PrgMode == 1) {
                PrgRomBank1 = value;
                PrgRomBank2 = value + 1;
            } else if (PrgMode == 2) {
                PrgRomBank1 = 0;
                PrgRomBank2 = value;
            } else if (PrgMode == 3) {
                PrgRomBank1 = value;
                PrgRomBank2 = (PrgRom.Length - 0x4000) / 0x4000;
            }
        }
    }
}
