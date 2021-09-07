using NesEmu.Rom;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.PPU {
    public class PPU {
        public byte[] ChrRom { get; private set; }
        public byte[] PaletteTable { get; private set; }
        public byte[] Vram { get; private set; }
        public byte[] OamData { get; private set; }
        public byte OamAddr { get; private set; }
        public ScreenMirroring Mirroring { get; private set; }
        public ulong CurrentCycle { get; private set; }

        byte InternalDataBuffer;
        MaskRegister Mask;
        AddrRegister Addr;
        ControlRegister Ctrl;
        ScrollRegister Scroll;
        StatusRegister Status;

        ushort CurrentScanline;
        bool NmiInterrupt;

        public PPU(byte[] chrRom, ScreenMirroring mirroring) {
            ChrRom = chrRom;
            Mirroring = mirroring;
            OamAddr = 0;
            OamData = new byte[256];
            PaletteTable = new byte[32];
            Vram = new byte[2048];

            InternalDataBuffer = 0;
            Mask = new MaskRegister();
            Addr = new AddrRegister();
            Ctrl = new ControlRegister();
            Scroll = new ScrollRegister();
            Status = new StatusRegister();

            CurrentScanline = 0;
            NmiInterrupt = false;
        }

        void IncrementVramAddr() {
            Addr.Increment(Ctrl.GetVramAddrIncrement());
        }

        //        pub fn is_interrupt(&mut self) -> bool {
        //        self.nmi_interrupt
        //    }

        //    pub fn get_interrupt(&mut self) -> bool {
        //        let interrupt = self.nmi_interrupt;
        //    self.nmi_interrupt = false;
        //        interrupt
        //}


        // Horizontal:
        //   [ A1 ] [ a2 ]
        //   [ B1 ] [ b2 ]

        // Vertical:
        //   [ A1 ] [ B1 ]
        //   [ a2 ] [ b2 ]
        ushort MirrorVramAddr(ushort addr) {
            // Mirrors values like 0x3000-0x3eff down to 0x2000-0x2eff
            var mirroredAddr = addr & 0b10111111111111;
            // Get absolute value within vram
            ushort vector = (ushort)(mirroredAddr - 0x2000);
            // Get index of nametable
            ushort nametable = (ushort)(vector / 0x400);

            ushort nameTableAddr = vector;
            switch (Mirroring) {
                case ScreenMirroring.Vertical:
                    if (nametable is 2 or 3) {
                        nameTableAddr -= 0x800;
                    }
                    break;
                case ScreenMirroring.Horizontal:
                    if (nametable is 1 or 2) {
                        nameTableAddr -= 0x400;
                    } 
                    //else if (nametable is 3) {
                    //    nameTableAddr -= 0x800;
                    //}
                    break;
                default:
                case ScreenMirroring.FourScreen:
                    break;
            }
            return nameTableAddr;
        }



        public bool IncrementCycle(ulong cycleCount) {
            CurrentCycle += cycleCount;

            if (CurrentCycle >= 341) {
                CurrentCycle -= 341;
                CurrentScanline++;

                if (CurrentScanline == 241) {
                    Status.SetVBlank(true);
                    Status.SetSpriteZeroHit(false);
                    if (Ctrl.ShouldGenerateVBlank()) {
                        NmiInterrupt = true;
                    }
                }

                if (CurrentScanline >= 262) {
                    //CurrentFrame++
                    CurrentScanline = 0;
                    Status.SetSpriteZeroHit(false);
                    Status.ResetVBlank();
                    NmiInterrupt = false;
                    return true;
                }
            }
            return false;
        }

        public bool IsInterrupt() {
            return NmiInterrupt;
        }

        /// <summary>
        /// Warning: Sets NmiInterrupt to false after getting value
        /// If you just want to check, use IsInterrupt()
        /// </summary>
        /// <returns></returns>
        public bool GetInterrupt() {
            var interrupt = NmiInterrupt;
            NmiInterrupt = false;
            return interrupt;
        }

        public ushort GetBackgroundPatternAddr() {
            return Ctrl.GetBackgroundPatternAddr();
        }

        public ushort GetSpritePatternAddr() {
            return Ctrl.GetSpritePatternAddr();
        }

        public void WriteCtrl(byte value) {
            var beforeNmi = Ctrl.ShouldGenerateVBlank();
            Ctrl.Update(value);
            if (beforeNmi && Ctrl.ShouldGenerateVBlank() && Status.IsVBlank()) {
                NmiInterrupt = true;
            }
        }


        public void WriteMask(byte value) {
            Mask.Update(value);
        }

        public (byte, byte) GetScroll() {
            return (Scroll.GetX(), Scroll.GetY());
        }

        public void WriteScroll(byte value) {
            Scroll.Update(value);
        }

        public void WritePPUAddr(byte value) {
            Addr.Update(value);
        }

        public byte GetData() {
            var addr = Addr.Get();
            IncrementVramAddr();

            if (addr is >= 0x000 and <= 0x1fff) {
                var buffer = InternalDataBuffer;
                InternalDataBuffer = ChrRom[addr];
                return buffer;
            } else if (addr is >= 0x2000 and <= 0x2fff) {
                var buffer = InternalDataBuffer;
                InternalDataBuffer = Vram[MirrorVramAddr(addr)];
                return buffer;
            } else if (addr is 0x3f10 or 0x3f14 or 0x3f18 or 0x3f1c) { // These are mirrors of 0x3f00, 0x3f04, 0x3f08, ox3f0c
                var addrMirror = addr - 0x10;
                return PaletteTable[(ushort)(addrMirror - 0x3f00)];
            } else if (addr is >= 0x3000 and <= 0x3eff) {
                throw new Exception("Address Space 0x3000 => 0x3eff is not supposed to be used");
            } else if (addr is >= 0x3f00 and <=0x3fff) {
                return PaletteTable[(ushort)(addr - 0x3f00)];
            } else {
                throw new Exception(string.Format("Unexpected access to mirrored space {0}", addr));
            }
        }
        public void WriteData(byte value) {
            var addr = Addr.Get();
            if (addr is >= 0x000 and <= 0x1fff) {
                throw new Exception("Trying to write to CHR ROM");
            } else if (addr is >= 0x2000 and <= 0x2fff) {
                Vram[MirrorVramAddr(addr)] = value;
            } else if (addr is 0x3f10 or 0x3f14 or 0x3f18 or 0x3f1c) { // These are mirrors of 0x3f00, 0x3f04, 0x3f08, ox3f0c
                var addrMirror = addr - 0x10;
                PaletteTable[addrMirror - 0x3f10] = value;
            } else if (addr is >= 0x3000 and <= 0x3eff) {
                throw new Exception("Address Space 0x3000 => 0x3eff is not supposed to be used");
            } else if (addr is >= 0x3f00 and <= 0x3fff) {
                PaletteTable[addr - 0x3f00] = value;
            } else {
                throw new Exception(string.Format("Unexpected access to mirrored space {0}", addr));
            }
            IncrementVramAddr();
        }

        public byte GetStatus() {
            var status = Status.GetSnapshot();
            Status.ResetVBlank();
            Addr.ResetLatch();
            Scroll.ResetLatch();
            return status;
        }

        public byte GetOAMData() {
            return OamData[OamAddr];
        }
        public void WriteOAMData(byte value) {
            OamData[OamAddr] = value;
            OamAddr++;
        }

        public byte GetOAMAddr() {
            return OamAddr;
        }
        public void WriteOAMAddr(byte value) {
            OamAddr = value;
        }
    }
}
