using BizHawk.NES;
using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static SDL2.SDL;

namespace NesEmu {
    internal class NesCore : IDisposable {
        public readonly PPU.PPU PPU;
        public readonly Bus.Bus Bus;
        public readonly APU APU;

        NesCpu CPU;
        readonly Rom.Rom Rom;

        int SamplesPerFrame = (44100 / 60) + 1;
        string UsedDevice;
        readonly SDL_AudioSpec SdlSpec;
        readonly int AudioDevice;

        public NesCore(Rom.Rom rom) {
            CPU = new NesCpu();
            PPU = new PPU.PPU(rom.ChrRom);
            APU = new APU(CPU, null, false);
            Bus = new Bus.Bus(CPU, PPU, APU, rom);
            Rom = rom;

            CPU.RegisterBus(Bus);
            PPU.RegisterBus(Bus);

            UsedDevice = "";
            if (File.Exists("audio_device.conf")) {
                UsedDevice = File.ReadAllText("audio_device.conf");
            }

            if (string.IsNullOrEmpty(UsedDevice)) {
                //Console.CursorTop++;
                Console.Clear();

                var devices = new List<string>();
                int count = SDL_GetNumAudioDevices(0);
                for (var i = 0; i < count; ++i) {
                    devices.Add(SDL_GetAudioDeviceName(i, 0));
                }

                Console.WriteLine($"Select your audio device: ");

                var marked = 0;
                int selected = -1;
                int initialRow = Console.CursorTop;
                while (selected < 0) {
                    Console.CursorTop = initialRow;
                    var index = 0;
                    foreach (string dev in devices) {
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
                    switch (nextKey.Key) {
                        case ConsoleKey.DownArrow when marked == devices.Count - 1:
                            continue;
                        case ConsoleKey.DownArrow:
                            marked++;
                            break;
                        case ConsoleKey.UpArrow when marked == 0:
                            continue;
                        case ConsoleKey.UpArrow:
                            marked--;
                            break;
                        case ConsoleKey.Enter:
                            selected = marked;
                            break;
                        default:
                            break;
                    }
                }
                UsedDevice = devices[selected];
                File.WriteAllText("audio_device.conf", UsedDevice);
            }

            SdlSpec.channels = 1;
            SdlSpec.freq = 44100;
            SdlSpec.samples = 4096;
            SdlSpec.format = AUDIO_S16LSB;
            
            AudioDevice = (int)SDL_OpenAudioDevice(UsedDevice, 0, ref SdlSpec, out var received, 0);
            if (AudioDevice == 0) {
                Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
                return;
            }
            
            Console.WriteLine($"Audio Device Initialized: {UsedDevice}");
            SDL_PauseAudioDevice((uint)AudioDevice, 0);
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
            uint count = APU.sampleclock;
            Bus.Blip.EndFrame(count);
            APU.sampleclock = 0;

            var samples = new short[SamplesPerFrame];
            int numAvailable = Bus.Blip.SamplesAvailable();
            int samplesSelected = Math.Min(numAvailable, SamplesPerFrame);
            Bus.Blip.ReadSamples(samples, samplesSelected, false);

            if (numAvailable != SamplesPerFrame) {
                samples = Resample(samples, samplesSelected, SamplesPerFrame);
            }

            uint numQueued = SDL_GetQueuedAudioSize((uint)AudioDevice);
            if (numQueued < 4096*3) {
                unsafe {
                    fixed (short* ptr = samples) {
                        var intPtr = new nint(ptr);
                        _ = SDL_QueueAudio((uint)AudioDevice, intPtr, (uint)(numAvailable * 2));
                    }
                }
            }

        }

        public void Dispose() {
            SDL_CloseAudioDevice((uint)AudioDevice);
            Bus.Mapper.Persist();
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

        // Taken from BizHawk, their license should be in my license file - Tromo
        // This uses simple linear interpolation which is supposedly not a great idea for
        // resampling audio, but it sounds surprisingly good to me. Maybe it works well
        // because we are typically stretching by very small amounts.
        private static short[] Resample(short[] input, int inputCount, int outputCount) {
            if (inputCount == outputCount) {
                return input;
            }

            const int channels = 1;
            var output = new short[outputCount * channels];

            if (inputCount == 0 || outputCount == 0) {
                Array.Clear(output, 0, outputCount * channels);
                return output;
            }

            for (var iOutput = 0; iOutput < outputCount; iOutput++) {
                double iInput = ((double)iOutput / (outputCount - 1)) * (inputCount - 1);
                var iInput0 = (int)iInput;
                int iInput1 = iInput0 + 1;
                double input0Weight = iInput1 - iInput;
                double input1Weight = iInput - iInput0;

                if (iInput1 == inputCount)
                    iInput1 = inputCount - 1;

                for (var iChannel = 0; iChannel < channels; iChannel++) {
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
