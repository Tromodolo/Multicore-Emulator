using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NesEmu.APU {
    //WaveProvider32, ISampleProvider
    internal class PulseWave  {
        public PulseWave(int duty, Action<float, uint> OnSoundGenerate) {
            Frequency = -1;
            Volume = 0.05f; // let's not hurt our ears
            audioSampleNo = 0;
            timerPeriod = 0;
            SetDuty(duty);
            SetMuted(true);
            onSoundGenerate = OnSoundGenerate;
        }

        public float Frequency;
        public float Volume;
        public float OutputSound;
        Action<float, uint> onSoundGenerate;

        bool channelEnabled;

        // Envelope
        byte[] DutyCycle = new byte[8];
        uint audioSampleNo;
        bool isMuted;

        int timerPeriod;
        int timerPeriodReset;
        uint lengthCounter;

        // Volume
        bool constantSound;
        int envelopeVolume;
        bool envelopeHalt;
        int volumeReloadDivider;

        // Sweep unit
        bool sweepEnabled;
        int sweepAmount;
        int sweepDividerReset;
        int sweepDivider;
        bool sweepReload;
        int targetPeriod;

        public void ResetState() {
            SetMuted(true);
            audioSampleNo = 0;
            timerPeriod = 0;
            lengthCounter = 0;
            channelEnabled = true;
        }

        public void SetEnable(bool enable) {
            channelEnabled = enable;
            if (!channelEnabled) {
                lengthCounter = 0;
            }
        }

        public float GetNextSound() {
            int sampleRate = 44100;
            if (audioSampleNo >= sampleRate) {
                audioSampleNo = 0;
            }
            audioSampleNo++;

            var samplesPerSecond = (sampleRate / 8) / Frequency;
            if (isMuted || Frequency < 0 || !channelEnabled) {
                return 0;
            }

            int dutyCycleIndex = (int)(audioSampleNo / samplesPerSecond);
            return DutyCycle[dutyCycleIndex % 8] * Volume;
        }

        public float GetNextSound(int sampleCount) {
            int sampleRate = 44100;
            if (audioSampleNo >= sampleRate) {
                audioSampleNo = 0;
            }

            var samplesPerSecond = (sampleRate / 8) / Frequency;

            if (isMuted || Frequency < 0 || !channelEnabled) {
                return 0;
            }

            int dutyCycleIndex = (int)(sampleCount / samplesPerSecond);
            audioSampleNo++;
            return DutyCycle[dutyCycleIndex % 8] * Volume;
        }

        /// <summary>
        /// Envelopes and Linear Counter
        /// </summary>
        public void TickQuarter() {
            timerPeriod--;
            if (timerPeriod <= 0) {
                timerPeriod = timerPeriodReset;
                UpdateTargetPeriod();
            }

            if (!constantSound) {
                envelopeVolume--;
                Volume = 0.0002f * envelopeVolume;
                if (envelopeVolume <= 0 && !envelopeHalt) {
                    envelopeVolume = volumeReloadDivider;
                }
            }

            TickAudio();
        }

        /// <summary>
        /// Length Counter & Sweep Units
        /// </summary>
        public void TickHalf() {
            timerPeriod--;
            if (timerPeriod <= 0) {
                timerPeriod = timerPeriodReset;
                UpdateTargetPeriod();
            }

            if (!envelopeHalt) {
                lengthCounter--;
            }
            if (lengthCounter <= 0 && envelopeHalt) {
                lengthCounter = 0;
                SetMuted(true);
            }

            if (timerPeriod < 8 || targetPeriod >= 0x7ff) {
                SetMuted(true);
            }
            if (sweepDivider == 0 && sweepEnabled) {
                timerPeriod = targetPeriod;
                timerPeriodReset = timerPeriod;
                Frequency = 1789773.0f / (16.0f * (timerPeriod));
                UpdateTargetPeriod();
            } else if (sweepEnabled && sweepDivider == 0 || sweepReload) {
                sweepDivider = sweepDividerReset;
                sweepReload = false;
            } 
            if (sweepEnabled) {
                sweepDivider--;
            }

            //if (sweepEnabled && sweepDivider == 0) {
            //    SweepFrequency();
            //} else if (sweepDivider == 0 || sweepReload) {
            //    sweepDivider = sweepDividerReset;
            //    sweepReload = false;
            //} else {
            //    sweepDivider--;
            //}

            TickAudio();
        }

        public void TickAudio() {
            OutputSound = GetNextSound();
            onSoundGenerate.Invoke(OutputSound, audioSampleNo);
        }

        private void SweepFrequency() {
            var target = timerPeriod + sweepAmount;
            if (target < 8 || target > 0b11111111111) {
                SetMuted(true);
            } else {
                timerPeriod = target;
                timerPeriodReset = timerPeriod;
                Frequency = 1789773.0f / (16.0f * (timerPeriod));
            }
        }

        public void SetVolume(int volume) {
            envelopeVolume = volume * 10;
            Volume = 0.0002f * envelopeVolume;
            volumeReloadDivider = volume;
        }

        public void SetConstantSound(bool constant) {
            constantSound = constant;
        }

        public void SetSweep(bool enabled, int period, bool negative, int shiftCount, int complement) {
            sweepEnabled = enabled;
            sweepAmount = (int)(timerPeriodReset >> shiftCount);
            if (negative) {
                sweepAmount = -sweepAmount + complement;
            }
            sweepDivider = period + 1;
            sweepDividerReset = sweepDivider;
            sweepReload = true;
            UpdateTargetPeriod();
        }

        private void UpdateTargetPeriod() {
            targetPeriod = timerPeriod + sweepAmount;
            if (targetPeriod >= 0x7ff) {
                SetMuted(true);
            }
        }

        public void SetLengthCounter(uint counter) {
            if (channelEnabled) {
                lengthCounter = counter;
                if (lengthCounter <= 0 && envelopeHalt) {
                    lengthCounter = 0;
                    SetMuted(true);
                }
            }
        }

        public void SetHalt(bool halt) {
            envelopeHalt = halt;
        }

        public void SetTimerLo(uint lo) {
            timerPeriod = (int)lo;
        }

        public void SetTimerHi(uint hi) {
            timerPeriod |= (int)(hi << 8);
            UpdateTargetPeriod();
            if (timerPeriod < 8) {
                SetMuted(true);
            } else {
                audioSampleNo = 0;
                timerPeriodReset = timerPeriod + 1;
                Frequency = 1789773.0f / (16.0f * (timerPeriodReset));
                SetMuted(false);
            }
        }

        public void SetMuted(bool muted) {
            isMuted = muted;
        }

        public void SetDuty(int duty) {
            if (duty == 0) {
                DutyCycle = new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 };
            } else if (duty == 1) {
                DutyCycle = new byte[] { 0, 1, 1, 0, 0, 0, 0, 0 };
            } else if (duty == 2) {
                DutyCycle = new byte[] { 0, 1, 1, 1, 1, 0, 0, 0 };
            } else {
                DutyCycle = new byte[] { 1, 0, 0, 1, 1, 1, 1, 1 };
            }
        }
    }
}
