namespace NesEmu.Bus {
    public partial class Bus {
        const ushort RAM_MIRRORS_END = 0x1fff;
        const ushort PPU_MIRRORS_END = 0x3fff;
        
        byte player1ButtonState;
        byte player1ButtonLatch;

        bool mapperDidMap;
        
        public void MemWrite(ushort address, byte value) {
            currentMapper.CpuWrite(address, value, out mapperDidMap);
            if (mapperDidMap) {
                return;
            }

            if (address <= RAM_MIRRORS_END) {
                var mirror = (ushort)(address & 0b11111111111);
                VRAM[mirror] = value;
            } else if (address == 0x2000) {     //Ctrl
                PPU.WriteCtrl(value);
            } else if (address == 0x2001) {     //Mask
                PPU.SetMask(value);
            } else if (address == 0x2002) {     //Status
                throw new Exception("Trying to write to status REEEEEEEEEEEEEEEEEEEEEEEEEEE");
            } else if (address == 0x2003) {     //OAM Addr
                PPU.OAMAddress = value;
            } else if (address == 0x2004) {     //OAM Data
                PPU.WriteOAMData(value);
            } else if (address == 0x2005) {     //Scroll
                PPU.WriteScroll(value);
            } else if (address == 0x2006) {     //Addr
                PPU.WritePPUAddress(value);
            } else if (address == 0x2007) {     //Data
                PPU.WriteData(value);
            } else if ((address >= 0x4000 && address <= 0x4013) || address == 0x4015) {
                APU.WriteReg(address, value);
            } else if (address == 0x4016) {
                if ((value & 1) == 1) {
                    ResetButtonlatch();
                }
                // Controller 1, not handled yet
            } else if (address == 0x4017) {
                // Controller 2, not handled yet
            } else if (address == 0x4014) {
                DMAActive = true;
                DMAPage = value;
                DMAAddress = 0x00;
            } else if (address <= PPU_MIRRORS_END) {
                var mirror = (ushort)(address & 0b00100000_00000111);
                MemWrite(mirror, value);
            }
        }

        public byte MemRead(ushort address) {
            unsafe {
                byte mapperValue = currentMapper.CpuRead(address, out mapperDidMap);
                if (mapperDidMap) {
                    return mapperValue;
                }

                if (address <= RAM_MIRRORS_END) {
                    fixed (byte* ptr = VRAM) {
                        var mirror = (ushort)(address & 0b11111111111);
                        return *(ptr + mirror);
                    }
                } else if (address == 0x2000) {            //Ctrl
                    return 0;
                } else if (address == 0x2001) {     //Mask
                    return 0;
                } else if (address == 0x2002) {     //Status
                    return PPU.GetStatus();
                } else if (address == 0x2003) {     //OAM Addr
                    return PPU.OAMAddress;
                } else if (address == 0x2004) {     //OAM Data
                    return PPU.GetOAMData();
                } else if (address == 0x2005) {     //Scroll
                    return 0;
                } else if (address == 0x2006) {     //Addr
                    return 0;
                } else if (address == 0x2007) {     //Data
                    return PPU.GetData();
                } else if ((address >= 0x4000 && address <= 0x4013) || address == 0x4015) {
                    return 0;
                } else if (address == 0x4016) {
                    return GetNextButtonState();
                } else if (address == 0x4017) {
                    // Controller 2, not handled yet
                    return 0;
                } else if (address <= PPU_MIRRORS_END) {
                    var mirror = (ushort)(address & 0b00100000_00000111);
                    return MemRead(mirror);
                } else {
                    return 0;
                }
            }
        }

        public void MemWriteShort(ushort address, ushort value) {
            var hiValue = (byte)(value >> 8);
            var loValue = (byte)(value & 0xff);
            MemWrite(address, loValue);
            MemWrite(++address, hiValue);
        }

        public ushort MemReadShort(ushort address) {
            byte loValue = MemRead(address);
            byte hiValue = MemRead(++address);
            return (ushort)(hiValue << 8 | loValue);
        }
        
        public void UpdateControllerState(byte newState) { 
            player1ButtonState = newState;
        }

        public byte GetAllButtonState() {
            return player1ButtonState;
        }

        // Order:
        // A, B, Select, Start, Up, Down, Left, Right
        byte GetNextButtonState() {
            var unmasked =  (byte)(player1ButtonState >> (7 - player1ButtonLatch));
            player1ButtonLatch++;
            return (byte)(unmasked & 0b1);
        }

        void ResetButtonlatch() {
            player1ButtonLatch = 0;
        }
    }
}
