using MultiCoreEmulator.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MultiCoreEmulator.Cores.GBC {
    internal partial class Board {
        ushort TimerDivider;
        byte TimerCounter;
        byte TimerModulo;
        byte TimerControl;

        byte TimerValue;
        byte LastTimerValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteIO(ushort address, byte value) {
            // Joypad
            if (address == 0xFF00) {
                JoypadState = value;
            }

            // SB: Serial transfer data
            // TODO(tromo): unused, unsure if i feel like adding link cable?
            if (address == 0xFF01) {
                Console.Write((char)value);
            }
            // SC: Serial transfer control
            // TODO(tromo): unused, unsure if i feel like adding link cable?
            if (address == 0xFF02) {
                var x = 2;
            }

            if (address == 0xFF02 && value == 0x81) {
                // Console.Write(Encoding.ASCII.GetString(serialData.ToArray()));
            }

            // Timer Divider
            if (address == 0xFF04) {
                TimerDivider = 0;
            }
            // Timer counter
            if (address == 0xFF05) {
                TimerCounter = value;
            }
            // Timer modulo
            if (address == 0xFF06) {
                TimerModulo = value;
            }
            // Timer control
            if (address == 0xFF07) {

            }

            // APU channel controls
            if (address >= 0xFF10 && address <= 0xFF26) {
                WriteAudio(address, value);
            }
            // Wave ram, pass it to the APU code
            if (address >= 0xFF30 && address <= 0xFF3F) {
                WriteAudio(address, value);
            }

            // LCD registers
            if (address >= 0xFF40 && address <= 0xFF4B) {
                Display.Write(address, value);
            }

            // Bootrom
            if (address == 0xFF50) {
                BootRomEnabled = value > 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ReadIO(ushort address) {
            if (address == 0xFF00) {
                return JoypadState;
            }

            // SB: Serial transfer data
            // TODO(tromo): unused, unsure if i feel like adding link cable?
            if (address == 0xFF01) {}
            // SC: Serial transfer control
            // TODO(tromo): unused, unsure if i feel like adding link cable?
            if (address == 0xFF02) {}

            // Timer Divider
            if (address == 0xFF04) {
                return (byte)(TimerDivider >> 8);
            }
            // Timer counter
            if (address == 0xFF05) {
                return TimerCounter;
            }
            // Timer modulo
            if (address == 0xFF06) {
                return TimerModulo;
            }
            // Timer control
            if (address == 0xFF07) {
                return TimerControl;
            }

            // APU channel controls
            if (address >= 0xFF10 && address <= 0xFF26) {
                return ReadAudio(address);
            }
            // Wave ram, pass it to the APU code
            if (address >= 0xFF30 && address <= 0xFF3F) {
                return ReadAudio(address);
            }

            // LCD registers
            if (address >= 0xFF40 && address <= 0xFF4B) {
                return Display.Read(address);
            }

            // Bootrom
            if (address == 0xFF50) {}

            return 0;
        }

        public void TickTimer() {
            TimerDivider++;

            // 2    - Enabled
            //  10  - Timer speed
            // Bits 3-7 are unused
            if ((TimerControl & 0b100) == 0) {
                return;
            }

            // Speed based on Divider:
            // 00 = bit 9,  /1024 4096 hz    & 0x200
            // 01 = bit 3,  /16   262144 hz  & 0x08
            // 10 = bit 5,  /64   65536 hz   & 0x20
            // 11 = bit 7,  /256  16384 hz   & 0x80
            // Tick TimerCounter when falling edge of bit in divider
            TimerValue = 0;
            switch (TimerControl & 0x3) {
                case 0:
                    TimerValue = (byte)(TimerDivider >> 9 & 0x1);
                    break;
                case 1:
                    TimerValue = (byte)(TimerDivider >> 3 & 0x1);
                    break;
                case 2:
                    TimerValue = (byte)(TimerDivider >> 5 & 0x1);
                    break;
                case 3:
                    TimerValue = (byte)(TimerDivider >> 7 & 0x1);
                    break;
            }

            // Falling edge
            if (TimerValue == 0 && LastTimerValue == 1) {
                if (TimerCounter == 0xFF) {
                    TimerCounter = TimerModulo;
                    TriggerInterrupt(InterruptType.Timer);
                } else {
                    TimerCounter++;
                }
            }

            LastTimerValue = TimerValue;
        }
    }
}
