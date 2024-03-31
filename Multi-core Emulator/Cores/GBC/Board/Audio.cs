using System.Runtime.CompilerServices;

namespace MultiCoreEmulator.Cores.GBC;

internal partial class Board {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteAudio(ushort address, byte value) {

	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte ReadAudio(ushort address) {
		return 0;
	}
}
