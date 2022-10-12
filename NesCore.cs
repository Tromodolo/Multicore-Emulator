using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static SDL2.SDL;

namespace NesEmu {
    internal class NesCore : IDisposable {
        public Bus.Bus Bus;
        public NesCpu CPU;
        public PPU.PPU PPU;
        public BizHawk.NES.APU APU;
        public Rom.Rom Rom;

        int SamplesPerFrame = (44100 / 60) + 1;
        SDL_AudioSpec sdlSpec;
        int audioDevice;

        public NesCore(Rom.Rom rom) {
            PPU = new(rom.ChrRom);
            CPU = new();
            APU = new(CPU, null, false);
            Bus = new(CPU, PPU, APU, rom);
            Rom = rom;

            CPU.RegisterBus(Bus);
            PPU.RegisterBus(Bus);

            Console.CursorTop++;

            List<string> devices = new List<string>();
            int count = SDL_GetNumAudioDevices(0);
            for (int i = 0; i < count; ++i) {
                devices.Add(SDL_GetAudioDeviceName(i, 0));
            }

            Console.WriteLine($"Select your audio device: ");

            int selected = -1;
            int marked = 0;
            int initialRow = Console.CursorTop;
            while (selected < 0) {
                Console.CursorTop = initialRow;
                var index = 0;
                foreach (var dev in devices) {
                    if (index == marked) {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write($"> {dev}\n");
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.ForegroundColor = ConsoleColor.White;
                    } else {
                        Console.Write($"  {dev}\n");
                    }
                    index++;
                }

                var nextKey = Console.ReadKey();
                if (nextKey.Key == ConsoleKey.DownArrow) {
                    if (marked == devices.Count - 1) {
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
            var usedDevice = devices[selected];

            sdlSpec.channels = 1;
            sdlSpec.freq = 44100;
            sdlSpec.samples = 4096;
            sdlSpec.format = AUDIO_S16LSB;
            audioDevice = (int)SDL_OpenAudioDevice(usedDevice, 0, ref sdlSpec, out SDL_AudioSpec received, 0);

            if (audioDevice == 0) {
                Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
            } else {
                Console.WriteLine($"Audio Device Initialized: {usedDevice}");
            }

            SDL_PauseAudioDevice((uint)audioDevice, 0);
        }

        /// <summary>
        /// PPU has the fastest clock speed, so clock the ppu every clock here and then the others every so often
        /// </summary>
        public bool Clock() {
            return Bus.Clock();
        }

        public void Reset() {
            APU.NESHardReset();
            CPU.Reset();
            Bus.Reset();
        }

        public void PlayFrameSamples() {
            var count = APU.sampleclock;
            Bus.blip.EndFrame(count);
            APU.sampleclock = 0;

            var numAvailable = Bus.blip.SamplesAvailable();
            var samples = new short[SamplesPerFrame];
            var samplesSelected = Math.Min(numAvailable, SamplesPerFrame);
            Bus.blip.ReadSamples(samples, samplesSelected, false);

            if (numAvailable != SamplesPerFrame) {
                samples = Resample(samples, samplesSelected, SamplesPerFrame);
            }

            var numQueued = SDL_GetQueuedAudioSize((uint)audioDevice);
            if (numQueued < 4096*3) {
                unsafe {
                    fixed (short* ptr = samples) {
                        IntPtr intPtr = new(ptr);
                        SDL_QueueAudio((uint)audioDevice, intPtr, (uint)(numAvailable * 2));
                    }
                }
            }

        }

        public void Dispose() {
            SDL_CloseAudioDevice((uint)audioDevice);
            Bus.Mapper.Persist();
        }

        public void SaveState(int slot) {
            var gameName = Rom.Filename.Split('\\').LastOrDefault();
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
            var gameName = Rom.Filename.Split('\\').LastOrDefault();
            gameName = gameName.Replace(".nes", "");
            gameName = gameName.Replace(".nez", "");
            var stateFileName = $"{gameName}.{slot}.state";

            if (File.Exists(stateFileName)) {
                var fileStream = new FileStream(stateFileName, FileMode.Open);
                var binaryReader = new BinaryReader(fileStream);
                CPU.Load(binaryReader);
                PPU.Load(binaryReader);
                Bus.Load(binaryReader);
                fileStream.Close();
            }
        }

        // Taken from BizHawk, their license should be in my license file - Tromo
        // This uses simple linear interpolation which is supposedly not a great idea for
        // resampling audio, but it sounds surprisingly good to me. Maybe it works well
        // because we are typically stretching by very small amounts.
        private short[] Resample(short[] input, int inputCount, int outputCount) {
            if (inputCount == outputCount) {
                return input;
            }

            int channels = 1;

            short[] output = new short[outputCount * channels];

            if (inputCount == 0 || outputCount == 0) {
                Array.Clear(output, 0, outputCount * channels);
                return output;
            }

            for (int iOutput = 0; iOutput < outputCount; iOutput++) {
                double iInput = ((double)iOutput / (outputCount - 1)) * (inputCount - 1);
                int iInput0 = (int)iInput;
                int iInput1 = iInput0 + 1;
                double input0Weight = iInput1 - iInput;
                double input1Weight = iInput - iInput0;

                if (iInput1 == inputCount)
                    iInput1 = inputCount - 1;

                for (int iChannel = 0; iChannel < channels; iChannel++) {
                    double value =
                        input[iInput0 * channels + iChannel] * input0Weight +
                        input[iInput1 * channels + iChannel] * input1Weight;

                    output[iOutput * channels + iChannel] = (short)((int)(value + 32768.5) - 32768);
                }
            }

            return output;
        }
    }
}
