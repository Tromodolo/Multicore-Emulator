using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.APU {
    internal class TriangleWaveProvider : WaveProvider32, ISampleProvider  {
        WaveFormat waveFormat;

        public TriangleWaveProvider(WaveFormat waveFormat) {
            Frequency = 1;
            Amplitude = 0.05f; // let's not hurt our ears
            sampleNo = 0;
            this.waveFormat = waveFormat;
        }

        public float Frequency { get; set; }
        public float Amplitude { get; set; }

        public WaveFormat WaveFormat => waveFormat;

        uint sampleNo;
        bool playing;

        public void SetPlaying(bool playing) {
            this.playing = playing;
        }


        public override int Read(float[] buffer, int offset, int sampleCount) {
            int sampleRate = WaveFormat.SampleRate;
            for (int n = 0; n < sampleCount; n++, sampleNo++) {
                if (playing) {
                    buffer[n + offset] = 0;
                    continue;
                }

                //buffer[n + offset] = generator.Read(buffer, 0, 1);

                if (n >= sampleRate) sampleNo = 0;
            }
            return sampleCount;
        }
    }
}
