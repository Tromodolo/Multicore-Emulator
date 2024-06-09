using MultiCoreEmulator.Cores.GBC.Cartridge.Mapper;
using System.Diagnostics;
using System.Text;

namespace MultiCoreEmulator.Cores.GBC {
    internal class GameCart {
        public string GameTitle;
        public string ManufacturerCode;
        public string NewLicenseeCode;

        public int OldLicenseeCode;
        public int CartridgeType;

        public int Region;
        public int VersionNumber;

        // Mapper related functions
        public bool SupportsRam;
        public bool SupportsBattery;
        public bool SupportsTimer;
        public bool SupportsRumble;

        public bool SupportsSGB;
        public bool ColorSupport;

        public int RomBanks = 2;
        public int RomSize = 0x8000;

        public int RamBanks = 0;
        public int RamSize = 0;

        public Memory<byte> RAM;
        public Memory<byte> ROM;

        private IMapper Mapper;

        public GameCart(Span<byte> rawBytes) {
            GameTitle = Encoding.ASCII.GetString(rawBytes[0x134..0x0143]).Replace("\0", " ");

            ManufacturerCode = Encoding.ASCII.GetString(rawBytes[0x013F..0x0142]).Replace("\0", " ");

            ColorSupport = rawBytes[0x0143] == 0x80 || rawBytes[0x0143] == 0xC0;

            NewLicenseeCode = Encoding.ASCII.GetString(rawBytes[0x0144..0x0145]).Replace("\0", " ");

            SupportsSGB = rawBytes[0x0146] == 0x03;

            CartridgeType = rawBytes[0x0147];

            switch (CartridgeType) {
                case 0: // Base
                    Mapper = new BaseMapper();
                    break;
                case 8: // Base + RAM
                    Mapper = new BaseMapper();
                    SupportsRam = true;
                    break;
                case 9: // Base + Ram + Battery
                    Mapper = new BaseMapper();
                    SupportsRam = true;
                    SupportsBattery = true;
                    break;
                // default:
                //     throw new NotImplementedException();
            }

            RomSize = 1 << rawBytes[0x0148];
            RomSize *= 0x8000;
            RomBanks = RomSize / 0x4000;

            if (SupportsRam) {
                switch (rawBytes[0x0149]) {
                    case 0:
                    case 1:
                        break;
                    case 2:
                        RamBanks = 1;
                        break;
                    case 3:
                        RamBanks = 4;
                        break;
                    case 4:
                        RamBanks = 16;
                        break;
                    case 5:
                        RamBanks = 8;
                        break;
                }

                RamSize *= 0x2000;
            }

            Region = rawBytes[0x014A];
            OldLicenseeCode = rawBytes[0x014B];
            VersionNumber = rawBytes[0x014C];

            Mapper = new BaseMapper();

            RAM = new Memory<byte>(new byte[RamSize]);
            if (rawBytes.Length != RomSize) {
                Debug.Assert(false, "File size does not match rom size in memory");
            }
            ROM = new Memory<byte>(rawBytes.ToArray());
        }

        public bool Read(ushort address, out byte value) {
            return Mapper.Read(ref RAM, ref ROM, address, out value);
        }

        public bool Write(ushort address, byte value) {
            return Mapper.Write(ref RAM, ref ROM, address, value);
        }
    }
}
