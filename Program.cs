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
        static string fileName;
        static NesCpu cpu;
        static UInt64 currentFrame = 0;

        static IntPtr Texture;

        static void Main(string[] args){
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
            currentFrame = 0;
            Stopwatch sw = new Stopwatch();
            Stopwatch frameSync = new Stopwatch();
            string windowTitle = $"Playing ${fileName} - FPS: 0";

            Texture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGB888, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 256, 240);

            sw.Start();
            frameSync.Start();
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

                do {
                    cpu.ExecuteInstruction();
                } while (!cpu.Bus.PollDrawFrame());

                if (cpu.Bus.GetDrawFrame()) {
                    currentFrame++;
                    cpu.Bus.PPU.DrawFrame(ref renderer, ref Texture);

                    //while (frameSync.ElapsedTicks < 16.66666666 * 10000) {
                    //    continue;
                    //}

                    if (currentFrame % 60 == 0 && currentFrame != 0) {
                        sw.Stop();
                        currentFrame = 0;
                        var framerate = 60m / ((decimal)sw.ElapsedMilliseconds / 1000);
                        windowTitle = $"Playing {fileName} - FPS: {Math.Round(framerate, 2)}";
                        SDL.SDL_SetWindowTitle(window, windowTitle);
                        sw.Restart();
                    }

                    frameSync.Restart();
                }
            }

            // Clean up the resources that were created.
            SDL.SDL_DestroyRenderer(renderer);
            SDL.SDL_DestroyWindow(window);
            SDL.SDL_Quit();
        }

        private static void HandleKeyDown(NesCpu cpu, SDL.SDL_KeyboardEvent key) {
            byte currentKeys = cpu.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL.SDL_Keycode.SDLK_r:
                    cpu.Reset();
                    break;
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
    }
}
