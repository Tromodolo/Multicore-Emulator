using NesEmu.Mapper;
using System;

namespace NesEmu.Rom {
    public enum ScreenMirroring {
        Vertical, 
        Horizontal,
        FourScreen,
        OneScreenLower,
        OneScreenUpper
    }

    // https://wiki.nesdev.com/w/index.php?title=INES
    public class Rom {
        static byte[] NesTag = new byte[] { 0x4e, 0x45, 0x53, 0x1a };

        public readonly byte[] ChrRom;
        public readonly byte[] PrgRom;
        public readonly byte[] PrgRam;
        
        // Not used yet
        readonly byte[] ChrRam;
        readonly byte[] ChrNvRam;
        readonly byte[] PrgNvRam;

        public readonly ScreenMirroring Mirroring;
        public readonly IMapper Mapper;
        public readonly string Filename;

        // NES 2.0 values
        byte SubMapper;

        public Rom(byte[] rawBytes, string filename) {
            Filename = filename;

            //NES 1.0 
            if (!(
                rawBytes[0] == NesTag[0] &&
                rawBytes[1] == NesTag[1] &&
                rawBytes[2] == NesTag[2] &&
                rawBytes[3] == NesTag[3])) {
                throw new Exception("File is not in iNES 1.0 file format");
            }

            byte control1 = rawBytes[6];
            byte control2 = rawBytes[7];

            int mapperLo = (control1 & 0b11110000) >> 4;
            int mapperHi = control2 & 0b11110000;
            var mapper = (byte)(mapperLo | mapperHi);

            ScreenMirroring mirror;
            if ((control1 & 0b1000) != 0) {
                mirror = ScreenMirroring.FourScreen;
            } else {
                if ((control1 & 0b1) == 0) {
                    mirror = ScreenMirroring.Horizontal;
                } else {
                    mirror = ScreenMirroring.Vertical;
                }
            }

            int prgRomSize;
            int chrRomSize;

            // NES 2.0
            int iNesVersion = (control2 >> 2) & 0b11;
            if (iNesVersion != 0) {
                SubMapper = rawBytes[8];

                // LSB
                byte prgRomLsb = rawBytes[4];
                byte chrRomLsb = rawBytes[5];

                int prgRomMsb = rawBytes[9] & 0b1111;
                int chrRomMsb = (rawBytes[9] & 0b11110000) >> 4;

                if ((prgRomLsb | (prgRomMsb << 8)) <= 16) {
                    prgRomSize = prgRomLsb * 0x4000;
                } else {
                    prgRomSize = prgRomLsb | (prgRomMsb << 8);
                }

                if ((chrRomLsb | (chrRomMsb << 8)) <= 16) {
                    chrRomSize = chrRomLsb * 0x2000;
                } else {
                    chrRomSize = chrRomLsb | (chrRomMsb << 8);
                }

                byte volatileShifts = rawBytes[10];
                int prgRamShift = volatileShifts & 0b1111;
                int prgNvRamShift = (volatileShifts & 0b1111) >> 4;
                if (prgRamShift != 0) {
                    PrgRam = new byte[64 << prgRamShift];
                }
                if (prgNvRamShift != 0) {
                    PrgNvRam = new byte[64 << prgNvRamShift];
                }

                byte volatileChrShifts = rawBytes[11];
                int chrRamShift = volatileChrShifts & 0b1111;
                int chrNvRamShift = (volatileChrShifts & 0b1111) >> 4;
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
                byte prgSize = rawBytes[4];
                byte chrSize = rawBytes[5];

                if (prgSize <= 16) {
                    prgRomSize = prgSize * 0x4000;
                } else {
                    prgRomSize = prgSize;
                }

                chrRomSize = chrSize * 0x2000;

                PrgRam = new byte[0x8000];
            }

            bool skip_trainer = (control1 & 0b100) != 0;

            var prgRomStart = 16;
            if (skip_trainer) {
                prgRomStart += 512;
            } else {
                prgRomStart += 0;
            }
            int chrRomStart = prgRomStart + prgRomSize;

            // Fills PRG with random data, which some games need to seed rng
            var rnd = new Random(Guid.NewGuid().GetHashCode());
            unsafe {
                fixed(byte* ram = PrgRam) {
                    for (var i = 0; i < PrgRam.Length; i++) {
                        *(ram + i) = (byte)rnd.Next(byte.MaxValue);
                    }
                }
            }

            Mirroring = mirror;
            PrgRom = rawBytes[prgRomStart..(prgRomStart + prgRomSize)];
            ChrRom = chrRomSize == 0 ? new byte[0x2000 * 16] : rawBytes[chrRomStart..(chrRomStart + chrRomSize)];
            Mapper = GetMapper(mapper);

            Mapper.RegisterRom(this);
        }

        private static IMapper GetMapper(byte mapperId) {
            return mapperId switch {
                0 => new NROM(),
                1 => new MMC1(),
                2 => new UxROM(),
                4 => new MMC3(),
                _ => throw new NotImplementedException($"Mapper {mapperId} not implemented"),
            };
        }
    }
}
