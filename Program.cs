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
using System.Collections.Generic;

namespace NesEmu {
    class Program {
        static string fileName;
        static ulong currentFrame = 0;
        static IntPtr Texture;
        static bool frameCap = true;
        static IntPtr window;

        static NesCore core;
        static bool running;

        static bool isShiftPressed;
        static bool isSaveStateHappening;

        static void Main(string[] args){
            Console.Clear();

            Console.WriteLine("Select which rom to run from current folder:");
            var dirFiles = Directory.GetFiles(Directory.GetCurrentDirectory());
            List<string> roms = new List<string>();
            foreach (var file in dirFiles) {
                if (file.EndsWith(".nes") || file.EndsWith(".nez")) {
                    roms.Add(file);
                }
            }

            int selected = -1;
            int marked = 0;
            int initialRow = Console.CursorTop;
            while (selected < 0) {
                Console.CursorTop = initialRow;
                var index = 0;
                foreach (var romfile in roms) {
                    if (index == marked) {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write($"> {romfile}\n");
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.ForegroundColor = ConsoleColor.White;
                    } else {
                        Console.Write($"  {romfile}\n");
                    }
                    index++;
                }

                var nextKey = Console.ReadKey();
                if (nextKey.Key == ConsoleKey.DownArrow) {
                    if (marked == roms.Count - 1) {
                        continue;
                    }
                    marked++;
                } else if (nextKey.Key == ConsoleKey.UpArrow) {
                    if (marked == 0) {
                        continue;
                    }
                    marked--;
                } else if (nextKey.Key == ConsoleKey.Enter) {
                    selected = marked;
                }
            }
            fileName = roms[selected];

            byte[] romByteArr;
            try {
                romByteArr = File.ReadAllBytes(fileName);
            } catch (Exception e) {
                throw new("Couldn't find file, try again");
            }

            SDL_SetHint(SDL_HINT_GAMECONTROLLER_USE_BUTTON_LABELS, "0");
            SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
            // Initilizes 
            if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO | SDL_INIT_GAMECONTROLLER) < 0) {
                Console.WriteLine($"There was an issue initilizing  {SDL_GetError()}");
                return;
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
                return;
            }

            // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
            var renderer = SDL_CreateRenderer(window,
                                                  -1,
                                                  SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            if (renderer == IntPtr.Zero) {
                Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
                return;
            }

            Rom.Rom rom = new(romByteArr, fileName);
            core = new NesCore(rom);

            running = true;
            currentFrame = 0;
            Stopwatch sw = new();
            Stopwatch frameSync = new();
            string windowTitle = $"Playing ${fileName} - FPS: 0";

            Texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 256, 240);
            IntPtr activeController;
            for (int i = 0; i < SDL_NumJoysticks(); i++) {
                if (SDL_IsGameController(i) != SDL_bool.SDL_TRUE) {
                    continue;
                }
                activeController = SDL_GameControllerOpen(i);
                break;
            }

            sw.Start();
            frameSync.Start();

            var coreThread = new Thread(() => {
                while(running) {
                    var clock = 0;
                    do {
                        if (!isSaveStateHappening) {
                            core.Clock();
                            clock++;
                        }
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
            });
            coreThread.Start();

            // Main loop for the program
            while (running) {
                while (SDL_PollEvent(out SDL_Event e) == 1) {
                    var key = e.key;
                    switch (e.type) {
                        case SDL_EventType.SDL_KEYDOWN:
                            HandleKeyDown(core, key);
                            break;
                        case SDL_EventType.SDL_KEYUP:
                            HandleKeyUp(core, key);
                            break;
                        case SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
                            SDL_GameControllerButton down = (SDL_GameControllerButton)e.cbutton.button;
                            HandleButtonDown(core, down);
                            break;
                        case SDL_EventType.SDL_CONTROLLERBUTTONUP:
                            SDL_GameControllerButton up = (SDL_GameControllerButton)e.cbutton.button;
                            HandleButtonUp(core, up);
                            break;
                        case SDL_EventType.SDL_QUIT:
                            running = false;
                            break;
                    }
                }
            }

            // Clean up the resources that were created.
            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            SDL_Quit();

            core.Dispose();
            Main(args);
        }

        private static void HandleSaveState(int slot) {
            if (isShiftPressed) {
                isSaveStateHappening = true;
                core.SaveState(slot);
                isSaveStateHappening = false;
            } else {
                isSaveStateHappening = true;
                core.LoadState(slot);
                isSaveStateHappening = false;
            }
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
                case SDL_Keycode.SDLK_F1:
                    HandleSaveState(1);
                    break;
                case SDL_Keycode.SDLK_F2:
                    HandleSaveState(2);
                    break;
                case SDL_Keycode.SDLK_F3:
                    HandleSaveState(3);
                    break;
                case SDL_Keycode.SDLK_F4:
                    HandleSaveState(4);
                    break;
                case SDL_Keycode.SDLK_F5:
                    HandleSaveState(5);
                    break;
                case SDL_Keycode.SDLK_F6:
                    HandleSaveState(6);
                    break;
                case SDL_Keycode.SDLK_F7:
                    HandleSaveState(7);
                    break;
                case SDL_Keycode.SDLK_F8:
                    HandleSaveState(8);
                    break;
                case SDL_Keycode.SDLK_LSHIFT:
                    isShiftPressed = true;
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
                case SDL_Keycode.SDLK_ESCAPE:
                    running = false;
                    break;
                case SDL_Keycode.SDLK_LSHIFT:
                    isShiftPressed = false;
                    break;
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleButtonDown(NesCore core, SDL_GameControllerButton key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    frameCap = false;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE:
                    core.Reset();
                    break;
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
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleButtonUp(NesCore core, SDL_GameControllerButton key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    frameCap = true;
                    break;
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
            }

            core.Bus.Controller1.Update(currentKeys);
        }
    }
}
