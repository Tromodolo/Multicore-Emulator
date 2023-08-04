using NesEmu.Rom;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        int[] ChrOffsets;
        int LastBank;

        int IRQLatch;
        int IRQCounter;
        bool IRQEnabled;
        bool IRQReload;

        bool IRQPending;

        ScreenMirroring CurrentMirroring;

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
            ChrOffsets = new int[8];

            UpdateOffsets();

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

        public void SetIRQ(bool IRQ) {
            IRQPending = IRQ;
        }

        public byte CpuRead(ushort address, out bool handled) {
            handled = false;

            if (address >= 0x6000 && address < 0x8000 && HasPrgRam) {
                handled = true;
                LastAddress = address - 0x6000;
                return PrgRam[LastAddress];
            } else if (address >= 0x8000 && address <= 0xffff) {
                handled = true;
                var index = (int)((address - 0x8000) / PRG_SIZE);
                LastAddress = PrgOffsets[index];
                return PrgRom[LastAddress + (address % PRG_SIZE)];
            }

            return 0;
        }

        public void CpuWrite(ushort address, byte value, out bool handled) {
            handled = false;

            if (address >= 0x6000 && address < 0x8000 && HasPrgRam) {
                handled = true;
                PrgRam[address - 0x6000] = value;
            } else if (address >= 0x8000 && address <= 0xFFFF) {
                handled = true;
                bool isEven = address % 2 == 0;
                if (address >= 0x8000 && address < 0xA000) {
                    if (isEven) { // Bank Select
                        NextBankUpdate = value & 0b111;
                        PrgMode = (value & 0x40) == 0x40; // Bit 6
                        ChrMode = (value & 0x80) == 0x80; // Bit 7
                    } else { // Bank Data
                        BankRegisters[NextBankUpdate] = value;
                    }
                } else if (address >= 0xA000 && address < 0xC000) {
                    if (isEven) { // Mirroring
                        if (CurrentMirroring == ScreenMirroring.FourScreen) {
                            return;
                        }
                        CurrentMirroring = (value & 1) == 0
                            ? ScreenMirroring.Vertical
                            : ScreenMirroring.Horizontal;
                    } else { // Prg Ram Flags
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
                    }
                } else if (address >= 0xC000 && address < 0xE000) {
                    if (isEven) { // IRQ Latch
                        IRQLatch = value;
                    } else { // IRQ Reload
                        IRQCounter = 0;
                        IRQReload = true;
                    }
                } else if (address >= 0xE000 && address < 0xFFFF) {
                    if (isEven) { // IRQ Disable
                        IRQEnabled = false;
                        IRQPending = false;
                    } else { // IRQ Enable
                        IRQEnabled = true;
                    }
                }
            }

            if (handled) {
                UpdateOffsets();
            }
        }

        public byte PPURead(ushort address, out bool handled) {
            handled = false;
            if (address >= 0x0000 && address <= 0x1FFF) {
                handled = true;
                var index = address / CHR_SIZE;
                return ChrRom[ChrOffsets[index] + (address % CHR_SIZE)];
            }
            return 0;
        }

        public void PPUWrite(ushort address, byte value, out bool handled) {
            handled = false;
            if (address >= 0x0000 && address <= 0x1FFF) {
                handled = true;
                var index = address / CHR_SIZE;
                ChrRom[ChrOffsets[index] + (address % CHR_SIZE)] = value;
            }
        }

        private void UpdateOffsets() {
            if (PrgMode) {
                PrgOffsets[0] = LastBank - 1;
                PrgOffsets[1] = BankRegisters[7];
                PrgOffsets[2] = BankRegisters[6];
                PrgOffsets[3] = LastBank;
            } else {
                PrgOffsets[0] = BankRegisters[6];
                PrgOffsets[1] = BankRegisters[7];
                PrgOffsets[2] = LastBank - 1;
                PrgOffsets[3] = LastBank;
            }

            if (ChrMode) {
                ChrOffsets[0] = BankRegisters[2];
                ChrOffsets[1] = BankRegisters[3];
                ChrOffsets[2] = BankRegisters[4];
                ChrOffsets[3] = BankRegisters[5];

                ChrOffsets[4] = BankRegisters[0] & ~1;
                ChrOffsets[5] = BankRegisters[0] | 1;

                ChrOffsets[6] = BankRegisters[1] & ~1;
                ChrOffsets[7] = BankRegisters[1] | 1;
            } else {
                ChrOffsets[0] = BankRegisters[0] & ~1;
                ChrOffsets[1] = BankRegisters[0] | 1;

                ChrOffsets[2] = BankRegisters[1] & ~1;
                ChrOffsets[3] = BankRegisters[1] | 1;

                ChrOffsets[4] = BankRegisters[2];
                ChrOffsets[5] = BankRegisters[3];
                ChrOffsets[6] = BankRegisters[4];
                ChrOffsets[7] = BankRegisters[5];
            }

            for (var i = 0; i < ChrOffsets.Length; i++) {
                ChrOffsets[i] *= CHR_SIZE;
                ChrOffsets[i] %= ChrRom.Length;
            }
            for (var i = 0; i < PrgOffsets.Length; i++) {
                PrgOffsets[i] *= PRG_SIZE;
                PrgOffsets[i] %= PrgRom.Length;
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
