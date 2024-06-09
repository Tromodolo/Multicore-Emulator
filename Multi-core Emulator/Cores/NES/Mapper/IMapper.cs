using NesEmu.Rom;

namespace NesEmu.Mapper {
    public interface IMapper {
        public ScreenMirroring GetMirroring();
        public bool GetIRQ() {
            return false;
        }

        public void SetIRQ(bool IRQ) {}

        public void SetProgramCounter(int pc) {}
        public void DecrementScanline() { }

        // public bool DidMap();
        public int MappedAddress() { return 0; }
        public void RegisterRom(Rom.Rom rom);
        
        /// <summary>
        /// Used in the case that a mapper has a bus conflict
        /// </summary>
        /// <param name="bus"></param>
        public void RegisterBus(Bus.Bus bus) {}

        public byte CpuRead(ushort address, out bool handled);
        public void CpuWrite(ushort address, byte value, out bool handled);

        public byte PPURead(ushort address, out bool handled);
        public void PPUWrite(ushort address, byte value, out bool handled);

        public void Persist() { }
        public void Save(BinaryWriter writer) { }
        public void Load(BinaryReader reader) { }
    }
}
