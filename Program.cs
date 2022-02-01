using NesEmu.CPU;
using NesEmu.PPU;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using SDL2;
using System.Runtime.InteropServices;
using System.Linq;

namespace NesEmu {
    class Program {
        static Random rng;
        static string fileName;
        static NesCpu cpu;
        static UInt64 currentFrame = 0;

        static IntPtr Texture;
        static uint[] FrameBuffer = new uint[256 * 240];

        static void Main(string[] args) {
            // Initilizes SDL.
            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0) {
                Console.WriteLine($"There was an issue initilizing SDL. {SDL.SDL_GetError()}");
            }

            // Create a new window given a title, size, and passes it a flag indicating it should be shown.
            var window = SDL.SDL_CreateWindow("Nes",
                                              SDL.SDL_WINDOWPOS_UNDEFINED,
                                              SDL.SDL_WINDOWPOS_UNDEFINED,
                                              256 * 3,
                                              240 * 3,
                                              SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE | 
                                              SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN);

            if (window == IntPtr.Zero) {
                Console.WriteLine($"There was an issue creating the window. {SDL.SDL_GetError()}");
            }

            // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
            var renderer = SDL.SDL_CreateRenderer(window,
                                                  -1,
                                                  SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            if (renderer == IntPtr.Zero) {
                Console.WriteLine($"There was an issue creating the renderer. {SDL.SDL_GetError()}");
            }

#if !NESTEST
            Console.WriteLine("Enter the filename of the .nes file to run");
            fileName = Console.ReadLine();
#else
            string fileName = "nestest.nes";
#endif
            byte[] romByteArr;
            try {
                romByteArr = File.ReadAllBytes(fileName);
            } catch (Exception e) {
                throw new FileNotFoundException("Couldn't find file, try again");
            }
            var rom = new Rom.Rom(romByteArr);
            cpu = new NesCpu(rom);

            var running = true;
            var lastScanline = 0;
            currentFrame = 0;
            Stopwatch sw = new Stopwatch();

            Texture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGB888, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 256, 240);

            // Main loop for the program
            while (running) {
                // Check to see if there are any events and continue to do so until the queue is empty.
                while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1) {
                    var key = e.key;
                    switch (e.type) {
                        case SDL.SDL_EventType.SDL_KEYDOWN:
                            HandleKeyDown(cpu, key);
                            break;
                        case SDL.SDL_EventType.SDL_KEYUP:
                            HandleKeyUp(cpu, key);
                            break;
                        case SDL.SDL_EventType.SDL_QUIT:
                            running = false;
                            break;
                    }
                }

                //// Sets the color that the screen will be cleared with.
                //if (SDL.SDL_SetRenderDrawColor(renderer, 135, 206, 235, 255) < 0) {
                //    Console.WriteLine($"There was an issue with setting the render draw color. {SDL.SDL_GetError()}");
                //}

                //// Clears the current render surface.
                //if (SDL.SDL_RenderClear(renderer) < 0) {
                //    Console.WriteLine($"There was an issue with clearing the render surface. {SDL.SDL_GetError()}");
                //}
                //sw.Restart();
                var scanline = cpu.RunScanline();
                RenderScanline(cpu.Bus.PPU, scanline);

                if (scanline < lastScanline) {
                    //sw.Stop();
                    currentFrame++;
                    DrawFrame(renderer);
                    //if (sw.ElapsedMilliseconds <= 16) {
                    //    Thread.Sleep(16 - (int)sw.ElapsedMilliseconds);
                    //}
                }
                lastScanline = scanline;
            }

            // Clean up the resources that were created.
            SDL.SDL_DestroyRenderer(renderer);
            SDL.SDL_DestroyWindow(window);
            SDL.SDL_Quit();
        }

        private static void HandleKeyDown(NesCpu cpu, SDL.SDL_KeyboardEvent key) {
            byte currentKeys = cpu.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL.SDL_Keycode.SDLK_j:
                    currentKeys |= 0b10000000;
                    break;
                case SDL.SDL_Keycode.SDLK_k:
                    currentKeys |= 0b01000000;
                    break;
                case SDL.SDL_Keycode.SDLK_RSHIFT:
                    currentKeys |= 0b00100000;
                    break;
                case SDL.SDL_Keycode.SDLK_RETURN:
                    currentKeys |= 0b00010000;
                    break;
                case SDL.SDL_Keycode.SDLK_w:
                    currentKeys |= 0b00001000;
                    break;
                case SDL.SDL_Keycode.SDLK_s:
                    currentKeys |= 0b00000100;
                    break;
                case SDL.SDL_Keycode.SDLK_a:
                    currentKeys |= 0b00000010;
                    break;
                case SDL.SDL_Keycode.SDLK_d:
                    currentKeys |= 0b00000001;
                    break;
            }

            cpu.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleKeyUp(NesCpu cpu, SDL.SDL_KeyboardEvent key) {
            byte currentKeys = cpu.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL.SDL_Keycode.SDLK_j:
                    currentKeys &= 0b01111111;
                    break;
                case SDL.SDL_Keycode.SDLK_k:
                    currentKeys &= 0b10111111;
                    break;
                case SDL.SDL_Keycode.SDLK_RSHIFT:
                    currentKeys &= 0b11011111;
                    break;
                case SDL.SDL_Keycode.SDLK_RETURN:
                    currentKeys &= 0b11101111;
                    break;
                case SDL.SDL_Keycode.SDLK_w:
                    currentKeys &= 0b11110111;
                    break;
                case SDL.SDL_Keycode.SDLK_s:
                    currentKeys &= 0b11111011;
                    break;
                case SDL.SDL_Keycode.SDLK_a:
                    currentKeys &= 0b11111101;
                    break;
                case SDL.SDL_Keycode.SDLK_d:
                    currentKeys &= 0b11111110;
                    break;
            }

            cpu.Bus.Controller1.Update(currentKeys);
        }

        private static void DrawFrame(IntPtr renderer) {
            unsafe {
                fixed (uint* pArray = FrameBuffer) {
                    IntPtr intPtr = new IntPtr((void*)pArray);

                    SDL.SDL_Rect rect;
                    rect.w = 256 * 3;
                    rect.h = 240 * 3;
                    rect.x = 0;
                    rect.y = 0;

                    SDL.SDL_UpdateTexture(Texture, ref rect, intPtr, 256 * 4);
                }

                SDL.SDL_RenderCopy(renderer, Texture, IntPtr.Zero, IntPtr.Zero);
                SDL.SDL_RenderPresent(renderer);
            }
        }

        private static void SetPixel(int x, int y, (byte r, byte g, byte b) color) {
            FrameBuffer[
                x +
                (y * 256)
            ] = (uint)((color.r << 16) | (color.g << 8 | (color.b << 0)));
        }

        private static bool RenderScanline(PPU.PPU ppu, int scanline) {
            if (scanline >= 240) {
                return false;
            }

            var bank = ppu.GetBackgroundPatternAddr();
            for (var tileIndex = 0; tileIndex < 0x3c0; tileIndex++) {
                var tileAddr = ppu.Vram[tileIndex];
                var tile_x = tileIndex % 32;
                var tile_y = tileIndex / 32;
                var palette = ppu.GetNametableTilePalette((byte)tile_x, (byte)tile_y);

                var tile = ppu.ChrRom[(bank + tileAddr * 16)..(bank + tileAddr * 16 + 16)];

                for (var y = 0; y <= 7; y++) {
                    var pixelY = tile_y * 8 + y;
                    var upper = tile[y];
                    var lower = tile[y + 8];

                    if (pixelY != scanline) {
                        continue;
                    }

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

                        SetPixel(pixelX, pixelY, color);
                    }
                }
            }

            return true;
        }
    }
}
