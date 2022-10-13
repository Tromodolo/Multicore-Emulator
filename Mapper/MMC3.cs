using NesEmu.Rom;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Mapper {
    public struct MMC3 : IMapper {
        Rom.Rom CurrentRom;

        const ushort PRG_SIZE = 0x2000;
        const ushort CHR_SIZE = 0x400;

        byte[] ChrRom;
        byte[] PrgRom;
        byte[] PrgRam;
        bool HasPrgRam;

        bool PrgMode;
        bool ChrMode;

        int NextBankUpdate;
        int[] BankRegisters;
        int[] PrgOffsets;
        int LastBank;

        int IRQLatch;
        int IRQCounter;
        bool IRQEnabled;
        bool IRQReload;

        bool IRQPending;

        ScreenMirroring CurrentMirroring;

        bool Handled;
        int LastAddress;

        FileStream savefile;

        public void RegisterRom(Rom.Rom rom) {
            CurrentRom = rom;

            ChrRom = rom.ChrRom;
            PrgRom = rom.PrgRom;
            PrgRam = rom.PrgRam;
            CurrentMirroring = rom.Mirroring;
            HasPrgRam = PrgRam.Length > 0;

            LastBank = (byte)((PrgRom.Length - PRG_SIZE) / PRG_SIZE);

            BankRegisters = new int[8];
            PrgOffsets = new int[4];

            //var gameName = rom.Filename.Split('\\').LastOrDefault();
            //savefile = new FileStream($"{gameName}.sav", FileMode.OpenOrCreate);
            //using MemoryStream ms = new MemoryStream();
            //int read;
            //while ((read = savefile.Read(PrgRam, 0, PrgRam.Length)) > 0) {
            //    ms.Write(PrgRam, 0, read);
            //}
            //var readArr = ms.ToArray();
            //if (readArr.Length > 0) {
            //    PrgRam = ms.ToArray();
            //}
        }

        public void Persist() {
            //if (savefile != null) {
            //    savefile.Seek(0, SeekOrigin.Begin);
            //    savefile.Write(PrgRam);
            //    savefile.Flush();
            //    savefile.Close();
            //    savefile = null;
            //}
        }

        public bool DidMap() {
            return Handled;
        }

        public int MappedAddress() { return LastAddress; }

        public void DecrementScanline() {
            if (IRQCounter == 0 || IRQReload) {
                IRQCounter = IRQLatch;
            } else {
                IRQCounter--;
            }

            if (IRQCounter == 0 && IRQEnabled) {
                IRQPending = true;
            }

            IRQReload = false;
        }

        public bool GetIRQ() {
            return IRQPending;
        }

        public byte CpuRead(ushort address) {
            Handled = false;

            if (address >= 0x6000 && address < 0x8000 && HasPrgRam) {
                Handled = true;
                LastAddress = address - 0x6000;
                return PrgRam[LastAddress + (address % PRG_SIZE)];
            } else if(address >= 0x8000 && address < 0xffff) {
                Handled = true;
                var index = (int)((address - 0x8000) / PRG_SIZE);
                LastAddress = PrgOffsets[index];
                return PrgRom[LastAddress + (address % PRG_SIZE)];
            }

            return 0;

            //if (address >= 0x6000 && address < 0x8000 && PrgRam.Length > 0) {
            //    Handled = true;
            //    LastAddress = address - 0x6000;
            //    return PrgRam[LastAddress];
            //} else if (address >= 0x8000 && address <= 0xFFFF) {
            //    Handled = true;

            //    //var index = (address - 0x8000) / PRG_SIZE;
            //    //LastAddress = prgBanks[index] * PRG_SIZE + (address % PRG_SIZE);
            //    //return PrgRom[LastAddress];

            //    if (address >= 0x8000 && address < 0x9FFF) {
            //        return PrgRom[prgBanks[0] * PRG_SIZE + (address % PRG_SIZE)];
            //    } else if (address >= 0xA000 && address < 0xBFFF) {
            //        return PrgRom[prgBanks[1] * PRG_SIZE + (address % PRG_SIZE)];
            //    } else if (address >= 0xC000 && address < 0xDFFF) {
            //        return PrgRom[prgBanks[2] * PRG_SIZE + (address % PRG_SIZE)];
            //    } else if (address >= 0xE000 && address <= 0xFFFF) {
            //        return PrgRom[prgBanks[3] * PRG_SIZE + (address % PRG_SIZE)];
            //    }
            //}
            //return 0;
        }

        public void CpuWrite(ushort address, byte value) {
            Handled = false;

            if (address >= 0x6000 && address <= 0x8000 && HasPrgRam) {

            } else {
                Handled = true;
                switch (address & 0x6001) {
                    case 0x0000: // Bank Select
                        NextBankUpdate = value & 0b111;
                        PrgMode = (value & 0b01000000) > 0;
                        ChrMode = (value & 0b10000000) > 0;
                        break;
                    case 0x0001: // Bank Data
                        BankRegisters[NextBankUpdate] = value;
                        break;
                    case 0x2000: // Mirroring
                        if (CurrentMirroring == ScreenMirroring.FourScreen) {
                            return;
                        }
                        CurrentMirroring = (value & 1) == 0
                            ? ScreenMirroring.Vertical
                            : ScreenMirroring.Horizontal;
                        break;
                    case 0x2001: // Prg Ram Flags
                        //7  bit  0
                        //--------
                        //RWXX xxxx
                        //||||
                        //|| ++------Nothing on the MMC3, see MMC6
                        //| +--------Write protection(0: allow writes; 1: deny writes)
                        //+---------PRG RAM chip enable(0: disable; 1: enable)
                        //Disabling PRG RAM through bit 7 causes reads from the PRG RAM region to return open bus.
                        //Though these bits are functional on the MMC3, their main purpose is to write - protect save RAM during power-off.Many emulators choose not to implement them as part of iNES Mapper 4 to avoid an incompatibility with the MMC6.
                        //See iNES Mapper 004 and MMC6 below.
                        break;
                    case 0x4000: // IRQ Latch
                        IRQLatch = value;
                        break;
                    case 0x4001: // IRQ Reload
                        IRQCounter = 0;
                        IRQReload = true;
                        break;
                    case 0x6000: // IRQ Disable
                        IRQEnabled = false;
                        IRQPending = false;
                        break;
                    case 0x6001: // IRQ Enable
                        IRQEnabled = true;
                        break;
                    default:
                        Handled = false;
                        break;
                }
            }

            if (Handled) {
                UpdateOffsets();
            }
            //if (address >= 0x6000 && address < 0x8000 && PrgRam.Length > 0) {
            //    Handled = true;
            //    PrgRam[address - 0x6000] = value;
            //    Persist();
            //    return;
            //} else if (address % 2 == 0) { // Evens
            //    if (address >= 0x8000 && address <= 0x9FFF) { // Bank Select
            //        Handled = true;
            //        NextBankUpdate = value & 0b111;
            //        PrgRomBankMode = value & 0b01000000;
            //        ChrRomBankMode = value & 0b10000000;
            //    } else if (address >= 0xA000 && address <= 0xBFFF) { // Mirroring
            //        Handled = true;
            //        if (CurrentMirroring == ScreenMirroring.FourScreen) {
            //            return;
            //        }
            //        CurrentMirroring = (value & 1) == 0
            //            ? ScreenMirroring.Vertical
            //            : ScreenMirroring.Horizontal;
            //    } else if (address >= 0xC000 && address <= 0xDFFF) { // IRQ Latch
            //        Handled = true;
            //        IRQLatch = value;
            //    } else if (address >= 0xE000 && address <= 0xFFFF) { // IRQ Disable
            //        Handled = true;
            //        IRQEnabled = false;
            //        IRQPending = false;
            //    }
            //} else { // Odds
            //    if (address >= 0x8000 && address <= 0x9FFF) { // Bank Data
            //        Handled = true;
            //        switch (NextBankUpdate) {
            //            case 0:
            //                ChrRomBank1 = value & 0b11111110;
            //                break;
            //            case 1:
            //                ChrRomBank2 = value & 0b11111110;
            //                break;
            //            case 2:
            //                ChrRomBank3 = value;
            //                break;
            //            case 3:
            //                ChrRomBank4 = value;
            //                break;
            //            case 4:
            //                ChrRomBank5 = value;
            //                break;
            //            case 5:
            //                ChrRomBank6 = value;
            //                break;
            //            case 6:
            //                PrgRomBank1 = value & 0b00111111;
            //                break;
            //            case 7:
            //                PrgRomBank2 = value & 0b00111111;
            //                break;
            //            default:
            //                break;
            //        }
            //    } else if (address >= 0xA000 && address <= 0xBFFF) { // PRG Ram Flags
            //        Handled = true;
            //        RamWriteEnabled = (value & 0b01000000) == 0;
            //        //7  bit  0
            //        //--------
            //        //RWXX xxxx
            //        //||||
            //        //|| ++------Nothing on the MMC3, see MMC6
            //        //| +--------Write protection(0: allow writes; 1: deny writes)
            //        //+---------PRG RAM chip enable(0: disable; 1: enable)
            //        //Disabling PRG RAM through bit 7 causes reads from the PRG RAM region to return open bus.
            //        //Though these bits are functional on the MMC3, their main purpose is to write - protect save RAM during power-off.Many emulators choose not to implement them as part of iNES Mapper 4 to avoid an incompatibility with the MMC6.
            //        //See iNES Mapper 004 and MMC6 below.
            //    } else if (address >= 0xC000 && address <= 0xDFFF) { // IRQ Reload
            //        Handled = true;
            //        IRQCounter = 0;
            //        IRQReload = true;
            //    } else if (address >= 0xE000 && address <= 0xFFFF) { // IRQ Enable
            //        Handled = true;
            //        IRQEnabled = true;
            //    }
            //}

            //SyncPrgBanks();
        }

        //private void SyncPrgBanks() {
        //    if (PrgRomBankMode == 0) {
        //        prgBanks[0] = PrgRomBank1;
        //        prgBanks[1] = PrgRomBank2;
        //        prgBanks[2] = (byte)(LastBank - 1);
        //        prgBanks[3] = LastBank;
        //    } else {
        //        prgBanks[0] = (byte)(LastBank - 1);
        //        prgBanks[1] = PrgRomBank2;
        //        prgBanks[2] = PrgRomBank1;
        //        prgBanks[3] = LastBank;
        //    }
        //}

        public byte PPURead(ushort address) {
            Handled = false;
            return ChrRom[address % PRG_SIZE];
            //if (address >= 0 && address < 0x1000) {
            //    Handled = true;
            //    return ChrRom[ChrRomOffset1 + address];
            //} else if (address >= 0x1000 && address <= 0x1FFF) {
            //    Handled = true;
            //    return ChrRom[ChrRomOffset2 + (address - 0x1000)];
            //}
            //return 0;
        }

        public void PPUWrite(ushort address, byte value) {
            Handled = false;
            //if (address >= 0 && address < 0x1000) {
            //    Handled = true;
            //    ChrRom[ChrRomOffset1 + address] = value;
            //} else if (address >= 0x1000 && address <= 0x1FFF) {
            //    Handled = true;
            //    ChrRom[ChrRomOffset2 + (address - 0x1000)] = value;
            //}
        }

        private void UpdateOffsets() {
            if (PrgMode) {
                PrgOffsets[0] = (LastBank - 1) * PRG_SIZE;
                PrgOffsets[1] = BankRegisters[7] * PRG_SIZE;
                PrgOffsets[2] = BankRegisters[6] * PRG_SIZE;
                PrgOffsets[3] = LastBank * PRG_SIZE;
            } else {
                PrgOffsets[0] = BankRegisters[6] * PRG_SIZE;
                PrgOffsets[1] = BankRegisters[7] * PRG_SIZE;
                PrgOffsets[2] = (LastBank - 1) * PRG_SIZE;
                PrgOffsets[3] = LastBank * PRG_SIZE;
            }
        }

        public ScreenMirroring GetMirroring() {
            return CurrentMirroring;
        }

        public void Save(BinaryWriter writer) {
            //writer.Write(HasPrgRam);
            //writer.Write(PrgRomBank1);
            //writer.Write(PrgRomBank2);
            //writer.Write(ChrRomBank1);
            //writer.Write(ChrRomBank2);
            //writer.Write(ChrRomOffset1);
            //writer.Write(ChrRomOffset2);
            //writer.Write(PrgMode);
            //writer.Write(ChrMode);
            //writer.Write(ShiftRegister);
            //writer.Write(ShiftCount);
            //writer.Write(CurrentPC);
            //writer.Write(LastPC);
            //writer.Write(LastControl);
            //writer.Write((int)CurrentMirroring);
            //writer.Write(ChrRom);
            //writer.Write(PrgRom);
            //writer.Write(PrgRam);
        }

        public void Load(BinaryReader reader) {
            //HasPrgRam = reader.ReadBoolean();
            //PrgRomBank1 = reader.ReadInt32();
            //PrgRomBank2 = reader.ReadInt32();
            //ChrRomBank1 = reader.ReadInt32();
            //ChrRomBank2 = reader.ReadInt32();
            //ChrRomOffset1 = reader.ReadInt32();
            //ChrRomOffset2 = reader.ReadInt32();
            //PrgMode = reader.ReadInt32();
            //ChrMode = reader.ReadInt32();
            //ShiftRegister = reader.ReadInt32();
            //ShiftCount = reader.ReadInt32();
            //CurrentPC = reader.ReadInt32();
            //LastPC = reader.ReadInt32();
            //LastControl = reader.ReadInt32();
            //CurrentMirroring = (ScreenMirroring)reader.ReadInt32();
            //ChrRom = reader.ReadBytes(ChrRom.Length);
            //PrgRom = reader.ReadBytes(PrgRom.Length);
            //PrgRam = reader.ReadBytes(PrgRam.Length);
        }
    }
}
