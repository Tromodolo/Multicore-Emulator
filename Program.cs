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
using System.Runtime.CompilerServices;
using BizHawk.Emulation.Common;
using static SDL2.SDL;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;

namespace NesEmu {
    class Program {
        static string fileName;
        static UInt64 currentFrame = 0;
        static IntPtr Texture;
        static bool frameCap = true;
        static IntPtr window;

        static NesCore core;

        static void Main(string[] args){
            // Initilizes 
            if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO) < 0) {
                Console.WriteLine($"There was an issue initilizing  {SDL_GetError()}");
            }

            // Create a new window given a title, size, and passes it a flag indicating it should be shown.
            window = SDL_CreateWindow("Nes",
                                              SDL_WINDOWPOS_UNDEFINED,
                                              SDL_WINDOWPOS_UNDEFINED,
                                              256 * 3,
                                              240 * 3,
                                              SDL_WindowFlags.SDL_WINDOW_RESIZABLE | 
                                              SDL_WindowFlags.SDL_WINDOW_SHOWN);

            if (window == IntPtr.Zero) {
                Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
            }

            // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
            var renderer = SDL_CreateRenderer(window,
                                                  -1,
                                                  SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            if (renderer == IntPtr.Zero) {
                Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
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
            core = new NesCore(rom);

            var running = true;
            currentFrame = 0;
            Stopwatch sw = new Stopwatch();
            Stopwatch frameSync = new Stopwatch();
            string windowTitle = $"Playing ${fileName} - FPS: 0";

            Texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 256, 240);

            sw.Start();
            frameSync.Start();
            // Main loop for the program
            while (running) {
                // Check to see if there are any events and continue to do so until the queue is empty.
                while (SDL_PollEvent(out SDL_Event e) == 1) {
                    var key = e.key;
                    switch (e.type) {
                        case SDL_EventType.SDL_KEYDOWN:
                            HandleKeyDown(core, key);
                            break;
                        case SDL_EventType.SDL_KEYUP:
                            HandleKeyUp(core, key);
                            break;
                        case SDL_EventType.SDL_QUIT:
                            running = false;
                            break;
                    }
                }

                do {
                    core.Clock();
                } while (!core.Bus.PollDrawFrame());

                if (core.Bus.GetDrawFrame()) {
                    currentFrame++;
                    core.PPU.DrawFrame(ref renderer, ref Texture);

                    if (frameCap) {
                        while (frameSync.ElapsedTicks < 16.66666666 * 10000) {
                            continue;
                        }
                    }

                    if (currentFrame % 60 == 0 && currentFrame != 0) {
                        sw.Stop();
                        currentFrame = 0;
                        var framerate = 60m / ((decimal)sw.ElapsedMilliseconds / 1000);
                        SDL_SetWindowTitle(window, $"Playing {fileName} - FPS: {Math.Round(framerate, 2)} {core.APU.sampleclock}");
                        sw.Restart();
                    }
                    core.PlayFrameSamples();

                    frameSync.Restart();
                }
            }

            // Clean up the resources that were created.
            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            SDL_Quit();
        }

        private static void HandleKeyDown(NesCore core, SDL_KeyboardEvent key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL_Keycode.SDLK_TAB:
                    frameCap = false;
                    break;
                case SDL_Keycode.SDLK_r:
                    core.Reset();
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
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleKeyUp(NesCore core, SDL_KeyboardEvent key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL_Keycode.SDLK_TAB:
                    frameCap = true;
                    break;
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
            }

            core.Bus.Controller1.Update(currentKeys);
        }
    }
}
