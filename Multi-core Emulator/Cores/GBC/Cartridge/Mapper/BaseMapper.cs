using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiCoreEmulator.Cores.GBC.Cartridge.Mapper {
    internal class BaseMapper : IMapper {
        public bool Write(ref Memory<byte> ram, ref Memory<byte> rom, ushort address, byte value) {
            bool didMap = false;

            if (address <= 0x7FFF) {
                rom.Span[address] = value;
                didMap = true;
            } else if (address >= 0xA000 && address <= 0xBFFF) {
                // RAM
                if (ram.Span.Length > 0) {
                    ram.Span[address % 0xA000] = value;
                    didMap = true;
                }
            }

            return didMap;
        }

        public bool Read(ref Memory<byte> ram, ref Memory<byte> rom, ushort address, out byte value) {
            bool didMap = false;
            value = 0;

            if (address <= 0x7FFF) {
                value = rom.Span[address];
                didMap = true;
            } else if (address >= 0xA000 && address <= 0xBFFF) {
                // RAM
                if (ram.Span.Length > 0) {
                    value = ram.Span[address % 0xA000];
                    didMap = true;
                }
            }

            return didMap;
        }
    }
}
