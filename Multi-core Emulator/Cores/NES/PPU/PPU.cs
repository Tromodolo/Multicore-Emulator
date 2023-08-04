using NesEmu.Rom;
using SDL2;

namespace NesEmu.PPU {
    struct SpriteEntry {
        public SpriteEntry() { }
        public byte YPosition = 0;
        public byte XPosition = 0;
        public byte TileId = 0;
        public byte Attribute = 0;
        public bool SpriteZero = false;
    }
    
    public class PPU {
        public byte[] CHR;
        public byte[] PALETTE;
        public byte[] VRAM;
        public byte[] OAM;
        
        Bus.Bus bus;
        bool mapperDidMap;
        readonly uint[] frameBuffer = new uint[256 * 240];
        
        byte readBuffer;
        bool isInNmiInterrupt;
        public byte OAMAddress;

        int dotsDrawn;
        int currentScanline;
        ulong totalCycles;

        // Handling these is an absolute nightmare
        // https://wiki.nesdev.org/w/index.php/PPU_scrolling
        // https://www.youtube.com/watch?v=-THeUXqR3zY
        // https://github.com/OneLoneCoder/olcNES/blob/master/Part%20%234%20-%20PPU%20Backgrounds/olc2C02.cpp
        Loopy loopyTemp; // Used for modifications
        Loopy loopyValue; // Used for rendering
        byte scrollFineX;
        //// Used when setting/updating T and V
        bool scrollWriteLatch;

        /// Sprites for current scanline
        SpriteEntry[] spritesEvaluated = new SpriteEntry[8];
        byte spriteCount;
        bool spriteZeroPossible;
        bool spriteZeroRendered;

        byte[] spriteShifterPatternLo = new byte[8];
        byte[] spriteShifterPatternHi = new byte[8];

        byte bgNextTileId,
             bgNextTileAttribute,
             bgNextTileLsb,
             bgNextTileMsb;

        ushort bgShifterPatternLo,
               bgShifterPatternHi,
               bgShifterAttributeLo,
               bgShifterAttributeHi;
        
        byte maskRegister;
        bool backgroundLeftColumn;
        bool renderBackground;
        bool spriteLeftcolumn;
        bool renderSprites;
        
        byte controlRegister;
        byte vramAddressIncrement;
        ushort backgroundPatternAddress;
        ushort spritePatternAddress;
        byte spriteSize;
        bool generateVBlank;
        
        bool isVBlank;
        bool isSpriteOverflow;
        bool isSpriteZeroHit;
        
        public PPU(byte[] chr) {
            CHR = chr;
            OAM = new byte[256];
            PALETTE = new byte[128];
            VRAM = new byte[0x800];
            
            OAMAddress = 0;
            readBuffer = 0;
            totalCycles = 0;
            currentScanline = 0;
            dotsDrawn = 0;
            isInNmiInterrupt = false;
            // statusRegister = StatusRegisterFlags.Empty;

            loopyTemp = new Loopy();
            loopyValue = new Loopy();
        }

        public void RegisterBus(Bus.Bus bus) {
            this.bus = bus;
        }

        public void Save(BinaryWriter writer) {
            writer.Write(CHR);
            writer.Write(PALETTE);
            writer.Write(VRAM);
            writer.Write(OAM);
            writer.Write(OAMAddress);
            writer.Write(dotsDrawn);
            writer.Write(currentScanline);
            writer.Write(totalCycles);
            writer.Write(readBuffer);
            writer.Write(maskRegister);
            writer.Write(controlRegister);
            // writer.Write((byte)statusRegister);
            writer.Write(isInNmiInterrupt);
            writer.Write(loopyTemp.GetAddress());
            writer.Write(loopyValue.GetAddress());
            writer.Write(scrollFineX);
            writer.Write(scrollWriteLatch);
            writer.Write(bgNextTileId);
            writer.Write(bgNextTileAttribute);
            writer.Write(bgNextTileLsb);
            writer.Write(bgNextTileMsb);
            writer.Write(bgShifterPatternLo);
            writer.Write(bgShifterPatternHi);
            writer.Write(bgShifterAttributeLo);
            writer.Write(bgShifterAttributeHi);
            writer.Write(spriteShifterPatternLo);
            writer.Write(spriteShifterPatternHi);
        }

        public void Load(BinaryReader reader) {
            CHR = reader.ReadBytes(CHR.Length);
            PALETTE = reader.ReadBytes(PALETTE.Length);
            VRAM = reader.ReadBytes(VRAM.Length);
            OAM = reader.ReadBytes(OAM.Length);
            OAMAddress = reader.ReadByte();
            dotsDrawn = reader.ReadInt32();
            currentScanline = reader.ReadInt32();
            totalCycles = reader.ReadUInt64();
            readBuffer = reader.ReadByte();
            maskRegister = reader.ReadByte();
            controlRegister = reader.ReadByte();
            // statusRegister = (StatusRegisterFlags)reader.ReadByte();
            isInNmiInterrupt = reader.ReadBoolean();
            loopyTemp.Update(reader.ReadUInt16());
            loopyValue.Update(reader.ReadUInt16());
            scrollFineX = reader.ReadByte();
            scrollWriteLatch = reader.ReadBoolean();
            bgNextTileId = reader.ReadByte();
            bgNextTileAttribute = reader.ReadByte();
            bgNextTileLsb = reader.ReadByte();
            bgNextTileMsb = reader.ReadByte();
            bgShifterPatternLo = reader.ReadUInt16();
            bgShifterPatternHi = reader.ReadUInt16();
            bgShifterAttributeLo = reader.ReadUInt16();
            bgShifterAttributeHi = reader.ReadUInt16();
            spriteShifterPatternLo = reader.ReadBytes(8);
            spriteShifterPatternHi = reader.ReadBytes(8);
        }

        void IncrementVramAddr() {
            loopyValue.Increment(vramAddressIncrement);
        }

        // Horizontal:
        //   [ A1 ] [ a2 ]
        //   [ B1 ] [ b2 ]

        // Vertical:
        //   [ A1 ] [ B1 ]
        //   [ a2 ] [ b2 ]
        ushort MirrorVramAddr(ushort addr) {
            // Mirrors values like 0x3000-0x3eff down to 0x2000-0x2eff
            int mirroredAddr = addr & 0b10111111111111;
            // Get absolute value within vram
            var vector = (ushort)(mirroredAddr - 0x2000);
            
            switch (bus.currentMapper.GetMirroring()) {
                case ScreenMirroring.Vertical:
                    if (vector <= 0x03FF)
                        return (ushort)(vector & 0x03FF); 

                    if (vector <= 0x07FF)
                        return (ushort)((vector & 0x03FF) + 0x400);

                    if (vector <= 0x0BFF)
                        return (ushort)(vector & 0x03FF);

                    if (vector <= 0x0FFF)
                        return (ushort)((vector & 0x03FF) + 0x400);
                    break;
                case ScreenMirroring.Horizontal:
                    if (vector <= 0x03FF)
                        return (ushort)(vector & 0x03FF);

                    if (vector <= 0x07FF)
                        return (ushort)(vector & 0x03FF);

                    if (vector <= 0x0BFF)
                        return (ushort)((vector & 0x03FF) + 0x400);

                    if (vector <= 0x0FFF)
                        return (ushort)((vector & 0x03FF) + 0x400);
                    break;
                case ScreenMirroring.OneScreenLower:
                    if (vector <= 0x03FF)
                        return (ushort)(vector & 0x03FF);

                    if (vector <= 0x07FF)
                        return (ushort)(vector & 0x03FF);

                    if (vector <= 0x0BFF)
                        return (ushort)(vector & 0x03FF);

                    if (vector <= 0x0FFF)
                        return (ushort)(vector & 0x03FF);
                    break;
                case ScreenMirroring.OneScreenUpper:
                    if (vector <= 0x03FF)
                        return (ushort)((vector & 0x03FF) + 0x400);

                    if (vector <= 0x07FF)
                        return (ushort)((vector & 0x03FF) + 0x400);

                    if (vector <= 0x0BFF)
                        return (ushort)((vector & 0x03FF) + 0x400);

                    if (vector <= 0x0FFF)
                        return (ushort)((vector & 0x03FF) + 0x400);
                    break;
                case ScreenMirroring.FourScreen:
                default:
                    break;
            }
            return vector;
        }

        byte GetChrRom(int addr) {
            byte mapperValue = bus.currentMapper.PPURead((ushort)addr, out mapperDidMap);
            if (mapperDidMap) {
                return mapperValue;
            }
            return CHR[addr];
        }

        public bool Clock() {
            totalCycles++;

            if (currentScanline >= -1 && currentScanline < 240) {
                if (currentScanline == 0 && dotsDrawn == 0) {
                    dotsDrawn = 1;
                }

                if (currentScanline == -1 && dotsDrawn == 1) {
                    isVBlank = false;
                    isSpriteZeroHit = false;
                    isSpriteOverflow = false;
                    //SpriteZeroPossible = false;
                    for (var j = 0; j < 8; j++) {
                        spriteShifterPatternLo[j] = 0;
                        spriteShifterPatternHi[j] = 0;
                    }
                }

                if ((dotsDrawn >= 2 && dotsDrawn < 258) || (dotsDrawn >= 321 && dotsDrawn < 338)) {
                    UpdateShifters();

                    switch ((dotsDrawn - 1) % 8) {
                        case 0:
                            LoadBackgroundShifters();
                            bgNextTileId = VRAM[MirrorVramAddr((ushort)(0x2000 | (loopyValue.GetAddress() & 0x0FFF)))];
                            break;
                        case 2:
                            bgNextTileAttribute = VRAM[MirrorVramAddr((ushort)(0x23c0 |
                                (loopyValue.NametableY << 11) |
                                (loopyValue.NametableX << 10) |
                                ((loopyValue.CoarseY >> 2) << 3) |
                                (loopyValue.CoarseX >> 2)))
                            ];

                            if ((loopyValue.CoarseY & 0x02) != 0) {
                                bgNextTileAttribute >>= 4;
                            }
                            if ((loopyValue.CoarseX & 0x02) != 0) {
                                bgNextTileAttribute >>= 2;
                            }
                            bgNextTileAttribute &= 0x03;
                            break;
                        case 4:
                            bgNextTileLsb = GetChrRom(backgroundPatternAddress + (bgNextTileId * 16) + loopyValue.FineY);
                            break;
                        case 6:
                            bgNextTileMsb = GetChrRom(backgroundPatternAddress + (bgNextTileId * 16) + loopyValue.FineY + 8);
                            break;
                        case 7:
                            IncrementScrollX();
                            break;
                    }
                }

                if (dotsDrawn == 256) {
                    IncrementScrollY();
                }

                if (dotsDrawn == 257) {
                    LoadBackgroundShifters();
                    ResetAddressX();
                }

                if (dotsDrawn == 338 || dotsDrawn == 340) {
                    bgNextTileId = VRAM[MirrorVramAddr((ushort)(0x2000 | (loopyValue.GetAddress() & 0x0FFF)))];
                }

                if (currentScanline == -1 && dotsDrawn >= 280 && dotsDrawn < 305) {
                    ResetAddressY();
                }

                // Sprite evaluation
                if (dotsDrawn == 257 && currentScanline >= 0) {
                    spriteCount = 0;

                    for (var j = 0; j < 8; j++) {
                        spriteShifterPatternLo[j] = 0;
                        spriteShifterPatternHi[j] = 0;
                    }

                    // var index = 0;
                    spriteZeroPossible = false;

                    unsafe {
                        fixed(SpriteEntry* ptr = spritesEvaluated) {
                            for (var index = 0; index < 64; index++) {
                                if (spriteCount >= 8) {
                                    isSpriteOverflow = true;
                                    break;
                                }

                                byte yPosition = OAM[index * 4];
                                byte tileIndex = OAM[(index * 4) + 1];
                                byte attributes = OAM[(index * 4) + 2];
                                byte xPosition = OAM[(index * 4) + 3];

                                if (yPosition == 0 && tileIndex == 0 && attributes == 0 && xPosition == 0) {
                                    continue;
                                }

                                int yDiff = currentScanline - yPosition;
                                if (yDiff >= 0 && yDiff < spriteSize) {
                                    if (spriteCount < 8) {
                                        SpriteEntry sprite;
                                        sprite.YPosition = yPosition;
                                        sprite.XPosition = xPosition;
                                        sprite.Attribute = attributes;
                                        sprite.TileId = tileIndex;
                                        if (index == 0) {
                                            sprite.SpriteZero = true;
                                            spriteZeroPossible = true;
                                        } else {
                                            sprite.SpriteZero = false;
                                        }

                                        *(ptr + spriteCount) = sprite;
                                        spriteCount++;
                                    } else {
                                        spriteCount++;
                                    }
                                }
                            }
                        }
                    }
                } 

                if (dotsDrawn == 340) {
                    var spriteIndex = 0;
                    foreach (var sprite in spritesEvaluated) {
                        var paletteVal = sprite.Attribute & 0b11;
                        var priority = (sprite.Attribute >> 5 & 1) == 0;
                        var flipHorizontal = (sprite.Attribute >> 6 & 1) == 1;
                        var flipVertical = (sprite.Attribute >> 7 & 1) == 1;

                        ushort patternAddrLo;
                        if (spriteSize == 8) {
                            if (flipVertical) {
                                patternAddrLo = (ushort)(spritePatternAddress | (sprite.TileId * 16) | (byte)(7 - (currentScanline - sprite.YPosition)));
                            } else {
                                patternAddrLo = (ushort)(spritePatternAddress | (sprite.TileId * 16) | (byte)(currentScanline - sprite.YPosition));
                            }

                        } else {
                            if (flipVertical) {
                                if (currentScanline - sprite.YPosition < 8) {
                                    patternAddrLo = (ushort)(
                                        (sprite.TileId & 1) << 12 |
                                        (((sprite.TileId & 0xFE) + 1) * 16) |
                                        (byte)(7 - (currentScanline - sprite.YPosition) & 0b111)
                                    );
                                } else {
                                    patternAddrLo = (ushort)(
                                        (sprite.TileId & 1) << 12 |
                                        ((sprite.TileId & 0xFE) * 16) |
                                        (byte)(7 - (currentScanline - sprite.YPosition) & 0b111)
                                    );
                                }
                            } else {
                                if (currentScanline - sprite.YPosition < 8) {
                                    patternAddrLo = (ushort)(
                                        (sprite.TileId & 1) << 12 |
                                        ((sprite.TileId & 0xFE) * 16) |
                                        (byte)((currentScanline - sprite.YPosition) & 0b111)
                                    );
                                } else {
                                    patternAddrLo = (ushort)(
                                        (sprite.TileId & 1) << 12 |
                                        (((sprite.TileId & 0xFE) + 1) * 16) |
                                        (byte)((currentScanline - sprite.YPosition) & 0b111)
                                    );
                                }
                            }
                        }

                        var patternAddrHi = (ushort)(patternAddrLo + 8);
                        byte patternBitsLo = GetChrRom(patternAddrLo);
                        byte patternBitsHi = GetChrRom(patternAddrHi);

                        if (flipHorizontal) {
                            // https://stackoverflow.com/a/2602885
                            // What the fuck
                            patternBitsLo = (byte)((patternBitsLo & 0xF0) >> 4 | (patternBitsLo & 0x0F) << 4);
                            patternBitsLo = (byte)((patternBitsLo & 0xCC) >> 2 | (patternBitsLo & 0x33) << 2);
                            patternBitsLo = (byte)((patternBitsLo & 0xAA) >> 1 | (patternBitsLo & 0x55) << 1);

                            patternBitsHi = (byte)((patternBitsHi & 0xF0) >> 4 | (patternBitsHi & 0x0F) << 4);
                            patternBitsHi = (byte)((patternBitsHi & 0xCC) >> 2 | (patternBitsHi & 0x33) << 2);
                            patternBitsHi = (byte)((patternBitsHi & 0xAA) >> 1 | (patternBitsHi & 0x55) << 1);
                        }

                        spriteShifterPatternLo[spriteIndex] = patternBitsLo;
                        spriteShifterPatternHi[spriteIndex] = patternBitsHi;

                        spriteIndex++;

                        if (spriteIndex >= spriteCount) {
                            break;
                        }
                    }
                } 

            }

            if (currentScanline == 240) {
                // Nothing?
            }

            if (currentScanline == 241 && dotsDrawn == 1) {
                isVBlank = true;
                if (generateVBlank) {
                    isInNmiInterrupt = true;
                }
            }

            byte bgPixel = 0;
            byte bgPalette = 0;

            byte fgPixel = 0;
            byte fgPalette = 0;

            var spritePriority = false;

            if (renderBackground && (dotsDrawn > 0 && dotsDrawn <= 256 && currentScanline >= 0 && currentScanline < 240)) {
                var bitMux = (ushort)(0x8000 >> scrollFineX);

                var p0Pixel = (byte)((bgShifterPatternLo & bitMux) > 0 ? 1 : 0);
                var p1Pixel = (byte)((bgShifterPatternHi & bitMux) > 0 ? 1 : 0);
                bgPixel = (byte)((p1Pixel << 1) | p0Pixel);

                var p0Palette = (byte)((bgShifterAttributeLo & bitMux) > 0 ? 1 : 0);
                var p1Palette = (byte)((bgShifterAttributeHi & bitMux) > 0 ? 1 : 0);
                bgPalette = (byte)((p1Palette << 1) | p0Palette);
            }

            if (renderSprites && (dotsDrawn > 0 && dotsDrawn <= 256 && currentScanline >= 0 && currentScanline < 240)) {
                spriteZeroRendered = false;

                var index = 0;
                foreach (var sprite in spritesEvaluated) {
                    if (index >= spriteCount) {
                        break;
                    }

                    if (sprite.XPosition == 0) {
                        int spritePixelLo = (spriteShifterPatternLo[index] & 0x80) > 0 ? 1 : 0;
                        int spritePixelHi = (spriteShifterPatternHi[index] & 0x80) > 0 ? 1 : 0;

                        fgPixel = (byte)((spritePixelHi << 1) | spritePixelLo);
                        fgPalette = (byte)((sprite.Attribute & 0b11) + 0b100);
                        spritePriority = (sprite.Attribute & 0x20) == 0;

                        if (fgPixel != 0) {
                            if (sprite.SpriteZero) {
                                spriteZeroRendered = true;
                            }

                            // Since sprites are sorted in priority, if we actually find a sprite for the pixel, we can just skip the rest
                            break;
                        }
                    }
                    index++;
                }
            }

            byte renderPixel = 0;
            byte renderPalette = 0;

            var isBg = false;

            if (bgPixel == 0 && fgPixel == 0) {
                // Just continue;
            } else if (bgPixel == 0 && fgPixel > 0) {
                isBg = false;
                renderPixel = fgPixel;
                renderPalette = fgPalette;
            } else if (bgPixel > 0 && fgPixel == 0) {
                isBg = true;
                renderPixel = bgPixel;
                renderPalette = bgPalette;
            } else if (bgPixel > 0 && fgPixel > 0) {
                if (spritePriority) {
                    isBg = true;
                    renderPixel = fgPixel;
                    renderPalette = fgPalette;
                } else {
                    isBg = false;
                    renderPixel = bgPixel;
                    renderPalette = bgPalette;
                }

                if (spriteZeroPossible && spriteZeroRendered) {
                    if (renderBackground && renderSprites) {
                        bool backgroundLeft = backgroundLeftColumn;
                        bool spriteLeft = spriteLeftcolumn;

                        if (backgroundLeft && spriteLeft) {
                            if (dotsDrawn >= 1 && dotsDrawn <= 258) {
                                isSpriteZeroHit = true;
                            }
                        } else {
                            if (dotsDrawn >= 9 && dotsDrawn <= 258) {
                                isSpriteZeroHit = true;
                            }
                        }
                    } 
                }
            }

            (byte, byte, byte) color;
            if (isBg) {
                if (!backgroundLeftColumn && dotsDrawn <= 8) {
                    color = Palette.SystemPalette[PALETTE[0] & 0x3f];
                } else {
                    color = Palette.SystemPalette[PALETTE[(renderPalette << 2) + renderPixel] & 0x3f];
                }
            } else {
                if (!spriteLeftcolumn && dotsDrawn <= 8) {
                    color = Palette.SystemPalette[PALETTE[0] & 0x3f];
                } else {
                    color = Palette.SystemPalette[PALETTE[(renderPalette << 2) + renderPixel] & 0x3f];
                }
            }

            var pixelX = dotsDrawn - 1;
            var pixelY = currentScanline;
            if (pixelX >= 0 && pixelX < 256 && pixelY >= 0 && pixelY < 240) {
                frameBuffer[
                    pixelX +
                    pixelY * 256
                ] = (uint)((color.Item1 << 16) | (color.Item2 << 8 | (color.Item3 << 0)));
            }

            dotsDrawn++;
            if (currentScanline < 240 && dotsDrawn == 260) {
                if (renderBackground || renderSprites) {
                    bus.currentMapper.DecrementScanline();
                }
            }
            if (dotsDrawn >= 341) {
                dotsDrawn = 0;
                currentScanline++;
                if (currentScanline >= 261) {
                    currentScanline = -1;
                    isVBlank = false;
                    isInNmiInterrupt = false;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Warning: Sets NmiInterrupt to false after getting value
        /// If you just want to check, use IsInterrupt()
        /// </summary>
        /// <returns></returns>
        public bool GetInterrupt() {
            bool interrupt = isInNmiInterrupt;
            isInNmiInterrupt = false;
            return interrupt;
        }

        public void WriteCtrl(byte value) {
            loopyTemp.NametableX = (byte)(value & 0b01);
            loopyTemp.NametableY = (byte)((value & 0b10) >> 1);

            bool beforeNmi = generateVBlank;
            
            controlRegister = value;
            
            vramAddressIncrement = (controlRegister & (1 << 2)) > 1 ? (byte)32 : (byte)1;
            backgroundPatternAddress = (controlRegister & (1 << 4)) > 1 ? (ushort)0x1000 : (ushort)0;
            spritePatternAddress = (controlRegister & (1 << 3)) > 1 ? (ushort)0x1000 : (ushort)0;
            spriteSize = (controlRegister & (1 << 5)) > 1 ? (byte)16 : (byte)8;
            generateVBlank = (controlRegister & (1 << 7)) > 1;
            
            if (!beforeNmi && generateVBlank && isVBlank) {
                isInNmiInterrupt = true;
            }
        }
        
        void IncrementScrollX() {
            if (renderSprites || renderBackground) {
                if (loopyValue.CoarseX == 31) {
                    loopyValue.CoarseX = 0;

                    // Flip the X bit
                    if (loopyValue.NametableX == 0) {
                        loopyValue.NametableX = 1;
                    } else {
                        loopyValue.NametableX = 0;
                    }
                    //V_Loopy.NametableX = (byte)(~V_Loopy.NametableX);
                } else {
                    loopyValue.CoarseX++;
                }
            }
        }

        void IncrementScrollY() {
            if (renderSprites || renderBackground) {
                if (loopyValue.FineY < 7) {
                    loopyValue.FineY++;
                } else {
                    loopyValue.FineY = 0;

                    if (loopyValue.CoarseY == 29) {
                        loopyValue.CoarseY = 0;
                        // Flip the Y bit
                        if (loopyValue.NametableY == 0) {
                            loopyValue.NametableY = 1;
                        } else {
                            loopyValue.NametableY = 0;
                        }
                    } else if (loopyValue.CoarseY == 31) {
                        loopyValue.CoarseY = 0;
                    } else {
                        loopyValue.CoarseY++;
                    }
                }
            }
        }

        void ResetAddressX() {
            if (renderSprites || renderBackground) {
                loopyValue.NametableX = loopyTemp.NametableX;//(byte)((V_Loopy.Nametable & 0b1) | ( & 0b10));
                loopyValue.CoarseX = loopyTemp.CoarseX;
            }
        }

        void ResetAddressY() {
            if (renderSprites || renderBackground) {
                loopyValue.NametableY = loopyTemp.NametableY;//(byte)((V_Loopy.Nametable & 0b10) | (T_Loopy.Nametable & 0b1));
                loopyValue.FineY = loopyTemp.FineY;
                loopyValue.CoarseY = loopyTemp.CoarseY;
            }
        }

        void LoadBackgroundShifters() {
            bgShifterPatternLo = (ushort)((bgShifterPatternLo & 0xFF00) | bgNextTileLsb);
            bgShifterPatternHi = (ushort)((bgShifterPatternHi & 0xFF00) | bgNextTileMsb);

            int attributeLo = bgNextTileAttribute & 0b01;
            int attributeHi = bgNextTileAttribute & 0b10;

            if (attributeLo != 0) {
                bgShifterAttributeLo = (ushort)((bgShifterAttributeLo & 0xFF00) | 0xFF);
            } else {
                bgShifterAttributeLo = (ushort)((bgShifterAttributeLo & 0xFF00));
            }
            if (attributeHi != 0) {
                bgShifterAttributeHi = (ushort)((bgShifterAttributeHi & 0xFF00) | 0xFF);
            } else {
                bgShifterAttributeHi = (ushort)((bgShifterAttributeHi & 0xFF00));
            }
        }
        
        void UpdateShifters() {
            unsafe {
                fixed (SpriteEntry* ptr = spritesEvaluated) {
                    if (renderBackground) {
                        bgShifterAttributeLo <<= 1;
                        bgShifterAttributeHi <<= 1;
                        bgShifterPatternLo <<= 1;
                        bgShifterPatternHi <<= 1;
                    }

                    if (renderSprites && dotsDrawn >= 1 && dotsDrawn < 258) {
                        for (var index  = 0; index < spriteCount; index++) {
                            var sprite = *(ptr + index);
                            if (sprite.XPosition > 0) {
                                sprite.XPosition -= 1;
                                *(ptr + index) = sprite;
                            } else {
                                spriteShifterPatternLo[index] <<= 1;
                                spriteShifterPatternHi[index] <<= 1;
                            }
                        }
                    }
                }
            }
        }

        public void WriteScroll(byte value) {
            if (scrollWriteLatch) {
                int coarseY = value >> 3;
                int fineY = value & 0b111;

                loopyTemp.CoarseY = (byte)coarseY;
                loopyTemp.FineY = (byte)fineY;

                scrollWriteLatch = false;
            } else {
                scrollFineX = (byte)(value & 0b111);
                int coarseX = value >> 3;
                loopyTemp.CoarseX = (byte)coarseX;

                scrollWriteLatch = true;
            }
        }

        public void WritePPUAddress(byte value) {
            if (scrollWriteLatch) {
                loopyTemp.Update((ushort)((loopyTemp.GetAddress() & 0xFF00) | value));
                loopyValue.Update(loopyTemp.GetAddress());
                scrollWriteLatch = false;
            } else {
                loopyTemp.Update((ushort)(((value & 0x3f) << 8) | (loopyTemp.GetAddress() & 0x00ff)));
                scrollWriteLatch = true;
            }
        }

        public byte GetData() {
            ushort addr = loopyValue.GetAddress();
            IncrementVramAddr();

            return InternalRead(addr);
        }

        byte InternalRead(ushort addr) {
            byte mapperValue = bus.currentMapper.PPURead(addr, out mapperDidMap);
            if (mapperDidMap) {
                return mapperValue;
            }

            if (addr <= 0x1fff) {
                byte result = readBuffer;
                readBuffer = GetChrRom(addr);
                return result;
            }

            // Nametable
            if (addr <= 0x2fff) {
                byte result = readBuffer;
                readBuffer = VRAM[MirrorVramAddr(addr)];
                return result;
            }

            if (addr <= 0x3eff) {
                // Normally undefined address space, but some games depend on this like Zelda
                byte result = readBuffer;
                readBuffer = VRAM[MirrorVramAddr(addr)];
                return result;
            }

            if (addr == 0x3f10 || addr == 0x3f14 || addr == 0x3f18 || addr == 0x3f1c) {
                int mirror = addr - 0x10;
                return PALETTE[mirror - 0x3f00];
            }

            if (addr <= 0x3fff) {
                return PALETTE[addr - 0x3f00];
            }

            throw new Exception("Reached unknown address");
        }

        public void WriteData(byte value) {
            ushort addr = loopyValue.GetAddress();
            IncrementVramAddr();

            bus.currentMapper.PPUWrite(addr, value, out mapperDidMap);
            if (mapperDidMap) {
                return;
            }

            if (addr <= 0x1fff) {
                CHR[addr] = value;
                return;
            }

            if (addr <= 0x2fff) {
                var mirror = MirrorVramAddr(addr);
                VRAM[mirror] = value;
                return;
            }

            if (addr <= 0x3eff) {
                //Console.WriteLine("0x3000 > x 0x3eff being used");
                //throw new NotImplementedException("Shouldn't be used");
                return;
            }


            if (addr == 0x3f10 || addr == 0x3f14 || addr == 0x3f18 || addr == 0x3f1c) {
                PALETTE[(addr - 0x10) - 0x3f00] = value;
                return;
            }

            if (addr <= 0x3fff) {
                PALETTE[addr - 0x3f00] = value;
                return;
            }
        }

        public byte GetStatus() {
            byte status = 0;

            if (isSpriteOverflow) {
                status |= 1 << 5;
            }
            if (isSpriteZeroHit) {
                status |= 1 << 6;
            }
            if (isVBlank) {
                status |= 1 << 7;
            }
            
            isVBlank = false;
            scrollWriteLatch = false;
            return status;
        }

        public byte GetOAMData() {
            return OAM[OAMAddress];
        }
        public void WriteOAMData(byte value) {
            OAM[OAMAddress] = value;
            OAMAddress++;
        }

        public void SetMask(byte value) {
            maskRegister = value;
            backgroundLeftColumn = (maskRegister & (1 << 1)) > 0;
            spriteLeftcolumn = (maskRegister & (1 << 2)) > 0;
            renderBackground = (maskRegister & (1 << 3)) > 0;
            renderSprites = (maskRegister & (1 << 4)) > 0;
        }
        
        public void DrawFrame(ref nint renderer, ref nint Texture) {
            unsafe {
                SDL.SDL_Rect rect;
                rect.w = 256 * 3;
                rect.h = 240 * 3;
                rect.x = 0;
                rect.y = 0;

                fixed (uint* pArray = frameBuffer) {
                    var intPtr = new nint(pArray);

                    _ = SDL.SDL_UpdateTexture(Texture, ref rect, intPtr, 256 * 4);
                }

                _ = SDL.SDL_RenderCopy(renderer, Texture, nint.Zero, ref rect);
                SDL.SDL_RenderPresent(renderer);
            }
        }
    }
}
