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
        const ushort PrgRomSize = 0x4000;
        const ushort ChrRomSize = 0x2000;

        public byte PrgRomBankCount;
        public byte ChrRomBankCount;

        public byte[] PrgRom;
        public byte[] ChrRom;
        public ScreenMirroring Mirroring;
        public IMapper Mapper;

        public Rom(byte[] rawBytes) {
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

            var iNesVersion = (control2 >> 2) & 0b11;
            if (iNesVersion != 0) {
                throw new NotImplementedException("NES 2.0 format is not yet supported");
            }

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

            PrgRomBankCount = rawBytes[4];
            ChrRomBankCount = rawBytes[5];

            var prgRomSize = rawBytes[4] * PrgRomSize;
            var chrRomSize = rawBytes[5] * ChrRomSize;

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
            ChrRom = chrRomSize == 0 ? new byte[ChrRomSize * 16] : rawBytes[chrRomStart..(chrRomStart + chrRomSize)];
            Mapper = GetMapper(mapper);

            Mapper.RegisterRom(this);
        }

        private IMapper GetMapper(byte mapperId) {
            return mapperId switch {
                0 => new NROM(),
                _ => null,
            };
        }
    }
}
