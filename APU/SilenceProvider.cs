using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.APU {
    internal class SilenceProvider : WaveProvider32, ISampleProvider  {
        WaveFormat waveFormat;

        public SilenceProvider(WaveFormat wf) {
            waveFormat = wf;
        }

        public WaveFormat WaveFormat => waveFormat;

        uint sampleNo;

        public override int Read(float[] buffer, int offset, int sampleCount) {
            for (int n = 0; n < sampleCount; n++, sampleNo++) {
                buffer[n + offset] = 0;
            }
            return sampleCount;
        }
    }
}
