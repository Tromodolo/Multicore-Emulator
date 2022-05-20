using BizHawk.Emulation.Common;
using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static SDL2.SDL;

namespace NesEmu {
    internal class NesCore {
        public Bus.Bus Bus;
        public NesCpu CPU;
        public PPU.PPU PPU;
        public BizHawk.NES.APU APU;

        IntPtr audioBuffer = Marshal.AllocHGlobal(16384);
        SDL_AudioSpec sdlSpec;
        int _audioDevice;

        public NesCore(Rom.Rom rom) {
            PPU = new PPU.PPU(rom.ChrRom, rom.Mirroring);
            CPU = new NesCpu();
            APU = new BizHawk.NES.APU(CPU, null, false);
            Bus = new Bus.Bus(CPU, PPU, APU, rom);

            CPU.RegisterBus(Bus);

            int count = SDL_GetNumAudioDevices(0);
            for (int i = 0; i < count; ++i) {
                Console.WriteLine($"Device {i} {SDL_GetAudioDeviceName(i, 0)}");
            }

            sdlSpec.channels = 2;
            sdlSpec.freq = 44100;
            sdlSpec.samples = 32;
            sdlSpec.format = AUDIO_S16LSB;
            var defaultDevice = SDL_GetAudioDeviceName(2, 0);
            _audioDevice = (int)SDL_OpenAudioDevice(defaultDevice, 0, ref sdlSpec, out SDL_AudioSpec received, 0);

            if (_audioDevice == 0) {
                Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
            } else {
                Console.WriteLine($"Audio Device Initialized: {defaultDevice}");
            }

            SDL_PauseAudioDevice((uint)_audioDevice, 0);
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
        }

        public void PlayFrameSamples() {
            var count = APU.sampleclock;
            Bus.blip.EndFrame(count);
            APU.sampleclock = 0;

            var numAvailable = Bus.blip.SamplesAvailable();
            var samples = new short[numAvailable * 2];
            Bus.blip.ReadSamples(samples, numAvailable, true);

            for (int i = numAvailable - 1; i >= 0; i--) {
                samples[i * 2] = samples[i];
                samples[i * 2 + 1] = samples[i];
            }

            int bytes = sizeof(short) * samples.Length;
            Marshal.Copy(samples, 0, audioBuffer, samples.Length);
            SDL_QueueAudio((uint)_audioDevice, audioBuffer, (uint)bytes);
        }
    }
}
