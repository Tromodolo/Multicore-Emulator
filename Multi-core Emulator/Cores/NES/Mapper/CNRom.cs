using NesEmu.Rom;

namespace NesEmu.Mapper; 

public class CNRom : IMapper {
	Rom.Rom CurrentRom;
	Bus.Bus Bus;

	const ushort BANK_SIZE = 0x2000;

	byte CurrentBank;
	byte[] PrgRom;
	byte[] ChrRom;

	bool SmallerPrgRom;
	
	public void RegisterRom(Rom.Rom rom) {
		CurrentRom = rom;
		ChrRom = rom.ChrRom;
		PrgRom = rom.PrgRom;
		SmallerPrgRom = PrgRom.Length == 0x4000;
		CurrentBank = 0;
	}

	public void RegisterBus(Bus.Bus bus) {
		Bus = bus;
	}
	
	public ScreenMirroring GetMirroring() {
		return CurrentRom.Mirroring;
	}
	
	public byte CpuRead(ushort address, out bool handled) {
		handled = false;
		
		if (address >= 0x8000) {
			handled = true;
			address -= 0x8000;
			
			if (SmallerPrgRom && address >= 0x4000) {
				address -= 0x4000;
			}
			
			return PrgRom[address];
		}

		return 0;
	}
	
	public void CpuWrite(ushort address, byte value, out bool handled) {
		handled = false;
		
		if (address >= 0x8000 && address < 0xFFFF) {
			handled = true;
			
			// bus conflict
			// value &= Bus.ReadPrgRom(address);
			
			CurrentBank = (byte)(value & 0b11);
		}
	}
	
	public byte PPURead(ushort address, out bool handled) {
		handled = false;

		if (address < 0x2000) {
			handled = true;
			return ChrRom[(CurrentBank * BANK_SIZE) + address];
		}
		
		return 0;
	}
	
	public void PPUWrite(ushort address, byte value, out bool handled) {
		handled = false;
		
		if (address < 0x2000) {
			handled = true;
			ChrRom[(CurrentBank * BANK_SIZE) + address] = value;
		}
	}
}
