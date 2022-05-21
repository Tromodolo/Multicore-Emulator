using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Mapper {
    public interface IMapper {
        public bool DidMap();

        public void RegisterRom(Rom.Rom rom);

        public byte CpuRead(ushort address);
        public void CpuWrite(ushort address, byte value);

        public byte PPURead(ushort address);
        public void PPUWrite(ushort address, byte value);
    }
}
