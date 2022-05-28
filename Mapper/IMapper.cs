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

        public void SetProgramCounter(int pc);
        public void SetScanline(int scanline);

        public bool DidMap();
        public void RegisterRom(Rom.Rom rom);

        public byte CpuRead(ushort address);
        public void CpuWrite(ushort address, byte value);

        public byte PPURead(ushort address);
        public void PPUWrite(ushort address, byte value);

        public void Save(BinaryWriter writer);
        public void Load(BinaryReader reader);
    }
}
