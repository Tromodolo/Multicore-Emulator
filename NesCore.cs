using BizHawk.Emulation.Common;
using NAudio.Wave;
using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static SDL2.SDL;

namespace NesEmu {
    internal class BufferProvider : WaveProvider16 {
        public Queue<short> internalBuffer = new Queue<short>();

        public void Clear() {
            internalBuffer.Clear();
        }

        public void TryQueue(short[] samples) {
            for (var i = 0; i < samples.Length; i++) {
                internalBuffer.Enqueue(samples[i]);
            }
        }
        
        public override int Read(short[] buffer, int offset, int sampleCount) {
            Queue<short> outBuff = new Queue<short>();
            var samplesTaken = Math.Min(sampleCount, internalBuffer.Count);
            for (var i = 0; i < samplesTaken; i++) {
                buffer[i] = internalBuffer.Dequeue();
            }
            return samplesTaken;
        }
    }

    internal class NesCore : IDisposable {
        public Bus.Bus Bus;
        public NesCpu CPU;
        public PPU.PPU PPU;
        public BizHawk.NES.APU APU;
        public Rom.Rom Rom;

        int SamplesPerFrame = (44100 / 60) + 1;

        DirectSoundOut dso;
        BufferProvider bp;

        public NesCore(Rom.Rom rom) {
            PPU = new(rom.ChrRom);
            CPU = new();
            APU = new(CPU, null, false);
            Bus = new(CPU, PPU, APU, rom);
            Rom = rom;

            CPU.RegisterBus(Bus);
            PPU.RegisterBus(Bus);

            dso = new DirectSoundOut(60);
            bp = new BufferProvider();
            dso.Init(bp);
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
            bp.Clear();
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

            if (bp.internalBuffer.Count == 0) {
                Console.WriteLine("Audio has drifted and buffer is empty");
            }
            if (bp.internalBuffer.Count <= SamplesPerFrame * 10) {
                bp.TryQueue(samples);
            }
            if (dso.PlaybackState == PlaybackState.Stopped) {
                dso.Play();
            }
        }

        public void Dispose() {
            bp.Clear();
            dso.Dispose();
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
