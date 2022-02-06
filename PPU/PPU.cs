using NesEmu.Rom;
using SDL2;
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
        public int DotsDrawn { get; private set; }
        public int CurrentScanline { get; set; }
        public ulong TotalCycles { get; set; }


        byte InternalDataBuffer;
        MaskRegister Mask;
        ControlRegister Ctrl;
        StatusRegister Status;

        bool NmiInterrupt;

        uint[] FrameBuffer = new uint[256 * 240];
        bool[] BgIsOpaque = new bool[256 * 240];

        // Handling these is an absolute nightmare
        // https://wiki.nesdev.org/w/index.php/PPU_scrolling
        // https://www.youtube.com/watch?v=-THeUXqR3zY
        // https://github.com/OneLoneCoder/olcNES/blob/master/Part%20%234%20-%20PPU%20Backgrounds/olc2C02.cpp
        Loopy T_Loopy;
        Loopy V_Loopy;
        byte fineX = 0;

        //// Used when setting/updating T and V
        bool WriteLatch = false;

        byte BgTileNo = 0;

        byte BgNextTileId,
             BgNextTileAttribute,
             BgNextTileLsb,
             BgNextTileMsb;

        ushort BgShifterPatternLo,
               BgShifterPatternHi,
               BgShifterAttributeLo,
               BgShifterAttributeHi;

        public PPU(byte[] chrRom, ScreenMirroring mirroring) {
            ChrRom = chrRom;
            Mirroring = mirroring;
            OamAddr = 0;
            OamData = new byte[256];
            PaletteTable = new byte[32];
            Vram = new byte[4096];

            InternalDataBuffer = 0;
            Mask = new MaskRegister();
            Ctrl = new ControlRegister();
            Status = new StatusRegister();

            TotalCycles = 0;
            CurrentScanline = 0;
            DotsDrawn = 0;
            NmiInterrupt = false;

            T_Loopy = new Loopy();
            V_Loopy = new Loopy();
        }


        void IncrementVramAddr() {
            V_Loopy.Increment(Ctrl.GetVramAddrIncrement());
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
                    if (nametable is 1 or 2) {
                        nameTableAddr -= 0x400;
                    }
                    break;
                case ScreenMirroring.Horizontal:
                    if (nametable is 2 or 3) {
                        nameTableAddr -= 0x800;
                    }
                    // Guide has this part, but it seems really weird to do this, no?
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
            TotalCycles += cycleCount / 3;

            for (var i = cycleCount; i > 0; i--) {
                if (CurrentScanline >= -1 && CurrentScanline < 240) {
                    if (CurrentScanline == -1 && DotsDrawn == 1) {
                        Status.SetVBlank(false);
                    }

                    if ((DotsDrawn >= 2 && DotsDrawn < 258) || (DotsDrawn >= 321 && DotsDrawn < 338)) {
                        UpdateShifters();

                        switch ((DotsDrawn - 1) % 8) {
                            case 0:
                                LoadBackgroundShifters();
                                BgTileNo = (byte)(V_Loopy.GetAddress() & 0x0FFF);
                                BgNextTileId = Vram[MirrorVramAddr((ushort)(0x2000 | (V_Loopy.GetAddress() & 0x0FFF)))];
                                break;
                            case 2:
                                BgNextTileAttribute = Vram[
                                    MirrorVramAddr((ushort)(0x23c0 |
                                    ((V_Loopy.Nametable & 0b10) << 11) |
                                    ((V_Loopy.Nametable & 1) << 10) |
                                    ((V_Loopy.CoarseY >> 2) << 3) |
                                    (V_Loopy.CoarseX >> 2)))
                                ];

                                if ((V_Loopy.CoarseY & 0x02) != 0) {
                                    BgNextTileAttribute >>= 4;
                                }
                                if ((V_Loopy.CoarseX & 0x02) != 0) {
                                    BgNextTileAttribute >>= 2;
                                }
                                BgNextTileAttribute &= 0x03;
                                break;
                            case 4:
                                BgNextTileLsb = ChrRom[Ctrl.GetBackgroundPatternAddr() + (BgNextTileId * 16) + V_Loopy.FineY];
                                break;
                            case 6:
                                BgNextTileMsb = ChrRom[Ctrl.GetBackgroundPatternAddr() + (BgNextTileId * 16) + V_Loopy.FineY + 8];
                                break;
                            case 7:
                                IncrementScrollX();
                                break;
                        }
                    }

                    if (DotsDrawn == 256) {
                        IncrementScrollY();
                    }

                    if (DotsDrawn == 257) {
                        LoadBackgroundShifters();
                        ResetAddressX();
                    }

                    if (DotsDrawn == 338 || DotsDrawn == 340) {
                        BgNextTileId = Vram[MirrorVramAddr((ushort)(0x2000 | (V_Loopy.GetAddress() & 0x0FFF)))];
                    }

                    if (CurrentScanline == -1 && DotsDrawn >= 280 && DotsDrawn < 305) {
                        ResetAddressY();
                    }
                }

                if (CurrentScanline == 240) {
                    // Nothing?
                }

                if (CurrentScanline == 241 && DotsDrawn == 1) {
                    Status.SetVBlank(true);
                    if (Ctrl.ShouldGenerateVBlank()) {
                        NmiInterrupt = true;
                    }
                }

                //&& DotsDrawn >= 0 && DotsDrawn <= 256 && CurrentScanline >= 0 && CurrentScanline < 240
                //  || (CurrentScanline == -1 && DotsDrawn >= 321)
                if (Mask.GetBackground() && (DotsDrawn > 0 && DotsDrawn <= 256 && CurrentScanline >= 0 && CurrentScanline < 240)) {
                    var pixelX = DotsDrawn - 1;
                    var pixelY = CurrentScanline - 1;

                    if (CurrentScanline == -1) {
                        pixelX -= 321;
                        pixelY = 0;
                    }

                    ushort bitMux = (ushort)(0x8000 >> fineX);

                    byte p0Pixel = (byte)((BgShifterPatternLo & bitMux) > 0 ? 1 : 0);
                    byte p1Pixel = (byte)((BgShifterPatternHi & bitMux) > 0 ? 1 : 0);
                    byte bgPixel = (byte)((p1Pixel << 1) | p0Pixel);

                    byte p0Palette = (byte)((BgShifterAttributeLo & bitMux) > 0 ? 1 : 0);
                    byte p1Palette = (byte)((BgShifterAttributeHi & bitMux) > 0 ? 1 : 0);
                    byte bgPalette = (byte)((p1Palette << 1) | p0Palette);

                    var color = GetPaletteFromMemory(bgPalette, bgPixel);
                    if (((bgPalette << 2) + bgPixel) == 0) {
                        var y = 0;
                    }
                    SetPixel(pixelX, pixelY, color);

                }

                DotsDrawn++;
                if (DotsDrawn >= 341) {
                    DotsDrawn = 0;
                    CurrentScanline++;
                    if (CurrentScanline >= 261) {
                        CurrentScanline = -1;
                        Status.ResetVBlank();
                        NmiInterrupt = false;
                        return true;
                    }
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

        public byte GetSpriteSize() {
            return Ctrl.GetSpriteSize();
        }

        public void WriteCtrl(byte value) {
            T_Loopy.Nametable = (byte)(value & 0b11);

            var beforeNmi = Ctrl.ShouldGenerateVBlank();
            Ctrl.Update(value);
            if (!beforeNmi && Ctrl.ShouldGenerateVBlank() && Status.IsVBlank()) {
                NmiInterrupt = true;
            }
        }


        public void WriteMask(byte value) {
            Mask.Update(value);
        }

        private void IncrementScrollX() {
            if (Mask.GetSprite() || Mask.GetBackground()) {
                if (V_Loopy.CoarseX == 31) {
                    V_Loopy.CoarseX = 0;

                    // Flip the X bit
                    V_Loopy.Nametable ^= 1;
                } else {
                    V_Loopy.CoarseX++;
                }
            }
        }

        private void IncrementScrollY() {
            if (Mask.GetSprite() || Mask.GetBackground()) {
                if (V_Loopy.FineY < 7) {
                    V_Loopy.FineY++;
                } else {
                    V_Loopy.FineY = 0;

                    if (V_Loopy.CoarseY == 29) {
                        V_Loopy.CoarseY = 0;
                        // Flip the Y bit
                        V_Loopy.Nametable ^= 0b10;
                    } else if (V_Loopy.CoarseY == 31) {
                        V_Loopy.CoarseY = 0;
                    } else {
                        V_Loopy.CoarseY++;
                    }
                }
            }
        }

        private void ResetAddressX() {
            if (Mask.GetSprite() || Mask.GetBackground()) {
                V_Loopy.Nametable = (byte)((V_Loopy.Nametable & 0b10) | (T_Loopy.Nametable & 0b1));
                V_Loopy.CoarseX = T_Loopy.CoarseX;
            }
        }

        private void ResetAddressY() {
            if (Mask.GetSprite() || Mask.GetBackground()) {
                V_Loopy.Nametable = (byte)((V_Loopy.Nametable & 0b1) | (T_Loopy.Nametable & 0b10));
                V_Loopy.FineY = T_Loopy.FineY;
                V_Loopy.CoarseY = T_Loopy.CoarseY;
            }
        }

        private void LoadBackgroundShifters() {
            BgShifterPatternLo = (ushort)((BgShifterPatternLo & 0xFF00) | BgNextTileLsb);
            BgShifterPatternHi = (ushort)((BgShifterPatternHi & 0xFF00) | BgNextTileMsb);

            var attributeLo = BgNextTileAttribute & 0b01;
            var attributeHi = BgNextTileAttribute & 0b10;

            if (attributeLo != 0) {
                BgShifterAttributeLo = (ushort)((BgShifterAttributeLo & 0xFF00) | 0xFF);
            } else {
                BgShifterAttributeLo = (ushort)((BgShifterAttributeLo & 0xFF00));
            }
            if (attributeHi != 0) {
                BgShifterAttributeHi = (ushort)((BgShifterAttributeHi & 0xFF00) | 0xFF);
            } else {
                BgShifterAttributeHi = (ushort)((BgShifterAttributeHi & 0xFF00));
            }
        }

        private void UpdateShifters() {
            if (Mask.GetBackground()) {
                BgShifterAttributeLo <<= 1;
                BgShifterAttributeHi <<= 1;
                BgShifterPatternLo <<= 1;
                BgShifterPatternHi <<= 1;
            }
        }

        private (byte r, byte g, byte b) GetPaletteFromMemory(byte palette, byte pixel) {
            return Palette.SystemPalette[PaletteTable[(palette << 2) + pixel] & 0x3f];
        }

        public void WriteScroll(byte value) {
            if (WriteLatch) {
                var coarseY = value >> 3;
                var fineY = value & 0b111;

                T_Loopy.CoarseY = (byte)coarseY;
                T_Loopy.FineY = (byte)fineY;

                WriteLatch = false;
            } else {
                fineX = (byte)(value & 0b111);
                var coarseX = value >> 3;
                T_Loopy.CoarseX = (byte)coarseX;

                WriteLatch = true;
            }
        }

        public void WritePPUAddr(byte value) {
            if (WriteLatch) {
                T_Loopy.Update((ushort)((T_Loopy.GetAddress() & 0xFF00) | value));
                V_Loopy.Update(T_Loopy.Address);
                WriteLatch = false;
            } else {
                T_Loopy.Update((ushort)(((value & 0x3f) << 8) | (T_Loopy.GetAddress() & 0x00ff)));
                WriteLatch = true;
            }
        }

        public byte GetData() {
            var addr = V_Loopy.GetAddress();
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
                return PaletteTable[mirror - 0x3f00];
            }

            if (addr >= 0x3f00 && addr <= 0x3fff) {
                return PaletteTable[addr - 0x3f00];
            }

            throw new Exception("Reached unknown address");
        }

        public void WriteData(byte value) {
            var addr = V_Loopy.GetAddress();
            IncrementVramAddr();

            if (addr >= 0 && addr <= 0x1fff) {
                ChrRom[addr] = value;
                return;
            }

            if (addr >= 0x2000 && addr <= 0x2fff) {
                var mirror = MirrorVramAddr(addr);
                Vram[mirror] = value;
                return;
            }

            if (addr >= 0x3000 && addr <= 0x3eff) {
                //Console.WriteLine("0x3000 > x 0x3eff being used");
                //throw new NotImplementedException("Shouldn't be used");
                return;
            }


            if (addr == 0x3f10 || addr == 0x3f14 || addr == 0x3f18 || addr == 0x3f1c) {
                PaletteTable[(addr - 0x10) - 0x3f00] = value;
                return;
            }

            if (addr >= 0x3f00 && addr <= 0x3fff) {
                PaletteTable[addr - 0x3f00] = value;
                return;
            }
        }

        public byte GetStatus() {
            var status = Status.GetSnapshot();
            Status.ResetVBlank();
            WriteLatch = false;
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

        public void WriteDMA(byte[] data) {
            foreach (var b in data) {
                OamData[OamAddr] = b;
                OamAddr++;
            }
        }

        public ushort GetNameTableAddress() {
            return Ctrl.GetNameTableAddress();
        }

        public byte[] GetNametableTilePalette(byte[] nametable, byte tileX, byte tileY) {
            var attrTableIndex = tileY / 4 * 8 + tileX / 4;
            var attrValue = nametable[0x3c0 + attrTableIndex];

            var segmentX = tileX % 4 / 2;
            var segmentY = tileY % 4 / 2;

            byte paletteIndex = 0;
            if (segmentX == 0) {
                if (segmentY == 0) {
                    paletteIndex = (byte)(attrValue & 0b11);
                } else if(segmentY == 1) {
                    paletteIndex = (byte)((attrValue >> 4) & 0b11);
                }
            } else {
                if (segmentY == 0) {
                    paletteIndex = (byte)((attrValue >> 2) & 0b11);
                } else if (segmentY == 1){
                    paletteIndex = (byte)((attrValue >> 6) & 0b11);
                }
            }

            var paletteStart =  1 + paletteIndex * 4;
            return new byte[] {
                PaletteTable[0],
                PaletteTable[paletteStart],
                PaletteTable[paletteStart + 1],
                PaletteTable[paletteStart + 2],
            };
        }

        public byte[] GetSpritePalette(byte spriteIndex) {
            var paletteStart = 17 + (spriteIndex * 4);
            return new byte[] {
                0,
                PaletteTable[paletteStart],
                PaletteTable[paletteStart + 1],
                PaletteTable[paletteStart + 2],
            };
        }

        public void DrawFrame(ref IntPtr renderer, ref IntPtr Texture) {
            unsafe {
                SDL.SDL_Rect rect;
                rect.w = 256 * 3;
                rect.h = 240 * 3;
                rect.x = 0;
                rect.y = 0;

                fixed (uint* pArray = FrameBuffer) {
                    IntPtr intPtr = new IntPtr((void*)pArray);

                    SDL.SDL_UpdateTexture(Texture, ref rect, intPtr, 256 * 4);
                }

                SDL.SDL_RenderCopy(renderer, Texture, IntPtr.Zero, ref rect);
                SDL.SDL_RenderPresent(renderer);
            }
        }

        private void SetPixel(int x, int y, (byte r, byte g, byte b) color) {
            if (x < 0 || x > 255 || y < 0 || y >= 240) {
                return;
            }

            FrameBuffer[
                x +
                (y * 256)
            ] = (uint)((color.r << 16) | (color.g << 8 | (color.b << 0)));
        }

        public bool RenderScanline() {
            //if (scanline >= 240) {
            //    return false;
            //}

            if (Mask.GetSprite()) {
                var isSpriteZero = true;
                var spriteSize = GetSpriteSize();
                for (var i = 0; i <= 63; i++) {
                    var yPosition = OamData[i * 4];
                    var tileIndex = OamData[(i * 4) + 1];
                    var attributes = OamData[(i * 4) + 2];
                    var xPosition = OamData[(i * 4) + 3];

                    if (!Mask.GetSpriteLeftColumn() && xPosition <= 7) {
                        xPosition = 8;
                    }

                    //if (!(scanline >= yPosition && scanline <= yPosition + 8)) {
                    //    continue;
                    //}

                    if (yPosition == 0 && xPosition == 0 && tileIndex == 0 && attributes == 0) {
                        continue;
                    }

                    var paletteVal = attributes & 0b11;
                    var priority = (attributes >> 5 & 1) == 0;
                    var flipHorizontal = (attributes >> 6 & 1) == 1;
                    var flipVertical = (attributes >> 7 & 1) == 1;

                    // If position is below the screen (?), at least it shouldn't crash anymore
                    // I think this happens when they copy a sprite to offscreen
                    if (yPosition >= 240 - 8) {
                        continue;
                    }

                    //if (!(yPosition < scanline || scanline < yPosition + 8)) {
                    //    continue;
                    //}

                    var palette = GetSpritePalette((byte)paletteVal);
                    if (spriteSize == 8) {
                        var spriteBank = GetSpritePatternAddr();
                        var sprite = ChrRom[(spriteBank + tileIndex * 16)..(spriteBank + tileIndex * 16 + 16)];

                        for (var y = 0; y <= 7; y++) {
                            byte pixelY = (byte)(yPosition + y);
                            if (flipVertical) {
                                pixelY = (byte)(yPosition + 7 - y);
                            }

                            var upper = sprite[y];
                            var lower = sprite[y + 8];

                            //if (pixelY != scanline) {
                            //    continue;
                            //}

                            for (var x = 7; x >= 0; x--) {
                                byte pixelX = (byte)(xPosition + x);
                                if (flipHorizontal) {
                                    pixelX = (byte)(xPosition + 7 - x);
                                }

                                var value = (1 & lower) << 1 | (1 & upper);
                                upper = (byte)(upper >> 1);
                                lower = (byte)(lower >> 1);
                                (byte r, byte g, byte b) color;
                                switch (value) {
                                    case 0: // Should be transparent
                                        if (isSpriteZero) {
                                            if (BgIsOpaque[pixelX + (pixelY * 256)]) {
                                                Status.SetSpriteZeroHit(true);
                                            }
                                        }

                                        continue;
                                    case 1:
                                        color = Palette.SystemPalette[palette[1]];
                                        break;
                                    case 2:
                                        color = Palette.SystemPalette[palette[2]];
                                        break;
                                    case 3:
                                        color = Palette.SystemPalette[palette[3]];
                                        break;
                                    default: throw new Exception("Something fucky");
                                };
                                if (!priority) {
                                    if (!BgIsOpaque[pixelX + (pixelY * 256)]) {
                                        break;
                                    }
                                }
                                switch (flipHorizontal, flipVertical) {
                                    case (false, false):
                                        SetPixel(xPosition + x, yPosition + y, color);
                                        break;
                                    case (true, false):
                                        SetPixel(xPosition + 7 - x, yPosition + y, color);
                                        break;
                                    case (false, true):
                                        SetPixel(xPosition + x, yPosition + 7 - y, color);
                                        break;
                                    case (true, true):
                                        SetPixel(xPosition + 7 - x, yPosition + 7 - y, color);
                                        break;
                                }
                            }
                        }
                    } else { // 8x16 sprites
                        var bankAddr = (tileIndex & 1) == 1;
                        var spriteAddr = bankAddr ? 0x1000 : 0;
                        var tileNumber = tileIndex >> 1;
                        // TODO: Finish the rest of the owl
                        throw new NotImplementedException("8x16 Sprites aren't supported yet");
                    }

                    isSpriteZero = false;
                }
            }

            return true;
        }
    }
}
