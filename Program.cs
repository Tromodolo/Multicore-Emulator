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
        static uint[] FrameBuffer = new uint[256*240];

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

            Texture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGB888, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC, 256 * 3, 240 * 3);

            // Main loop for the program
            while (running) {
                // Check to see if there are any events and continue to do so until the queue is empty.
                while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1) {
                    switch (e.type) {
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

                cpu.RunScanline((scanline) => {
                    if (scanline < lastScanline) {
                        currentFrame++;
                        DrawFrame(renderer);
                    }
                    lastScanline = scanline;
                    return RenderScanline(cpu.Bus.PPU, scanline);
                });
            }

            // Clean up the resources that were created.
            SDL.SDL_DestroyRenderer(renderer);
            SDL.SDL_DestroyWindow(window);
            SDL.SDL_Quit();
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

                    SDL.SDL_UpdateTexture(Texture, ref rect, intPtr, 4);
                }

                SDL.SDL_RenderCopy(renderer, Texture, IntPtr.Zero, IntPtr.Zero);
                SDL.SDL_RenderPresent(renderer);
            }
        }

        private static void SetPixel(int x, int y, (byte r, byte g, byte b, byte alpha) color) {
            FrameBuffer[
                x +
                (y * 256)
            ] = (uint)((color.r << 16) | (color.g << 168 | (color.b << 0)));
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

                var tile = ppu.ChrRom[(bank + tileAddr * 16)..(bank + tileAddr * 16 + 16)];

                for (var y = 0; y <= 7; y++) {
                    var pixelY = tile_y * 8 + y;
                    var upper = tile[y];
                    var lower = tile[y + 8];

                    if (pixelY != scanline) {
                        continue;
                    }

                    for (var x = 0; x <= 7; x++) {
                        var pixelX = tile_x * 8 + x;

                        var value = (1 & lower) << 1 | (1 & upper);
                        upper = (byte)(upper >> 1);
                        lower = (byte)(lower >> 1);
                        (byte r, byte g, byte b, byte alpha) color;
                        switch (value) {
                            case 0:
                                color = Palette.SystemPalette[0x01];
                                break;
                            case 1:
                                color = Palette.SystemPalette[0x23];
                                break;
                            case 2:
                                color = Palette.SystemPalette[0x27];
                                break;
                            case 3:
                                color = Palette.SystemPalette[0x30];
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
