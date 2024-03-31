using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiCoreEmulator.Cores.GBC.Cartridge.Mapper {
    internal interface IMapper {
        public bool Write(ref Memory<byte> ram, ref Memory<byte> rom, ushort address, byte value);

        public bool Read(ref Memory<byte> ram, ref Memory<byte> rom, ushort address, out byte value);
    }
}
