using System.Runtime.CompilerServices;

namespace MultiCoreEmulator.Cores.GBC;

internal partial class CPU {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void StackWriteByte(Board board, byte value) {
		board.Write(SP--, value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal byte StackReadByte(Board board) {
		return board.Read(++SP);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void StackWriteUShort(Board board, ushort value) {
		// big endian, save lsb first and then msb
		board.Write(--SP, (byte)(value >> 8));
		board.Write(--SP, (byte)(value & 0xFF));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ushort StackReadUShort(Board board) {
		// big endian, because lsb was saved first above, get it last
		var lsb = board.Read(SP++);
		var msb = board.Read(SP++);
		return (ushort)((msb << 8) | lsb);
	}
}
