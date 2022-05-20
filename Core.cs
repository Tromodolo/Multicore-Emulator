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
    class Core {
        static string fileName;
        static NesCpu cpu;
        static UInt64 currentFrame = 0;
        static IntPtr Texture;
        static bool frameCap = true;
        static IntPtr window;

        static BlipBuffer blip = new BlipBuffer(4096);
        static int blipbuffsize = 4096;
        static int oldSample;

        static uint AUDIO_SAMPLE_FULL_THRESHOLD = 2048;
        static int SAMPLES_PER_CALLBACK = 32;  // 735 = 44100 samples / 60 fps // 367.5? 1470

        static IntPtr audioBuffer = Marshal.AllocHGlobal(16384);
        static SDL_AudioSpec sdlWant, sdlHave;
        static int _audioDevice;

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

            int count = SDL_GetNumAudioDevices(0);
            for (int i = 0; i < count; ++i) {
                Console.WriteLine($"Device {i} {SDL_GetAudioDeviceName(i, 0)}");
            }

            sdlWant.channels = 2;
            sdlWant.freq = 44100;
            sdlWant.samples = (ushort)SAMPLES_PER_CALLBACK;
            sdlWant.format = AUDIO_S16LSB;
            var defaultDevice = SDL_GetAudioDeviceName(2, 0);
            _audioDevice = (int)SDL_OpenAudioDevice(defaultDevice, 0, ref sdlWant, out sdlHave, 0); //(int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);

            if (_audioDevice == 0) {
                Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
            } else {
                Console.WriteLine($"Audio Device Initialized: {SDL_GetAudioDeviceName((int)_audioDevice, 0)}");
            }

            SDL_PauseAudioDevice((uint)_audioDevice, 0);

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

            blip.Clear();
            blip.SetRates(1789773 * 2, 44100);

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
                            HandleKeyDown(cpu, key);
                            break;
                        case SDL_EventType.SDL_KEYUP:
                            HandleKeyUp(cpu, key);
                            break;
                        case SDL_EventType.SDL_QUIT:
                            running = false;
                            break;
                    }
                }

                do {
                    Clock(cpu);
                } while (!cpu.Bus.PollDrawFrame());

                if (cpu.Bus.GetDrawFrame()) {
                    currentFrame++;
                    cpu.Bus.PPU.DrawFrame(ref renderer, ref Texture);

                    if (frameCap) {
                        while (frameSync.ElapsedTicks < 16.66666666 * 10000) {
                            continue;
                        }
                    }

                    if (currentFrame % 60 == 0 && currentFrame != 0) {
                        sw.Stop();
                        currentFrame = 0;
                        var framerate = 60m / ((decimal)sw.ElapsedMilliseconds / 1000);
                        SDL_SetWindowTitle(window, $"Playing {fileName} - FPS: {Math.Round(framerate, 2)} {cpu.Bus.APU.sampleclock}");
                        sw.Restart();
                    }
                    PlayFrameSamples();

                    frameSync.Restart();
                }
            }

            // Clean up the resources that were created.
            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            SDL_Quit();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Clock(NesCpu cpu) {
            cpu.Bus.APU.RunOneFirst();
            cpu.ExecuteInstruction();
            cpu.Bus.APU.RunOneLast();

            int sample = cpu.Bus.APU.EmitSample();
            if (sample != oldSample) {
                blip.AddDelta(cpu.Bus.APU.sampleclock, sample - oldSample);
                oldSample = sample;
            }

            cpu.Bus.APU.sampleclock++;
        }

        public static void PlayFrameSamples() {
            var count = cpu.Bus.APU.sampleclock;
            blip.EndFrame(count);
            cpu.Bus.APU.sampleclock = 0;
            var numAvailable = blip.SamplesAvailable();
            var samples = new short[numAvailable * 2];
            blip.ReadSamples(samples, numAvailable, true);

            for (int i = numAvailable - 1; i >= 0; i--) {
                samples[i * 2] = samples[i];
                samples[i * 2 + 1] = samples[i];
            }

            if ((SDL_GetQueuedAudioSize((uint)_audioDevice) / sizeof(short)) < AUDIO_SAMPLE_FULL_THRESHOLD) {
                int bytes = sizeof(short) * samples.Length;
                Marshal.Copy(samples, 0, audioBuffer, samples.Length);
                //SDL_SetWindowTitle(window, $"{count} samples {numAvailable} available");
                SDL_QueueAudio((uint)_audioDevice, audioBuffer, (uint)bytes);
            } else {
                var x = 5;
            }
        }

        private static void HandleKeyDown(NesCpu cpu, SDL_KeyboardEvent key) {
            byte currentKeys = cpu.Bus.Controller1.GetAllButtons();

            switch (key.keysym.sym) {
                case SDL_Keycode.SDLK_TAB:
                    frameCap = false;
                    break;
                case SDL_Keycode.SDLK_r:
                    cpu.Reset();
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

            cpu.Bus.Controller1.Update(currentKeys);
        }

        private static void HandleKeyUp(NesCpu cpu, SDL_KeyboardEvent key) {
            byte currentKeys = cpu.Bus.Controller1.GetAllButtons();

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

            cpu.Bus.Controller1.Update(currentKeys);
        }
    }
}
