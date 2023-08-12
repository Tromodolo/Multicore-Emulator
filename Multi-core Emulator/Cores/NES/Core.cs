using BizHawk.NES;
using MultiCoreEmulator.Utility.SDL;
using NesEmu.Bus;
using NesEmu.CPU;
using NesEmu.PPU;
using NesEmu.Rom;
using static NesEmu.PPU.PPU;
using System;
using static SDL2.SDL;
using System.Threading.Tasks;
using System.Drawing;
using System.Xml;
using System.Reflection.Metadata.Ecma335;

namespace MultiCoreEmulator.Cores.NES {
    internal class Core : EmulatorCoreBase {
        public nint Texture;
        public nint Window;
        public nint Renderer;
        
        PPU PPU;
        Bus Bus;
        APU APU;
        NesCpu CPU;
        Rom Rom;

        short[]? samplesOut;

        public nint InitializeWindow(string windowName = "NES", int windowWidth = 256, int windowHeight = 240) {
            // Create a new window given a title, size, and passes it a flag indicating it should be shown.
            Window = SDL_CreateWindow(windowName, SDL_WINDOWPOS_UNDEFINED, SDL_WINDOWPOS_UNDEFINED, windowWidth * 3, windowHeight * 3, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_SHOWN);

            if (Window == nint.Zero) {
                Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
                return nint.Zero;
            }

            // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
            nint renderer = SDL_CreateRenderer(Window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            if (renderer == nint.Zero) {
                Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
                return nint.Zero;
            }

            Texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, windowWidth, windowHeight);
            Renderer = renderer;

            return Window;
        }

        public virtual void CloseWindow() {
            // Clean up the resources that were created.
            SDL_DestroyRenderer(Renderer);
            SDL_DestroyWindow(Window);
            SDL_Quit();
        }

        public void LoadBytes(string fileName, byte[] bytes) {
            Rom = new Rom(bytes, fileName);

            CPU = new NesCpu();
            PPU = new PPU(Rom.ChrRom);
            APU = new APU(CPU, null, false);
            Bus = new Bus(CPU, PPU, APU, Rom);

            CPU.RegisterBus(Bus);
            PPU.RegisterBus(Bus);
        }

        public void Reset() {
            APU.NESHardReset();
            CPU.Reset();
            Bus.Reset();
        }

        public void ClockSamples(int numAudioSamples) {
            while (Bus.SamplesCollected < numAudioSamples) {
                Bus.Clock();

                if (Bus.PendingFrame) {
                    PPU.DrawFrame(ref Renderer, ref Texture);
                    Bus.PendingFrame = false;        
                }
            }

            Bus.SamplesCollected -= numAudioSamples;
        }

        public short[] GetSamples(int numAudioSamples) {
            if (samplesOut == null || samplesOut.Length != numAudioSamples) {
                samplesOut = new short[numAudioSamples];
            }
            
            Bus.AudioBuffer.FillBuffer(ref samplesOut);
            return samplesOut;
        }

        public void SaveState(int slot) {
            string gameName = Rom.Filename.Split('\\').LastOrDefault();
            gameName = gameName.Replace(".nes", "");
            gameName = gameName.Replace(".nez", "");
            var stateFileName = $"{gameName}.{slot}.state";

            var fileStream = new FileStream(stateFileName, FileMode.OpenOrCreate);
            var binaryWriter = new BinaryWriter(fileStream);
            CPU.Save(binaryWriter);
            PPU.Save(binaryWriter);
            Bus.Save(binaryWriter);
            fileStream.Close();
        }

        public void LoadState(int slot) {
            string gameName = Rom.Filename.Split('\\').LastOrDefault();
            gameName = gameName.Replace(".nes", "");
            gameName = gameName.Replace(".nez", "");
            var stateFileName = $"{gameName}.{slot}.state";

            if (!File.Exists(stateFileName))
                return;
            var fileStream = new FileStream(stateFileName, FileMode.Open);
            var binaryReader = new BinaryReader(fileStream);
            CPU.Load(binaryReader);
            PPU.Load(binaryReader);
            Bus.Load(binaryReader);
            fileStream.Close();
        }

        public void HandleKeyDown(SDL_KeyboardEvent keyboardEvent) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (keyboardEvent.keysym.sym) {
                case SDL_Keycode.SDLK_r:
                    Reset();
                    break;
                case SDL_Keycode.SDLK_j:
                    currentKeys |= 0b10000000;
                    break;
                case SDL_Keycode.SDLK_k:
                    currentKeys |= 0b01000000;
                    break;
                case SDL_Keycode.SDLK_RSHIFT:
                    currentKeys |= 0b00100000;
                    break;
                case SDL_Keycode.SDLK_RETURN:
                    currentKeys |= 0b00010000;
                    break;
                case SDL_Keycode.SDLK_w:
                    currentKeys |= 0b00001000;
                    break;
                case SDL_Keycode.SDLK_s:
                    currentKeys |= 0b00000100;
                    break;
                case SDL_Keycode.SDLK_a:
                    currentKeys |= 0b00000010;
                    break;
                case SDL_Keycode.SDLK_d:
                    currentKeys |= 0b00000001;
                    break;
                default:
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void HandleKeyUp(SDL_KeyboardEvent keyboardEvent) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (keyboardEvent.keysym.sym) {
                case SDL_Keycode.SDLK_j:
                    currentKeys &= 0b01111111;
                    break;
                case SDL_Keycode.SDLK_k:
                    currentKeys &= 0b10111111;
                    break;
                case SDL_Keycode.SDLK_RSHIFT:
                    currentKeys &= 0b11011111;
                    break;
                case SDL_Keycode.SDLK_RETURN:
                    currentKeys &= 0b11101111;
                    break;
                case SDL_Keycode.SDLK_w:
                    currentKeys &= 0b11110111;
                    break;
                case SDL_Keycode.SDLK_s:
                    currentKeys &= 0b11111011;
                    break;
                case SDL_Keycode.SDLK_a:
                    currentKeys &= 0b11111101;
                    break;
                case SDL_Keycode.SDLK_d:
                    currentKeys &= 0b11111110;
                    break;
                default:
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void HandleButtonDown(SDL_GameControllerButton button) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (button) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B:
                    currentKeys |= 0b10000000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A:
                    currentKeys |= 0b01000000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK:
                    currentKeys |= 0b00100000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START:
                    currentKeys |= 0b00010000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP:
                    currentKeys |= 0b00001000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN:
                    currentKeys |= 0b00000100;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT:
                    currentKeys |= 0b00000010;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT:
                    currentKeys |= 0b00000001;
                    break;
                default:
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void HandleButtonUp(SDL_GameControllerButton button) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (button) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B:
                    currentKeys &= 0b01111111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A:
                    currentKeys &= 0b10111111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK:
                    currentKeys &= 0b11011111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START:
                    currentKeys &= 0b11101111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP:
                    currentKeys &= 0b11110111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN:
                    currentKeys &= 0b11111011;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT:
                    currentKeys &= 0b11111101;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT:
                    currentKeys &= 0b11111110;
                    break;
                default:
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void RenderDebugView(DebugWindow debugWindow) {
#if false
// Come back to this later
            var linePosX = 8;
            var linePosY = 8;

            var addLine = (string text) => {
                debugWindow.DrawText(text, linePosX, linePosY, Colors.White);

                linePosY += 8;
            };

            addLine("Registers");
            addLine($"A: {CPU.Accumulator.ToString("X2")} X: {CPU.RegisterX.ToString("X2")} Y: {CPU.RegisterY.ToString("X2")}");
            addLine($"PC: {CPU.ProgramCounter.ToString("X4")} SP: {CPU.StackPointer.ToString("X2")} Status: {((int)CPU.Status).ToString("X2")}");

            linePosY += 8;

            addLine("PPU Status");
            addLine($"Scanline: {PPU.CurrentScanline.ToString().PadLeft(3, '0')} Dots Drawn: {PPU.DotsDrawn.ToString().PadLeft(3, '0')}");
            addLine($"Cycles: {PPU.TotalCycles}");

            linePosY += 8;

            addLine("PPU Registers");
            addLine($"Mask: {PPU.Mask.Get().ToString("X2")} Ctrl: {PPU.Ctrl.Get().ToString("X2")} Status: {PPU.Status.GetSnapshot().ToString("X2")}");
            addLine($"Scroll X: {PPU.Scroll_T_Loopy.CoarseX} Scroll Y: {PPU.Scroll_T_Loopy.CoarseY.ToString().PadLeft(3, '0')} Fine X: {PPU.ScrollFineX} Fine Y: {PPU.Scroll_T_Loopy.FineY.ToString().PadLeft(3, '0')}");
            addLine($"Nametable X: {PPU.Scroll_T_Loopy.NametableX} Nametable Y: {PPU.Scroll_T_Loopy.NametableY}");

            var getNameTablePalette = (byte tileX, byte tileY) => {
                var attrTableIndex = tileY / 4 * 8 + tileX / 4;
                var attrValue = PPU.Vram[0x3c0 + attrTableIndex];

                var segmentX = tileX % 4 / 2;
                var segmentY = tileY % 4 / 2;

                var paletteIndex = (segmentX, segmentY) switch {
                    (0, 0) => attrValue & 0b11,
                    (1, 0) => (attrValue >> 2) & 0b11,
                    (0, 1) => (attrValue >> 4) & 0b11,
                    (1, 1) => (attrValue >> 6) & 0b11,
                    (_, _) => attrValue & 0b11,
                };

                var paletteStart = 1 + paletteIndex * 4;
                return new byte[] {
                    PPU.PaletteTable[0],
                    PPU.PaletteTable[paletteStart],
                    PPU.PaletteTable[paletteStart + 1],
                    PPU.PaletteTable[paletteStart + 2],
                };
            };

            var index = 0;
            var OamData = PPU.OamData;
            var spriteSize = PPU.GetSpriteSize();
            var yOffset = 110;
            var xOffset = 8;
            foreach (byte oamEntry in OamData) {
                if (index >= 64) {
                    break;
                }

                byte yPosition = OamData[index * 4];
                byte tileIndex = OamData[(index * 4) + 1];
                byte attributes = OamData[(index * 4) + 2];
                byte xPosition = OamData[(index * 4) + 3];

                if (yPosition >= 232 || xPosition >= 248) {
                    for (var x = 0; x < 8; x++) {
                        for (var y = 0; y < 8; y++) {
                            debugWindow.SetPixel(xOffset + x, yOffset + y, (20, 20, 20));
                        }
                    }
                    xOffset += 12;
                    if (xOffset >= 346) {
                        xOffset = 8;
                        yOffset += 12;
                    }
                    index++;
                    continue;
                }

                var paletteVal = attributes & 0b11;
                var priority = (attributes >> 5 & 1) == 0;
                var flipHorizontal = (attributes >> 6 & 1) == 1;
                var flipVertical = (attributes >> 7 & 1) == 1;

                var paletteStart = 17 + (paletteVal * 4);
                var palette = new byte[] {
                    0,
                    PPU.PaletteTable[paletteStart],
                    PPU.PaletteTable[paletteStart + 1],
                    PPU.PaletteTable[paletteStart + 2],
                };

                var spriteBank = PPU.GetSpritePatternAddr();
                var sprite = new byte[16];
                for (var i = 0; i < 16; i++) {
                    sprite[i] = PPU.GetChrRom(spriteBank + tileIndex * 16 + i);
                }

                for (var y = 0; y <= 7; y++) {
                    var upper = sprite[y];
                    var lower = sprite[y + 8];

                    for (var x = 7; x >= 0; x--) {
                        var value = (1 & lower) << 1 | (1 & upper);
                        upper = (byte)(upper >> 1);
                        lower = (byte)(lower >> 1);
                        (byte r, byte g, byte b) color;
                        switch (value) {
                            case 0: // Should be transparent
                                color = (0, 0, 0);
                                break;
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

                        debugWindow.SetPixel(xOffset + x, yOffset + y, color);
                    }
                }
                xOffset += 12;
                if (xOffset >= 346) {
                    xOffset = 8;
                    yOffset += 12;
                }

                index++;
            }

            yOffset += 16;
            for (var nametableIndex = 0; nametableIndex < 4; nametableIndex++) {
                var nametableXOffset = 0;
                var nametableYOffset = 0;

                switch (nametableIndex) {
                    case 0:
                        nametableYOffset = 0;
                        nametableXOffset = 0;
                        break;
                    case 1:
                        nametableYOffset = 0;
                        nametableXOffset = 256;
                        break;
                    case 2:
                        nametableYOffset = 240;
                        nametableXOffset = 0;
                        break;
                    case 3:
                        nametableYOffset = 240;
                        nametableXOffset = 256;
                        break;
                }

                var offset = 0x400 * nametableIndex + 0x2000;
                for (var addr = 0; addr < 0x3c0; addr++) {
                    var memoryAddr = PPU.MirrorVramAddr((ushort)(offset + addr));

                    var bank = PPU.GetBackgroundPatternAddr();
                    var tileAddr = PPU.Vram[memoryAddr];
                    var tile_x = addr % 32;
                    var tile_y = addr / 32;

                    var attrTableIndex = tile_y / 4 * 8 + tile_x / 4;
                    var attrValue = PPU.Vram[
                        PPU.MirrorVramAddr((ushort)(offset + 0x3c0 + attrTableIndex))
                    ];

                    var segmentX = tile_x % 4 / 2;
                    var segmentY = tile_y % 4 / 2;

                    byte paletteIndex = 0;
                    if (segmentX == 0) {
                        if (segmentY == 0) {
                            paletteIndex = (byte)(attrValue & 0b11);
                        } else if (segmentY == 1) {
                            paletteIndex = (byte)((attrValue >> 4) & 0b11);
                        }
                    } else {
                        if (segmentY == 0) {
                            paletteIndex = (byte)((attrValue >> 2) & 0b11);
                        } else if (segmentY == 1) {
                            paletteIndex = (byte)((attrValue >> 6) & 0b11);
                        }
                    }

                    var paletteStart = 1 + paletteIndex * 4;
                    var palette = new byte[] {
                        PPU.PaletteTable[0],
                        PPU.PaletteTable[paletteStart],
                        PPU.PaletteTable[paletteStart + 1],
                        PPU.PaletteTable[paletteStart + 2],
                    };

                    var tile = PPU.ChrRom[(bank + tileAddr * 16)..(bank + tileAddr * 16 + 16)];
                    for (var y = 0; y <= 7; y++) {
                        var pixelY = tile_y * 8 + y;
                        var upper = tile[y];
                        var lower = tile[y + 8];

                        for (var x = 7; x >= 0; x--) {
                            var pixelX = tile_x * 8 + x;

                            var value = (1 & lower) << 1 | (1 & upper);
                            upper = (byte)(upper >> 1);
                            lower = (byte)(lower >> 1);
                            (byte r, byte g, byte b) color;
                            switch (value) {
                                case 0:
                                    color = Palette.SystemPalette[palette[0]];
                                    break;
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

                            debugWindow.SetPixel(pixelX + nametableXOffset, pixelY + yOffset + nametableYOffset, color);
                        }
                    }
                }
            }
#endif
        }
    }
}
