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
            if (address >= RamMirrorStart && address <= RamMirrorsEnd) {
                var mirror = (ushort)(address & 0b11111111111);
                VRAM[mirror] = value;
            }

            if (address == 0x2000) {            //Ctrl
                FastForwardPPU();
                PPU.WriteCtrl(value);
            } else if (address == 0x2001) {     //Mask
                FastForwardPPU();
                PPU.WriteMask(value);
            } else if (address == 0x2002) {     //Status
                throw new Exception("Trying to write to status REEEEEEEEEEEEEEEEEEEEEEEEEEE");
            } else if (address == 0x2003) {     //OAM Addr
                FastForwardPPU();
                PPU.WriteOAMAddr(value);
            } else if (address == 0x2004) {     //OAM Data
                FastForwardPPU();
                PPU.WriteOAMData(value);
            } else if (address == 0x2005) {     //Scroll
                FastForwardPPU();
                PPU.WriteScroll(value);
            } else if (address == 0x2006) {     //Addr
                FastForwardPPU();
                PPU.WritePPUAddr(value);
            } else if (address == 0x2007) {     //Data
                FastForwardPPU();
                PPU.WriteData(value);
            }

            if ((address >= 0x4000 && address <= 0x4013) || address == 0x4015) {
                // APU, not handled yet
            } 

            if (address == 0x4016) {
                // Controller 1, not handled yet
            } else if (address == 0x4017) {
                // Controller 2, not handled yet
            }

            if (address == 0x4014) {
                // https://wiki.nesdev.com/w/index.php/PPU_programmer_reference#OAM_DMA_.28.244014.29_.3E_write
                //DMA, not handled yet
            }

            if (address >= 0x2008 && address <= PPUMirrorsEnd) {
                var mirror = (ushort)(address & 0b00100000_00000111);
                MemWrite(mirror, value);
            }

            if (address >= 0x8000 && address <= 0xffff) {
                // Attempting to write to ROM
            }

            // Unknown address
            return;
            
        }
        public byte MemRead(ushort address) {
            if (address >= RamMirrorStart && address <= RamMirrorsEnd) {
                var mirror = (ushort)(address & 0b11111111111);
                return VRAM[mirror];
            }

            if (address == 0x2000) {            //Ctrl
                return 0;
            } else if (address == 0x2001) {     //Mask
                return 0;
            } else if (address == 0x2002) {     //Status
                FastForwardPPU();
                return PPU.GetStatus();
            } else if (address == 0x2003) {     //OAM Addr
                FastForwardPPU();
                return PPU.GetOAMAddr();
            } else if (address == 0x2004) {     //OAM Data
                FastForwardPPU();
                return PPU.GetOAMData();
            } else if (address == 0x2005) {     //Scroll
                return 0;
            } else if (address == 0x2006) {     //Addr
                return 0;
            } else if (address == 0x2007) {     //Data
                return PPU.GetData();
            }

            if ((address >= 0x4000 && address <= 0x4013) || address == 0x4015) {
                return 0;
            }

            if (address == 0x4016) {
                // Controller 1, not handled yet
                return 0;
            } else if (address == 0x4017) {
                // Controller 2, not handled yet
                return 0;
            }

            if (address == 0x4014) {
                return 0;
            }

            if (address >= 0x2008 && address <= PPUMirrorsEnd) {
                var mirror = (ushort)(address & 0b00100000_00000111);
                return MemRead(mirror);
            }

            if (address >= 0x8000 && address <= 0xffff) {
                return ReadPrgRom(address);
            }

            // Unknown address
            return 0;
        }

        public void MemWriteShort(ushort address, ushort value) {
            var hiValue = (byte)(value >> 8);
            var loValue = (byte)(value & 0xff);
            MemWrite(address, loValue);
            MemWrite(++address, hiValue);
        }
        public ushort MemReadShort(ushort address) {
            var loValue = MemRead(address);
            var hiValue = MemRead(++address);
            return (ushort)(hiValue << 8 | loValue);
        }
    }
}
