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
        static ulong CurrentFrame = 0;
        static bool IsFrameCap = true;
        static nint Texture;
        static nint Window;

        static NesCore GameCore;
        static bool IsRunning;

        static bool IsShiftPressed;
        static bool IsSaveStateHappening;

        private static void Main(string[] args) {
            while (true) {
#if DEBUG
                //DebugEntrypoint();
#endif

                var picker = new ConsoleFilePicker(new[] {
                    ".nes", ".nez"
                }, Directory.GetCurrentDirectory());
                string fileName = picker.SelectFile();

                byte[] romByteArr;
                try {
                    romByteArr = File.ReadAllBytes(fileName);
                } catch (Exception e) {
                    throw new("Couldn't find file, try again");
                }

                SDL_SetHint(SDL_HINT_GAMECONTROLLER_USE_BUTTON_LABELS, "0");
                SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
                if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO | SDL_INIT_GAMECONTROLLER) < 0) {
                    Console.WriteLine($"There was an issue initializing  {SDL_GetError()}");
                    return;
                }

                // Create a new window given a title, size, and passes it a flag indicating it should be shown.
                Window = SDL_CreateWindow("Nes", SDL_WINDOWPOS_UNDEFINED, SDL_WINDOWPOS_UNDEFINED, 256 * 3, 240 * 3, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_SHOWN);

                if (Window == nint.Zero) {
                    Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
                    return;
                }

                // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
                nint renderer = SDL_CreateRenderer(Window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

                if (renderer == nint.Zero) {
                    Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
                    return;
                }

                var rom = new Rom.Rom(romByteArr, fileName);
                GameCore = new NesCore(rom);

                IsRunning = true;
                CurrentFrame = 0;
                var sw = new Stopwatch();
                var frameSync = new Stopwatch();
                var windowTitle = $"Playing ${fileName} - FPS: 0";

                Texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 256, 240);
                nint activeController;
                for (var i = 0; i < SDL_NumJoysticks(); i++) {
                    if (SDL_IsGameController(i) != SDL_bool.SDL_TRUE) {
                        continue;
                    }
                    activeController = SDL_GameControllerOpen(i);
                    break;
                }

                sw.Start();
                frameSync.Start();

                var coreThread = new Thread(() => {
                    while (IsRunning) {
                        var clock = 0;
                        do {
                            if (IsSaveStateHappening)
                                continue;
                            GameCore.Clock();
                            clock++;
                        } while (!GameCore.Bus.PollDrawFrame());

                        if (!GameCore.Bus.GetDrawFrame())
                            continue;
                        CurrentFrame++;
                        GameCore.PPU.DrawFrame(ref renderer, ref Texture);

                        if (IsFrameCap) {
                            while (frameSync.ElapsedTicks < 16.66666666 * 10000) {
                                continue;
                            }
                        }

                        if (CurrentFrame % 60 == 0 && CurrentFrame != 0) {
                            sw.Stop();
                            CurrentFrame = 0;
                            decimal framerate = 60m / ((decimal)sw.ElapsedMilliseconds / 1000);
                            SDL_SetWindowTitle(Window, $"Playing {fileName} - FPS: {Math.Round(framerate, 2)} {GameCore.APU.sampleclock}");
                            sw.Restart();
                        }
                        GameCore.PlayFrameSamples();

                        frameSync.Restart();
                    }
                });
                coreThread.Start();

                // Main loop for the program
                while (IsRunning) {
                    while (SDL_PollEvent(out var e) == 1) {
                        var key = e.key;
                        switch (e.type) {
                            case SDL_EventType.SDL_KEYDOWN:
                                HandleKeyDown(GameCore, key);
                                break;
                            case SDL_EventType.SDL_KEYUP:
                                HandleKeyUp(GameCore, key);
                                break;
                            case SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
                                var down = (SDL_GameControllerButton)e.cbutton.button;
                                HandleButtonDown(GameCore, down);
                                break;
                            case SDL_EventType.SDL_CONTROLLERBUTTONUP:
                                var up = (SDL_GameControllerButton)e.cbutton.button;
                                HandleButtonUp(GameCore, up);
                                break;
                            case SDL_EventType.SDL_QUIT:
                                IsRunning = false;
                                break;
                            default:
                                break;
                        }
                    }
                }

                // Clean up the resources that were created.
                SDL_DestroyRenderer(renderer);
                SDL_DestroyWindow(Window);
                SDL_Quit();

                GameCore.Dispose();
            }
        }

        private static void HandleSaveState(int slot) {
            if (IsShiftPressed) {
                IsSaveStateHappening = true;
                GameCore.SaveState(slot);
                IsSaveStateHappening = false;
            } else {
                IsSaveStateHappening = true;
                GameCore.LoadState(slot);
                IsSaveStateHappening = false;
            }
        }

        private static void HandleKeyDown(NesCore core, SDL_KeyboardEvent key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL_Keycode.SDLK_TAB:
                    IsFrameCap = false;
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
                    IsShiftPressed = true;
                    break;
                default:
                    break;
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleKeyUp(NesCore core, SDL_KeyboardEvent key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL_Keycode.SDLK_TAB:
                    IsFrameCap = true;
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
                    IsRunning = false;
                    break;
                case SDL_Keycode.SDLK_LSHIFT:
                    IsShiftPressed = false;
                    break;
                default:
                    break;
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleButtonDown(NesCore core, SDL_GameControllerButton key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    IsFrameCap = false;
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
                default:
                    break;
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleButtonUp(NesCore core, SDL_GameControllerButton key) {
            byte currentKeys = core.Bus.Controller1.GetAllButtons();

            switch (key) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    IsFrameCap = true;
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
                default:
                    break;
            }

            core.Bus.Controller1.Update(currentKeys);
        }

        private static void DebugEntrypoint() {
            // var ms = new MemoryStream();
            // var writer = new BinaryWriter(ms);
            //
            // writer.Write((byte)0x4e);       // INES Tag
            // writer.Write((byte)0x45);
            // writer.Write((byte)0x53);
            // writer.Write((byte)0x1a);
            //
            // writer.Write((byte)0x10);       // Prg Rom LSB
            // writer.Write((byte)0);          // Chr Rom LSB
            //
            // writer.Write((byte)0b01000001); // Control Byte 1
            // writer.Write((byte)0b00001100); // Control Byte 2
            //
            // writer.Write((byte)0);          // Submapper
            // writer.Write((byte)0);          // xxxxyyyy  X = Chr Rom MSB Y = Prg Rom MSB
            // writer.Write((byte)0);          // Volatile shifts RAM
            // writer.Write((byte)0);          // Volatile shifts Chr RAM
            //
            // writer.Write((byte)0);          // Region Flag NTSC
            //
            // writer.Write((byte)0);          // Padding
            // writer.Write((byte)0);          // Padding
            // writer.Write((byte)0);          // Padding
            //
            // // This is where PRG ROM starts
            // const int size = 0x10 * 0x4000;
            // for (var i = 0; i < size; i++) {
            //     writer.Write((byte)i); 
            // }
            //
            // var debugRom = new Rom.Rom(ms.ToArray(), "Test.nes");
            // const ushort cpuSize = 0x2000;
            // byte bank1 = 0;
            // byte bank2 = 1;
            // byte lastBank = (byte)((debugRom.PrgRom.Length - cpuSize) / cpuSize);
            //
            // var testBanks = (bool mode) => {
            //     if (mode) {
            //         debugRom.Mapper.CpuRead(0x8000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == (lastBank - 1) * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //
            //         debugRom.Mapper.CpuRead(0xA000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == bank2 * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //
            //         debugRom.Mapper.CpuRead(0xC000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == bank1 * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //
            //         debugRom.Mapper.CpuRead(0xE000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == lastBank * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //     } else {
            //         debugRom.Mapper.CpuRead(0x8000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == bank1 * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //
            //         debugRom.Mapper.CpuRead(0xA000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == bank2 * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //
            //         debugRom.Mapper.CpuRead(0xC000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == (lastBank - 1) * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //
            //         debugRom.Mapper.CpuRead(0xE000);
            //         Debug.Assert(debugRom.Mapper.MappedAddress() == lastBank * cpuSize);
            //         Debug.Assert(debugRom.Mapper.DidMap());
            //     }
            // };
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b00000110);
            // debugRom.Mapper.CpuWrite(0x8001, bank1);
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b00000111);
            // debugRom.Mapper.CpuWrite(0x8001, bank2);
            //
            // testBanks(false);
            //
            // bank1 = 14;
            // bank2 = 12;
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b00000110);
            // debugRom.Mapper.CpuWrite(0x8001, bank1);
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b00000111);
            // debugRom.Mapper.CpuWrite(0x8001, bank2);
            //
            // testBanks(false);
            //
            // bank1 = 7;
            // bank2 = 24;
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b00000110);
            // debugRom.Mapper.CpuWrite(0x8001, bank1);
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b00000111);
            // debugRom.Mapper.CpuWrite(0x8001, bank2);
            //
            // testBanks(false);
            //
            // bank1 = 5;
            // bank2 = 6;
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b01000110);
            // debugRom.Mapper.CpuWrite(0x8001, bank1);
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b01000111);
            // debugRom.Mapper.CpuWrite(0x8001, bank2);
            //
            // testBanks(true);
            //
            // bank1 = 9;
            // bank2 = 8;
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b01000110);
            // debugRom.Mapper.CpuWrite(0x8001, bank1);
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b01000111);
            // debugRom.Mapper.CpuWrite(0x8001, bank2);
            //
            // testBanks(true);
            //
            // bank1 = 1;
            // bank2 = 13;
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b01000110);
            // debugRom.Mapper.CpuWrite(0x8001, bank1);
            //
            // debugRom.Mapper.CpuWrite(0x8000, 0b01000111);
            // debugRom.Mapper.CpuWrite(0x8001, bank2);
            //
            // testBanks(true);
            //
            // // If we made it this far, bank switching is assumed to be correct
            //
            // debugRom.Mapper.CpuWrite(0xE001, 0);
            // debugRom.Mapper.CpuWrite(0xC000, 10);
            // debugRom.Mapper.CpuWrite(0xC001, 0);
            //
            // debugRom.Mapper.DecrementScanline();
            //
            // for (var i = 0; i < 10; i++) {
            //     debugRom.Mapper.DecrementScanline();
            // }
            //
            // Debug.Assert(debugRom.Mapper.GetIRQ());
            // debugRom.Mapper.CpuWrite(0xE000, 0);
            // Debug.Assert(!debugRom.Mapper.GetIRQ());
            //
            // debugRom.Mapper.DecrementScanline();
            //
            // return;
        }
    }
}
