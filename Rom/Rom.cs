using NesEmu.Mapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu.Rom {
    public enum ScreenMirroring {
        Vertical, 
        Horizontal,
        FourScreen
    }

    // https://wiki.nesdev.com/w/index.php?title=INES
    public class Rom {
        static byte[] NesTag = new byte[] { 0x4e, 0x45, 0x53, 0x1a };

        public byte[] PrgRom;
        public byte[] ChrRom;

        public byte[] PrgRam = new byte[0];
        public byte[] PrgNvRam = new byte[0];

        public byte[] ChrRam = new byte[0];
        public byte[] ChrNvRam = new byte[0];

        public ScreenMirroring Mirroring;
        public IMapper Mapper;

        // NES 2.0 values
        public byte SubMapper;

        public Rom(byte[] rawBytes) {
            //NES 1.0 
            if (!(
                rawBytes[0] == NesTag[0] &&
                rawBytes[1] == NesTag[1] &&
                rawBytes[2] == NesTag[2] &&
                rawBytes[3] == NesTag[3])) {
                throw new Exception("File is not in iNES 1.0 file format");
            }

            var control1 = rawBytes[6];
            var control2 = rawBytes[7];

            var mapperLo = (control1 & 0b11110000) >> 4;
            var mapperHi = control2 & 0b11110000;
            var mapper = (byte)(mapperLo | mapperHi);

            ScreenMirroring mirror;
            if ((control1 & 0b1000) != 0) {
                mirror = ScreenMirroring.FourScreen;
            } else {
                if ((control1 & 0b1) != 0) {
                    mirror = ScreenMirroring.Horizontal;
                } else {
                    mirror = ScreenMirroring.Vertical;
                }
            }

            int prgRomSize;
            int chrRomSize;

            // NES 2.0
            var iNesVersion = (control2 >> 2) & 0b11;
            if (iNesVersion != 0) {
                SubMapper = rawBytes[8];

                // LSB
                var prgRomLsb = rawBytes[4];
                var chrRomLsb = rawBytes[5];

                var prgRomMsb = rawBytes[9] & 0b1111;
                var chrRomMsb = (rawBytes[9] & 0b11110000) >> 4;

                if ((prgRomLsb | (prgRomMsb << 8)) <= 15) {
                    prgRomSize = prgRomLsb * 0x4000;
                } else {
                    prgRomSize = prgRomLsb | (prgRomMsb << 8);
                }

                if ((chrRomLsb | (chrRomMsb << 8)) <= 15) {
                    chrRomSize = chrRomLsb * 0x2000;
                } else {
                    chrRomSize = chrRomLsb | (chrRomMsb << 8);
                }

                var volatileShifts = rawBytes[10];
                var prgRamShift = volatileShifts & 0b1111;
                var prgNvRamShift = (volatileShifts & 0b1111) >> 4;
                if (prgRamShift != 0) {
                    PrgRam = new byte[64 << prgRamShift];
                }
                if (prgNvRamShift != 0) {
                    PrgNvRam = new byte[64 << prgNvRamShift];
                }

                var volatileChrShifts = rawBytes[11];
                var chrRamShift = volatileChrShifts & 0b1111;
                var chrNvRamShift = (volatileChrShifts & 0b1111) >> 4;
                if (chrRamShift != 0) {
                    ChrRam = new byte[64 << chrRamShift];
                }
                if (chrNvRamShift != 0) {
                    ChrNvRam = new byte[64 << chrNvRamShift];
                }

                if ((rawBytes[12] & 0b11) != 0) {
                    throw new NotImplementedException("There is only support for NTSC ROMs at this moment");
                } 
            } else {
                var prgSize = rawBytes[4];
                var chrSize = rawBytes[5];

                if (prgSize <= 15) {
                    prgRomSize = prgSize * 0x4000;
                } else {
                    prgRomSize = prgSize;
                }

                if (chrSize <= 15) {
                    chrRomSize = chrSize * 0x2000;
                } else {
                    chrRomSize = chrSize;
                }
            }

            var skip_trainer = (control1 & 0b100) != 0;

            int prgRomStart = 16;
            if (skip_trainer) {
                prgRomStart += 512;
            } else {
                prgRomStart += 0;
            }
            var chrRomStart = prgRomStart + prgRomSize;

            Mirroring = mirror;
            PrgRom = rawBytes[prgRomStart..(prgRomStart + prgRomSize)];
            ChrRom = chrRomSize == 0 ? new byte[0x2000 * 16] : rawBytes[chrRomStart..(chrRomStart + chrRomSize)];
            Mapper = GetMapper(mapper);

            Mapper.RegisterRom(this);
        }

        private IMapper GetMapper(byte mapperId) {
            return mapperId switch {
                0 => new NROM(),
                2 => new UxROM(),
                _ => throw new NotImplementedException($"Mapper {mapperId} not implemented"),
            };
        }
    }
}
