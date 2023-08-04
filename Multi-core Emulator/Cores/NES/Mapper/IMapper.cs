using NesEmu.Rom;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public byte CpuRead(ushort address, out bool handled);
        public void CpuWrite(ushort address, byte value, out bool handled);

        public byte PPURead(ushort address, out bool handled);
        public void PPUWrite(ushort address, byte value, out bool handled);

        public void Persist() { }
        public void Save(BinaryWriter writer) { }
        public void Load(BinaryReader reader) { }
    }
}
