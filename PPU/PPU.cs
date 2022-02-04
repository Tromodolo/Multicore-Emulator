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

        uint[] FrameBuffer = new uint[256 * 240];
        bool[] BgIsOpaque = new bool[256 * 240];

        public PPU(byte[] chrRom, ScreenMirroring mirroring) {
            ChrRom = chrRom;
            Mirroring = mirroring;
            OamAddr = 0;
            OamData = new byte[256];
            PaletteTable = new byte[32];
            Vram = new byte[4096];

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
            CurrentCycle += cycleCount;
            TotalCycles += cycleCount / 3;

            if (CurrentCycle >= 341) {
                Status.SetSpriteZeroHit(false);
                //if (IsSpriteZeroHit(cycleCount)) {
                //    Status.SetSpriteZeroHit(true);
                //}

                CurrentCycle -= 341;
                CurrentScanline++;

                if (CurrentScanline == 241) {
                    Status.SetVBlank(true);
                    //Status.SetSpriteZeroHit(false);
                    if (Ctrl.ShouldGenerateVBlank()) {
                        NmiInterrupt = true;
                    }
                }

                if (CurrentScanline >= 262) {
                    CurrentScanline = 0;
                    //Status.SetSpriteZeroHit(false);
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

        public byte GetSpriteSize() {
            return Ctrl.GetSpriteSize();
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

        public (byte x, byte y) GetScroll() {
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
                return PaletteTable[mirror - 0x3f00];
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

            if (addr == 0x3f10 || addr == 0x3f14 || addr == 0x3f18 || addr == 0x3f1c) {
                PaletteTable[(addr - 0x10) - 0x3f00] = value;
            }

            if (addr >= 0x3f00 && addr <= 0x3fff) {
                PaletteTable[addr - 0x3f00] = value;
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
            if (x < 0 || x > 255 || y < 0 || y > 240) {
                return;
            }

            FrameBuffer[
                x +
                (y * 256)
            ] = (uint)((color.r << 16) | (color.g << 8 | (color.b << 0)));
        }

        public bool RenderScanline(int scanline) {
            if (scanline >= 240) {
                return false;
            }

            var scroll = GetScroll();
            var mirroring = Mirroring;
            var nametableAddr = GetNameTableAddress();

            byte[] primary = { };
            byte[] secondary = { };

            if (mirroring == Rom.ScreenMirroring.Vertical) {
                if (nametableAddr == 0x2000 || nametableAddr == 0x2400) {
                    primary = Vram[0..0x400];
                    secondary = Vram[0x400..0x800];
                } else if (nametableAddr == 0x2800 || nametableAddr == 0x2C00) {
                    primary = Vram[0x400..0x800];
                    secondary = Vram[0..0x400];
                }
            } else if (mirroring == Rom.ScreenMirroring.Horizontal) {
                if (nametableAddr == 0x2000 || nametableAddr == 0x2800) {
                    primary = Vram[0..0x400];
                    secondary = Vram[0x400..0x800];
                } else if (nametableAddr == 0x2400 || nametableAddr == 0x2C00) {
                    primary = Vram[0x400..0x800];
                    secondary = Vram[0..0x400];
                }
            } else {
                throw new NotImplementedException("Four Screen mirroring is not supported");
            }

            if (Mask.GetBackground()) {
                Viewport viewport;
                viewport.x0 = scroll.x;
                viewport.x1 = 255;
                viewport.y0 = scroll.y;
                viewport.y1 = 239;

                RenderNametable(primary, scanline, viewport, -scroll.x, -scroll.y);

                if (scroll.x > 0) {
                    viewport.x0 = 0;
                    viewport.x1 = scroll.x;
                    viewport.y0 = 0;
                    viewport.y1 = 239;

                    RenderNametable(secondary, scanline, viewport, 255 - scroll.x, 0);
                } else if (scroll.y > 0) {
                    viewport.x0 = 0;
                    viewport.x1 = 255;
                    viewport.y0 = 0;
                    viewport.y1 = scroll.y;

                    RenderNametable(secondary, scanline, viewport, 0, 239 - scroll.y);
                }
            }

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

                    if (!(scanline >= yPosition && scanline <= yPosition + 8)) {
                        continue;
                    }

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

                    if (!(yPosition < scanline || scanline < yPosition + 8)) {
                        continue;
                    }

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

                            if (pixelY != scanline) {
                                continue;
                            }

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

        public struct Viewport {
            public int x0;
            public int x1;
            public int y0;
            public int y1;
        }

        private void RenderNametable(byte[] nametable, int scanline, Viewport vp, int shiftX, int shiftY) {
            var bank = GetBackgroundPatternAddr();
            var leftColumnEnable = Mask.GetBackgroundLeftColumn();

            if (vp.x0 <= 7 && !leftColumnEnable) {
                vp.x0 = 8;
            }

            for (var tileIndex = 0; tileIndex < 0x3c0; tileIndex++) {
                var tileAddr = nametable[tileIndex];
                var tile_x = tileIndex % 32;
                var tile_y = tileIndex / 32;

                if (!(scanline >= tile_y * 8 && scanline <= (tile_y * 8) + 8)) {
                    continue;
                }

                var palette = GetNametableTilePalette(nametable, (byte)tile_x, (byte)tile_y);
                var tile = ChrRom[(bank + tileAddr * 16)..(bank + tileAddr * 16 + 16)];

                for (var y = 0; y <= 7; y++) {
                    var pixelY = tile_y * 8 + y;
                    var upper = tile[y];
                    var lower = tile[y + 8];

                    if (pixelY != scanline || vp.y0 > pixelY || vp.y1 < pixelY) {
                        continue;
                    }

                    for (var x = 7; x >= 0; x--) {
                        var pixelX = tile_x * 8 + x;

                        if (vp.x0 > pixelX || vp.x1 < pixelX) {
                            continue;
                        }

                        var value = (1 & lower) << 1 | (1 & upper);
                        upper = (byte)(upper >> 1);
                        lower = (byte)(lower >> 1);
                        (byte r, byte g, byte b) color;
                        switch (value) {
                            case 0:
                                color = Palette.SystemPalette[palette[0]];
                                BgIsOpaque[pixelX + shiftX + ((pixelY + shiftY) * 256)] = true;
                                break;
                            case 1:
                                color = Palette.SystemPalette[palette[1]];
                                BgIsOpaque[pixelX + shiftX + ((pixelY + shiftY) * 256)] = false;
                                break;
                            case 2:
                                color = Palette.SystemPalette[palette[2]];
                                BgIsOpaque[pixelX + shiftX + ((pixelY + shiftY) * 256)] = false;
                                break;
                            case 3:
                                color = Palette.SystemPalette[palette[3]];
                                BgIsOpaque[pixelX + shiftX + ((pixelY + shiftY) * 256)] = false;
                                break;
                            default: throw new Exception("Something fucky");
                        };

                        SetPixel(pixelX + shiftX, pixelY + shiftY, color);
                    }
                }
            }
        }
    }
}
