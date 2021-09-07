using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Bus {
    public partial class Bus {
        const ushort RamMirrorStart = 0x0000;
        const ushort RamMirrorsEnd = 0x1fff;
        const ushort PPUMirrorsStart = 0x2008;
        const ushort PPUMirrorsEnd = 0x3fff;
        const ushort PrgRomStart = 0x8000;
        const ushort PrgRomEnd = 0xffff;

        public void MemWrite(ushort address, byte value) {
            if (address is >= RamMirrorStart and <= RamMirrorsEnd) {
                var mirroredAddr = (ushort)(address & 0b11111111111);
                VRAM[mirroredAddr] = value;
            } else if (address is >= PPUMirrorsStart and <= PPUMirrorsEnd) {
                var mirroredAddr = (ushort)(address & 0b00100000_00000111);
                MemWrite(mirroredAddr, value);
            } else if (address is >= PrgRomStart and <= PrgRomEnd) {
                throw new Exception("Trying to write to ROM");
            } else if (address is 0x2000) {
                PPU.WriteCtrl(value);
            } else if (address is 0x2001) {
                PPU.WriteMask(value);
            } else if (address is 0x2002) {
                throw new Exception("Trying to write to PPU Status");
            } else if (address is 0x2003) {
                PPU.WriteOAMAddr(value);
            } else if (address is 0x2004) {
                PPU.WriteOAMData(value);
            } else if (address is 0x2005) {
                PPU.WriteScroll(value);
            } else if (address is 0x2006) {
                PPU.WritePPUAddr(value);
            } else if (address is 0x2007) {
                PPU.WriteData(value); ;
            } else if (address is >= 0x4000 and <= 0x4013 or 0x4015) {
                return; // APU
            } else if (address is 0x4014) {
                // https://wiki.nesdev.com/w/index.php/PPU_programmer_reference#OAM_DMA_.28.244014.29_.3E_write
                // OAM DMA
            } else if (address is 0x4016) {
                return; // Controller 1
            } else if (address is 0x4017) {
                return; // Controller 2
            } else {
                return;
            }
            
        }
        public byte MemRead(ushort address) {
            if (address is >= RamMirrorStart and <= RamMirrorsEnd) {
                var mirroredAddr = (ushort)(address & 0b00000111_11111111);
                return VRAM[mirroredAddr];
            } else if (address is >= PPUMirrorsStart and <= PPUMirrorsEnd) {
                var mirroredAddr = (ushort)(address & 0b00100000_00000111);
                return MemRead(mirroredAddr);
            } else if (address is >= PrgRomStart and <= PrgRomEnd) {
                return ReadPrgRom(address);
            } else if (address is 0x2000 or 0x2001 or 0x2003 or 0x2005 or 0x2006 or 0x4014) {
                return 0;
            } else if (address is 0x2002) {
                return PPU.GetStatus();
            } else if (address is 0x2004) {
                return PPU.GetOAMData();
            } else if (address is 0x2007) {
                return PPU.GetData();
            } else if (address is >= 0x4000 and <= 0x4013 or 0x4015) {
                return 0; // APU
            } else if (address is 0x4016) {
                return 0; // Controller 1
            } else if (address is 0x4017) {
                return 0; // Controller 2
            } else {
                return 0;
            }
        }

        public void MemWriteShort(ushort address, ushort value) {
            var hiValue = (byte)(value >> 8);
            var loValue = (byte)(value & 0xff);
            MemWrite(address, loValue);
            MemWrite(++address, hiValue);
        }
        public ushort MemReadShort(ushort address) {
            var loValue = MemRead(address);
            var hiValue = MemRead((ushort)(address + 1));
            return (ushort)(hiValue << 8 | loValue);
        }
    }
}
