using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MemWrite(ushort address, byte value) {
            if (address >= RamMirrorStart && address <= RamMirrorsEnd) {
                var mirror = (ushort)(address & 0b11111111111);
                VRAM[mirror] = value;
            }

            if (address == 0x2000) {            //Ctrl
                PPU.WriteCtrl(value);
            } else if (address == 0x2001) {     //Mask
                PPU.WriteMask(value);
            } else if (address == 0x2002) {     //Status
                throw new Exception("Trying to write to status REEEEEEEEEEEEEEEEEEEEEEEEEEE");
            } else if (address == 0x2003) {     //OAM Addr
                PPU.WriteOAMAddr(value);
            } else if (address == 0x2004) {     //OAM Data
                PPU.WriteOAMData(value);
            } else if (address == 0x2005) {     //Scroll
                PPU.WriteScroll(value);
            } else if (address == 0x2006) {     //Addr
                PPU.WritePPUAddr(value);
            } else if (address == 0x2007) {     //Data
                PPU.WriteData(value);
            }

            if ((address >= 0x4000 && address <= 0x4013) || address == 0x4015) {
                APU.WriteReg(address, value);
            } 

            if (address == 0x4016) {
                if ((value & 1) == 1) {
                    Controller1.ResetLatch();
                }
                // Controller 1, not handled yet
            } else if (address == 0x4017) {
                // Controller 2, not handled yet
            }

            if (address == 0x4014) {
                DmaActive = true;
                DmaPage = value;
                DmaAddr = 0x00;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte MemRead(ushort address) {
            unsafe {
                if (address >= RamMirrorStart && address <= RamMirrorsEnd) {
                    fixed (byte* ptr = VRAM) {
                        var mirror = (ushort)(address & 0b11111111111);
                        return *(ptr + mirror);
                    }
                    //return VRAM[mirror];
                }

                if (address == 0x2000) {            //Ctrl
                    return 0;
                } else if (address == 0x2001) {     //Mask
                    return 0;
                } else if (address == 0x2002) {     //Status
                    return PPU.GetStatus();
                } else if (address == 0x2003) {     //OAM Addr
                    return PPU.GetOAMAddr();
                } else if (address == 0x2004) {     //OAM Data
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
                    return Controller1.ReadNextButton();
                } else if (address == 0x4017) {
                    // Controller 2, not handled yet
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MemWriteShort(ushort address, ushort value) {
            var hiValue = (byte)(value >> 8);
            var loValue = (byte)(value & 0xff);
            MemWrite(address, loValue);
            MemWrite(++address, hiValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort MemReadShort(ushort address) {
            var loValue = MemRead(address);
            var hiValue = MemRead(++address);
            return (ushort)(hiValue << 8 | loValue);
        }
    }
}
