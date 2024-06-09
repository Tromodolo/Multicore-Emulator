using MultiCoreEmulator.Utility;

namespace MultiCoreEmulator.Cores.GBC {
    [Flags]
    internal enum InterruptType {
        None = 0x00,
        VBlank = 0x01,
        LCD = 0x02,
        Timer = 0x04,
        Serial = 0x08,
        Joypad = 0x10
    }

    internal partial class Board {
        string FileName;

        CPU CPU;
        internal Display Display;
        GameCart Cart;

        int WRamBank = 1;
        Memory<byte> WRam;

        Memory<byte> HRam;

        int VRamBank;
        Memory<byte> VRam;

        Memory<byte> OAM;

        public int SamplesCollected;
        public CircleBuffer<short> AudioBuffer;

        byte[] WaveRam = new byte[16];
        byte JoypadState;

        bool BootRomEnabled;

        int MasterClock = 0;

        int InterruptsPendingTimer;
        bool InterruptsEnablePendingValue;

        bool InterruptsEnabled;
        InterruptType ActiveInterrupts;
        InterruptType RequestedInterrupts;

        public Board(string filename, byte[] rawBytes) {
            FileName = filename;

            var byteSpan = new Span<byte>(rawBytes);
            Cart = new GameCart(byteSpan);

            VRamBank = 0;
            VRam = new Memory<byte>(new byte[0x4000]);

            WRamBank = 1;
            WRam = new Memory<byte>(new byte[0x8000]);

            CPU = new CPU(Path.GetFileName(filename));
            Display = new Display();

            OAM = new Memory<byte>(new byte[256]);
            HRam = new Memory<byte>(new byte[256]);
            
            AudioBuffer = new CircleBuffer<short>(735 * 3);

            BootRomEnabled = true;
        }

        public void Clock() {
            Display.Clock(this);
            CPU.Clock(this);
            TickTimer();
            MasterClock++;
        }

        public void Draw(ref GraphicsDevice gd, ref Texture tex) {
            Display.Draw(ref gd, ref tex);
        }

        public byte Read(ushort address) {
            if (address == 0xFFFF) { // Enabled Interrupts
                return (byte)ActiveInterrupts;
            } else if (address == 0xFF0F) { // Requested Interrupts
                return (byte)RequestedInterrupts;
            } else if (address <= 0x7FFF) { // ROM Bank 0-NN
                Cart.Read(address, out byte value);
                return value;
            } else if (address >= 0x8000 && address <= 0x9FFF) { // VRAM Bank 0/1
                return VRam.Span[(address % 0x8000) + (0x2000 * VRamBank)];
            } else if (address >= 0xA000 && address <= 0xBFFF) { // External Cart RAM
                Cart.Read(address, out byte value);
                return value;
            } else if (address >= 0xC000 && address <= 0xCFFF) { // WRAM Bank 0
                return WRam.Span[(address % 0xC000)];
            } else if (address >= 0xD000 && address <= 0xDFFF) { // Switchable WRAM 1-7
                return WRam.Span[(address % 0xD000) + (0x1000 * WRamBank)];
            } else if (address >= 0xE000 && address <= 0xFDFF) { // Prohibited
                // Debug.Assert(false, "Prohibited address");
                return 0;
            } else if (address >= 0xFE00 && address <= 0xFE9F) { // OAM
                return OAM.Span[address % 0xFE00];
            } else if (address >= 0xFEA0 && address <= 0xFEFF) { // Unusable
                // Debug.Assert(false, "Prohibited address");
                return 0;
            } else if (address >= 0xFF00 && address <= 0xFF7F) { // I/O
                return ReadIO(address);
            } else if (address >= 0xFF80 && address <= 0xFFFE) { // High RAM
                return HRam.Span[address % 0xFF80];
            }
            return 0;
        }

        public void Write(ushort address, byte value) {
            if (address == 0xFFFF) {// Enabled interrupts
                ActiveInterrupts = (InterruptType)value;
            } else if (address == 0xFF0F) { // Requested interrupts
                RequestedInterrupts = (InterruptType)value;
            } else if (address <= 0x3FFF) { // ROM Bank 0-NN
                Cart.Write(address, value);
            } else if (address >= 0x8000 && address <= 0x9FFF) {// VRAM Bank 0/1
                VRam.Span[(address % 0x8000) + (0x2000 * VRamBank)] = value;
            } else if (address >= 0xA000 && address <= 0xBFFF) { // External Cart RAM
                Cart.Write(address, value);
            } else if (address >= 0xC000 && address <= 0xD000) { // WRAM Bank 0
                WRam.Span[(address % 0xC000)] = value;
            } else if (address >= 0xD000 && address <= 0xDFFF) {// Switchable WRAM 1-7
                WRam.Span[(address % 0xD000) + (0x1000 * WRamBank)] = value;
            } else if (address >= 0xE000 && address <= 0xFDFF) {// Prohibited
                // Debug.Assert(false, "Prohibited address");
            } else if (address >= 0xFE00 && address <= 0xFE9F) {// OAM
                OAM.Span[address % 0xFE00] = value;
            } else if (address >= 0xFEA0 && address <= 0xFEFF) {// Unusable
                // Debug.Assert(false, "Prohibited address");
            } else if (address >= 0xFF00 && address <= 0xFF7F) {// I/O
                WriteIO(address, value);
            } else if (address >= 0xFF80 && address <= 0xFFFE) {// High RAM
                HRam.Span[address % 0xFF80] = value;
            }
        }

        // Used by HALT to check if there is a pending interrupt independent of the enable flag
        public bool PeekPendingInterrupt() {
            return (ActiveInterrupts & RequestedInterrupts) > 0;
        }

        public bool HasPendingInterrupt() {
            if (!InterruptsEnabled) {
                return false;
            }

            return (ActiveInterrupts & RequestedInterrupts) > 0;
        }

        // Returns interrupts in order from most prio to least prio
        public InterruptType GetPendingInterrupt() {
            var interrupts = ActiveInterrupts & RequestedInterrupts;
            if ((interrupts & InterruptType.VBlank) == InterruptType.VBlank) {
                return InterruptType.VBlank;
            } else if ((interrupts & InterruptType.LCD) == InterruptType.LCD) {
                return InterruptType.LCD;
            } else if ((interrupts & InterruptType.Timer) == InterruptType.Timer) {
                return InterruptType.Timer;
            } else if ((interrupts & InterruptType.Serial) == InterruptType.Serial) {
                return InterruptType.Serial;
            } else if ((interrupts & InterruptType.Joypad) == InterruptType.Joypad) {
                return InterruptType.Joypad;
            } else {
                return InterruptType.None;
            }
        }

        public void TriggerInterrupt(InterruptType type) {
            // if ((ActiveInterrupts & type) == type) {
                RequestedInterrupts |= type;
            // }
        }

        public void ClearInterrupt(InterruptType type) {
            RequestedInterrupts &= ~type;
        }

        public void SetInterruptEnabled(bool enabled) {
            InterruptsEnablePendingValue = enabled;
            InterruptsPendingTimer = enabled ? 1 : 0;
        }

        // Only used by interrupts
        public void SetInterruptEnabledUnsafe(bool enabled) {
            InterruptsEnabled = enabled;
        }

        public void TickInterruptTimer() {
            if (InterruptsPendingTimer-- == 0) {
                InterruptsEnabled = InterruptsEnablePendingValue;
                InterruptsPendingTimer = -1;
            }
        }
    }
}
