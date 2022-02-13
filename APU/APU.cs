using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NesEmu.APU {
    public class APU {
        //bits bit 3
        //7-4   0   1
        //    -------
        //0   $0A $FE
        //1   $14 $02
        //2   $28 $04
        //3   $50 $06
        //4   $A0 $08
        //5   $3C $0A
        //6   $0E $0C
        //7   $1A $0E
        //8   $0C $10
        //9   $18 $12
        //A   $30 $14
        //B   $60 $16
        //C   $C0 $18
        //D   $48 $1A
        //E   $10 $1C
        //F   $20 $1E
        byte[] LengthLookupTable = new byte[] {
            10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
            12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30,
        };

        WaveOut wo;
        NesAudioMixer mixer;

        /// <summary>
        /// True is 5-step and false is 4-step
        /// </summary>
        bool stepSequenceMode;
        bool interruptInhibit;
        uint cycleCount;

        public APU() {
            wo = new WaveOut();
            stepSequenceMode = false;
            mixer = new NesAudioMixer();
            wo.Init(mixer);
            Reset();
            wo.Play();
        }

        public void Reset() {
            mixer.pulse1.ResetState();
            mixer.pulse2.ResetState();
        }

        public void TickCycle() {
            cycleCount++;

            if (cycleCount == 3729) {
                // Quarter
                mixer.pulse1.TickQuarter();
                mixer.pulse2.TickQuarter();
            } else if (cycleCount == 7457) {
                // Quarter, Half
                mixer.pulse1.TickQuarter();
                mixer.pulse2.TickQuarter();
                mixer.pulse1.TickHalf();
                mixer.pulse2.TickHalf();
            } else if (cycleCount == 11186) {
                // Quarter
                mixer.pulse1.TickQuarter();
                mixer.pulse2.TickQuarter();
            } else if (cycleCount == 14915) { // 4-Step ending
                // Quarter, Half
                if (!stepSequenceMode) {
                    mixer.pulse1.TickQuarter();
                    mixer.pulse2.TickQuarter();
                    mixer.pulse1.TickHalf();
                    mixer.pulse2.TickHalf();
                    cycleCount = 0;
                }
            } else if (cycleCount == 18641) { // 5-Step ending
                // Quarter, Half
                if (stepSequenceMode) {
                    mixer.pulse1.TickQuarter();
                    mixer.pulse2.TickQuarter();
                    mixer.pulse1.TickHalf();
                    mixer.pulse2.TickHalf();
                    cycleCount = 0;
                }
            } else {
                mixer.pulse1.TickAudio();
                mixer.pulse2.TickAudio();
            }
        }

        public void TickAudioOutput() {
            mixer.pulse1.TickAudio();
            mixer.pulse2.TickAudio();
        }

        public void WriteData(ushort address, byte data) {
            // Pulse Channel 1
            if (address >= 0x4000 && address <= 0x4003) {
                var abs = address - 0x4000;
                switch (abs) {
                    case 0:
                        var duty = (data & 0b11000000) >> 6;
                        var envelopeHalt = (data & 0b00100000) >> 5;
                        var constantVolume = (data & 0b00010000) >> 4;
                        var volume = (data & 0b00001111);

                        mixer.pulse1.SetConstantSound(constantVolume == 1);
                        mixer.pulse1.SetDuty(duty);
                        mixer.pulse1.SetVolume(volume);
                        mixer.pulse1.SetHalt(envelopeHalt == 0);
                        break;
                    case 1: // Sweeping
                        var enabled = (data & 0b10000000) >> 7;
                        var period = (data & 0b01110000) >> 4;
                        var negate = (data & 0b00001000) >> 3;
                        var shift = (data & 0b00000111);
                        mixer.pulse1.SetSweep(enabled == 1, period, negate == 1, shift, -1);
                        break;
                    case 2:
                        mixer.pulse1.SetTimerLo(data);
                        break;
                    case 3:
                        var lengthCounterIndex = (data & 0b11111000) >> 3;
                        var loadCounter = LengthLookupTable[lengthCounterIndex];

                        var timerHi = (data & 0b00000111);
                        mixer.pulse1.SetLengthCounter((uint)loadCounter);
                        mixer.pulse1.SetTimerHi((uint)timerHi);
                        break;
                }
            }

            // Pulse Channel 2
            if (address >= 0x4004 && address <= 0x4007) {
                var abs = address - 0x4004;
                switch (abs) {
                    case 0:
                        var duty = (data & 0b11000000) >> 6;
                        var envelopeHalt = (data & 0b00100000) >> 5;
                        var constantVolume = (data & 0b00010000) >> 4;
                        var volume = (data & 0b00001111);

                        mixer.pulse2.SetConstantSound(constantVolume == 1);
                        mixer.pulse2.SetDuty(duty);
                        mixer.pulse2.SetVolume(volume);
                        mixer.pulse2.SetHalt(envelopeHalt == 0);
                        break;
                    case 1: // Sweeping
                        var enabled = (data & 0b10000000) >> 7;
                        var period = (data & 0b01110000) >> 4;
                        var negate = (data & 0b00001000) >> 3;
                        var shift = (data & 0b00000111);
                        mixer.pulse2.SetSweep(enabled == 1, period, negate == 1, shift, 0);
                        break;
                    case 2:
                        mixer.pulse2.SetTimerLo(data);
                        break;
                    case 3:
                        var lengthCounterIndex = (data & 0b11111000) >> 3;
                        var loadCounter = LengthLookupTable[lengthCounterIndex];

                        var timerHi = (data & 0b00000111);
                        mixer.pulse2.SetLengthCounter((uint)loadCounter);
                        mixer.pulse2.SetTimerHi((uint)timerHi);
                        break;
                }
            }

            //// Triangle Channel
            //if (address >= 0x4008 && address <= 0x400B) {

            //}

            //// Noise Channel
            //if (address >= 0x400C && address <= 0x400F) {

            //}

            //// Delta Modulation Channel
            //if (address >= 0x400C && address <= 0x400F) {

            //}

            if (address == 0x4015) {
                var pulse1Enable = (data & 1) > 0;
                var pulse2Enable = (data & 2) > 0;
                mixer.pulse1.SetEnable(pulse1Enable);
                mixer.pulse2.SetEnable(pulse2Enable);
            }

            if (address == 0x4017) {
                var sequencerMode = (address & 0b10000000) > 0;
                var interruptInhibitFlag = (address & 0b01000000) > 0;

                stepSequenceMode = sequencerMode;
                interruptInhibit = interruptInhibitFlag;
            }
        }

        public byte ReadData(ushort address) {
            // Status Register
            if (address == 0x4015) {
                return 0;
            }

            // Frame Counter
            if (address == 0x4017) {
                return 0;
            }

            return 0;
        }
    }
}
