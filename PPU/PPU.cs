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
        public ushort CurrentScanline { get; set; }
        public ulong TotalCycles { get; set; }

        byte InternalDataBuffer;
        MaskRegister Mask;
        AddrRegister Addr;
        ControlRegister Ctrl;
        ScrollRegister Scroll;
        StatusRegister Status;

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

            TotalCycles = 7;
            CurrentScanline = 0;
            CurrentCycle = 21;
            NmiInterrupt = false;
        }

        void IncrementVramAddr() {
            Addr.Increment(Ctrl.GetVramAddrIncrement());
        }

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
                    // Guide has this part, but it seems really weird to do this, no?
                    else if (nametable is 3) {
                        nameTableAddr -= 0x800;
                    }
                    break;
                default:
                case ScreenMirroring.FourScreen:
                    break;
            }
            return nameTableAddr;
        }



        public bool IncrementCycle(ulong cycleCount) {
            CurrentCycle += cycleCount;
            TotalCycles += cycleCount / 3;

            if (CurrentCycle >= 341) {
                if (IsSpriteZeroHit(cycleCount)) {
                    Status.SetSpriteZeroHit(true);
                }

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
                    CurrentScanline = 0;
                    Status.SetSpriteZeroHit(false);
                    Status.ResetVBlank();
                    NmiInterrupt = false;
                    return true;
                }
            }
            return false;
        }

        private bool IsSpriteZeroHit(ulong cycle) {
            var y = OamData[0];
            var x = OamData[3];
            return (y == CurrentScanline) && x <= cycle && Mask.GetSprite();
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
            if (!beforeNmi && Ctrl.ShouldGenerateVBlank() && Status.IsVBlank()) {
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

            if (addr >= 0 && addr <= 0x1fff) {
                var result = InternalDataBuffer;
                InternalDataBuffer = ChrRom[addr];
                return result;
            }

            if (addr >= 0x2000 && addr <= 0x2fff) {
                var result = InternalDataBuffer;
                InternalDataBuffer = Vram[MirrorVramAddr(addr)];
                return result;
            }

            if (addr >= 0x3000 && addr <= 0x3eff) {
                throw new Exception("This should normally not be reached");
            }

            if (addr == 0x3f10 || addr == 0x3f14 || addr == 0x3f18 || addr == 0x3f1c) {
                var mirror = addr - 0x10;
                return PaletteTable[mirror];
            }

            if (addr >= 0x3f00 && addr <= 0x3fff) {
                return PaletteTable[addr - 0x3f00];
            }

            throw new Exception("Reached unknown address");
        }

        public void WriteData(byte value) {
            var addr = Addr.Get();
            if (addr >= 0 && addr <= 0x1fff) {
                ChrRom[addr] = value;
            }

            if (addr >= 0x2000 && addr <= 0x2fff) {
                var mirror = MirrorVramAddr(addr);
                Vram[mirror] = value;
            }

            if (addr >= 0x3000 && addr <= 0x3eff) {
                //Console.WriteLine("0x3000 > x 0x3eff being used");
                ////throw new NotImplementedException("Shouldn't be used");
            }

            if (addr >= 0x3f10 || addr == 0x3f14 || addr == 0x3f18 || addr == 0x3f1c) {
                PaletteTable[(addr - 0x10) & 0x1F] = value;
            }

            if (addr >= 0x3f00 && addr <= 0x3fff) {
                PaletteTable[addr & 0x1F] = value;
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

        public byte[] GetNametableTilePalette(byte tileX, byte tileY) {
            var attrTableIndex = tileY / 4 * 8 + tileX / 4;
            var attrValue = Vram[0x3c0 + attrTableIndex];

            var segmentX = tileX % 4 / 2;
            var segmentY = tileY % 4 / 2;

            var paletteIndex = 0;
            if (segmentX == 0 && segmentY == 0) {
                paletteIndex = attrValue & 0b11;
            } 
            else if (segmentX == 1 && segmentY == 0) {
                paletteIndex = (attrValue >> 2) & 0b11;
            } 
            else if (segmentX == 0 && segmentY == 1) {
                paletteIndex = (attrValue >> 4) & 0b11;
            } 
            else if (segmentX == 1 && segmentY == 1) {
                paletteIndex = (attrValue >> 6) & 0b11;
            }

            var paletteStart = paletteIndex * 4 + 1;
            return new byte[] {
                PaletteTable[0],
                PaletteTable[paletteStart],
                PaletteTable[paletteStart + 1],
                PaletteTable[paletteStart + 2],
            };
        } 
    }
}
