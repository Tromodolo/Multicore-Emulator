using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.APU {
    internal class NesAudioMixer: WaveProvider32 {
        public WaveFormat WaveFormat => waveFormat;
        public PulseWave pulse1;
        public PulseWave pulse2;
        List<float> pulse1Buffer;
        List<float> pulse2Buffer;
        WaveFormat waveFormat;

        public NesAudioMixer() {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

            pulse1 = new PulseWave(0, OnPulse1AudioOut) {
                Frequency = 300,
                Volume = 0.02f,
            };
            pulse2 = new PulseWave(0, OnPulse2AudioOut) {
                Frequency = 300,
                Volume = 0.02f,
            };
            //triangle = new SignalGenerator() {
            //    Gain = 0.05,
            //    Frequency = 250,
            //    Type = SignalGeneratorType.Triangle,
            //};

            //triangle2 = new SignalGenerator() {
            //    Gain = 0.05,
            //    Frequency = 250,
            //    Type = SignalGeneratorType.Triangle,
            //};
            //triangle3 = new SignalGenerator() {
            //    Gain = 0.05,
            //    Frequency = 250,
            //    Type = SignalGeneratorType.Triangle,
            //};

            pulse1Buffer = new List<float>();
            pulse2Buffer = new List<float>();

            pulse1.SetMuted(false);
            pulse2.SetMuted(false);
        }

        private void OnPulse1AudioOut(float audio, uint sampleNo) {
            if (sampleNo >= waveFormat.SampleRate) {
                return;
            }
            pulse1Buffer.Add(audio);
        }

        private void OnPulse2AudioOut(float audio, uint sampleNo) {
            if (sampleNo >= waveFormat.SampleRate) {
                return;
            }
            pulse2Buffer.Add(audio);
        }


        //private void PushAudio() {
        //    var lowest = Math.Min(pulse1Buffer.Count, pulse2Buffer.Count);

        //    for (int n = 0; n < lowest; n++) {
        //        var pulse1OutFloat = pulse1Buffer[n];
        //        var pulse2OutFloat = pulse2Buffer[n];
        //        var pulseOutFloat = 0.0752f * (pulse1OutFloat + pulse2OutFloat);
        //        //0.0752 * 
        //        var outsample = (short)(pulseOutFloat * short.MaxValue);
        //        byte pulseOutByteLo = (byte)(outsample & 0xff);
        //        byte pulseOutByteHi = (byte)((outsample >> 8) & 0xff);

        //        //var outsample = (short)(samples[sampleIndex] * short.MaxValue);
        //        //pcm[pcmIndex] = (byte)(outsample & 0xff);
        //        //pcm[pcmIndex + 1] = (byte)((outsample >> 8) & 0xff);

        //        var combinedNoiseLo = pulseOutByteLo;
        //        var combinedNoiseHi = pulseOutByteHi;
        //        bwpBuffer.Add(combinedNoiseLo);
        //        bwpBuffer.Add(combinedNoiseHi);

        //    }
        //    if (bwpBuffer.Sum(x => x) != 0) {
        //        var arr = bwpBuffer.ToArray().Clone();
        //        bwp.AddSamples(arr as byte[], 0, lowest);
        //    }
        //    pulse1Buffer.RemoveRange(0, lowest);
        //    pulse2Buffer.RemoveRange(0, lowest);
        //    bwpBuffer.Clear();
        //}

        public override int Read(float[] buffer, int offset, int sampleCount) {
            int samplesTaken = 0;
            int sampleRate = WaveFormat.SampleRate;
            for (int n = 0; n < sampleCount; n++, samplesTaken++) {
                if (n >= pulse1Buffer.Count || n >= pulse2Buffer.Count) {
                    break;
                }
                var pulse1Out = pulse1Buffer[n] * 15;
                var pulse2Out = pulse2Buffer[n] * 15;
                //var pulse1Out = pulse1.GetNextSound(count) * 15;
                //var pulse2Out = pulse2.GetNextSound(count) * 15;
                var pulseOut = 0.0752f * (pulse1Out + pulse2Out);

                var combinedNoise = pulseOut;
                buffer[n + offset] = combinedNoise;
            }
            //if (pulse1Empty) {
            // This needs to get fixed eventually or it will keep cutting off sounds
                pulse1Buffer.Clear();
            //}
            //if(pulse2Empty) {
                pulse2Buffer.Clear();
            //}
            return sampleCount;
        }
    }
}
