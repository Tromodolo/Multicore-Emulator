using SDL2;
using System.Runtime.CompilerServices;
using static MultiCoreEmulator.Cores.GBC.Core;

namespace MultiCoreEmulator.Cores.GBC;

struct LCDControl {
	public byte Value;

	public bool Enabled;

	public ushort WindowTileMapArea;
	public bool WindowEnabled;

	public ushort BGWindowTileDataArea;
	public ushort BGTileMapArea;

	public byte ObjSize;
	public bool ObjEnabled;

	public bool BGWindowPriority;

	public LCDControl() {
		Value = 0;
		Enabled = true;

		WindowTileMapArea = 0x9800;
		WindowEnabled = false;

		BGWindowTileDataArea = 0x8800;
		BGTileMapArea = 0x9800;

		ObjSize = 8;
		ObjEnabled = false;

		BGWindowPriority = false;
	}
}

struct PixelData {
	public byte Color;				// a value between 0 and 3
	public byte Palette;			// on CGB a value between 0 and 7 and on DMG this only applies to objects
	public bool ObjectPriority;		// on CGB this is the OAM index for the object and on DMG this doesn’t exist
	public bool BackgroundPriority; // holds the value of the OBJ-to-BG Priority bit
}

[Flags]
enum STATInterrupt {
	None = 0,
	Mode0 = 0x08,
	Mode1 = 0x10,
	Mode2 = 0x20,
	LYC = 0x40
}

internal class Display {
	readonly uint[] frameBuffer = new uint[SCREEN_WIDTH * SCREEN_HEIGHT];

	// Registers
	LCDControl Control;

	// Status register in bit order 6-0
	/*  Bit 6 - LYC=LY STAT Interrupt source         (1=Enable) (Read/Write)
		Bit 5 - Mode 2 OAM STAT Interrupt source     (1=Enable) (Read/Write)
		Bit 4 - Mode 1 VBlank STAT Interrupt source  (1=Enable) (Read/Write)
		Bit 3 - Mode 0 HBlank STAT Interrupt source  (1=Enable) (Read/Write)
		Bit 2 - LYC=LY Flag                          (0=Different, 1=Equal) (Read Only)
		Bit 1-0 - Mode Flag                          (Mode 0-3, see below) (Read Only)
          0: HBlank
          1: VBlank
          2: Searching OAM
          3: Transferring Data to LCD Controller
	 */
	STATInterrupt EnabledSTATInterrupts;
	bool LYCEqual;

	// Internal rendering logic
	// LY = "Scanline"
	// LYC = If LY == LYC, trigger STAT if LYCStatInterruptEnabled
	internal byte LY;
	int LX;
	byte LYC;

	// Scroll
	byte SCY;
	byte SCX;

	// Window Position, top left corner
	byte WY;
	byte WX;

	internal int DotsDrawn = -56;
	internal int DotsDrawnTotal = 0;
	bool FirstLine;

	int RenderMode = 2;

	byte BGPalette;
	byte ObjectPalette1;
	byte ObjectPalette2;

	ushort CurrentTileAddr;
	byte CurrentTile;

	byte CurrentTileDataLo;
	byte CurrentTileDataHi;

	int FIFOState;
	Queue<PixelData> BGFIFO = new Queue<PixelData>(16);
	Queue<PixelData> ObjectFIFO = new Queue<PixelData>(16);

	public Display() {
		Control = new LCDControl();
	}

	public void Write(ushort address, byte value) {
		switch (address) {
			case 0xFF40:
				Control.Value = value;

				Control.Enabled = (value & 0x80) > 0;
				if (!Control.Enabled) {
					DotsDrawn = 0;
					LY = 0;
				}

				Control.WindowTileMapArea = (value & 0x40) > 0 ? (ushort)0x9C00 : (ushort)0x9800;
				Control.WindowEnabled = (value & 0x20) > 0;

				Control.BGWindowTileDataArea = (value & 0x10) > 0 ? (ushort)0x8000 : (ushort)0x8800;
				Control.BGTileMapArea = (value & 0x08) > 0 ? (ushort)0x9C00 : (ushort)0x9800;

				Control.ObjSize = (value & 0x04) > 0 ? (byte)16 : (byte)8;
				Control.ObjEnabled = (value & 0x02) > 0;

				Control.BGWindowPriority = (value & 0x01) > 0;
				break;
			case 0xFF41:
				EnabledSTATInterrupts = (STATInterrupt)(value & 0x78);
				break;
			case 0xFF42:
				SCY = value;
				break;
			case 0xFF43:
				SCX = value;
				break;
			case 0xFF4A:
				WY = value;
				break;
			case 0xFF4B:
				WX = value;
				break;
			case 0xFF47:
				BGPalette = value;
				break;
			case 0xFF48:
				ObjectPalette1 = value;
				break;
			case 0xFF49:
				ObjectPalette2 = value;
				break;
			case 0xFF44:
				// LCD Y, READ ONLY
				// NOP
				break;
			case 0xFF45:
				// LYC
				LYC = value;
				break;
		}

	}

	public byte Read(ushort address) {
		switch (address) {
			case 0xFF40:
				return Control.Value;
			case 0xFF41:
				return (byte)((int)EnabledSTATInterrupts | (LYCEqual ? 0x04 : 0) | RenderMode & 0x3);
			case 0xFF42:
				return SCY;
			case 0xFF43:
				return SCX;
			case 0xFF4A:
				return WY;
			case 0xFF4B:
				return (byte)(WX + 7);
			case 0xFF47:
				return BGPalette;
			case 0xFF48:
				return ObjectPalette1;
			case 0xFF49:
				return ObjectPalette2;
			case 0xFF44:
				// LCD Y, READ ONLY
				return LY;
			case 0xFF45:
				// LYC
				return LYC;
		}
		return 0;
	}


	/*
	 *Mode	Action															Duration	Accessible video memory
	2		Searching OAM for OBJs whose Y coordinate overlap this line		80 dots													VRAM, CGB palettes
	3		Reading OAM and VRAM to generate the picture					168 to 291 dots, depending on object count				None
	0		Nothing (HBlank)												85 to 208 dots, depending on previous mode 3 duration	VRAM, OAM, CGB palettes
	1		Nothing (VBlank)												4560 dots (10 scanlines)								VRAM, OAM, CGB palettes
	 *
	 */
	public void Clock(Board board) {
		if (!Control.Enabled) {
			return;
		}

		DotsDrawn++;
		DotsDrawnTotal++;

		switch (RenderMode) {
			case 0: {
				// Horizontal Blank
				// 87-204 dots, depending on whether Mode 3 is lengthened
				if (DotsDrawn == 456) {
					RenderMode = 2;
				}
				break;
			}
			case 1: {
				// VBlank, happens from scanlines 144 to 153
				// 4560 dots = 10 x 456
				var y = 2;
				break;
			}
			case 2: {
				// OAM Scan
				// 80 dots
				// OAM inaccessible (outside of DMA)
				if (DotsDrawn >= 79) {
					RenderMode = 3;
					// CurrentTileAddr++;

					BGFIFO.Clear();
					ObjectFIFO.Clear();
				}
				break;
			}
			case 3: {
				// Drawing pixels
				// 172-289 dots, depending on whether it is lengthened
				// VRAM inaccessible
				// CBG palettes inaccessible
				// OAM inaccessible (outside of DMA)
				// https://gbdev.io/pandocs/STAT.html#stat-modes

				// Goes through FIFO Pixel Fetcher
				/*
			 The fetcher fetches a row of 8 background or window pixels and queues them up to be mixed with object pixels.
			 The pixel fetcher has 5 steps.
			 The first four steps take 2 dots each and the fifth step is attempted every dot until it succeeds.
			 The order of the steps are as follows:
			    Get tile
			    Get tile data low
			    Get tile data high
			    Sleep
				Push */

				// if (!Control.Enabled) {
				// 	break;
				// }

				// - 80 because mode 3 starts 80 dots in
				bool isWindow = false;
				//
				// if (WX <= 166 && WY <= 143) { // Is Window Enabled
				// 	if (LY < WY && LX <= WX) { // Is current coordinate within window
				// 		isWindow = true;
				// 	}
				// }

				// Write two pixels to the screen
				// Only if FIFO is longer than 8 pixels
				if (BGFIFO.Count > 8) {
					var pixel = BGFIFO.Dequeue();
					if (LX < 160 && LY < 144) {
						frameBuffer[
							LX +
							LY * SCREEN_WIDTH
							] = Palette.Colors[pixel.Color];
						LX++;
					}
				}

				switch (FIFOState) {
					case 0: {
						// Get tile
						// if (isWindow) {
						// 	CurrentTileAddr = Control.WindowTileMapArea;
						// 	CurrentTileAddr |= (ushort)(WY / 8 << 5);
						// 	CurrentTileAddr |= (ushort)(LX / 8);
						// } else {
							CurrentTileAddr = Control.BGTileMapArea;
							CurrentTileAddr |= (ushort)((LX + SCX) / 8);
							CurrentTileAddr |= (ushort)((LY + SCY) / 8 << 5);
						// }

						CurrentTile = board.Read(CurrentTileAddr);
						break;
					}
					case 2: {
						// Get tile data lo
						ushort addr;
						ushort tile = (ushort)(CurrentTile * 2);
						if (Control.BGWindowTileDataArea == 0x8800) { // Use current tile as signed, ex: 0x9000 + -128
							addr = (ushort)(0x9000 + (sbyte)tile);
						} else { // use current tile as unsigned
							addr = (ushort)(Control.BGWindowTileDataArea + tile);
						}
						// if (Control.BGWindowTileDataArea == 0x8800 && (CurrentTile & 0x80) == 0) { // In 0x8800 mode, bit 12 is set to negation of bit 7 of tile id
						// 	addr |= 0b1000000000000;
						// }
						// if (isWindow) {
						// 	addr |= (ushort)(WY % 8 << 1);
						// } else {
							// addr |= (ushort)((LY + SCY) % 8 << 1);
						// }
						CurrentTileDataLo = board.Read(addr);
						break;
					}
					case 4: {
						// Get tile data hi
						ushort addr;
						ushort tile = (ushort)((CurrentTile * 2) + 1);
						if (Control.BGWindowTileDataArea == 0x8800) { // Use current tile as signed, ex: 0x8800 + -128
							addr = (ushort)(0x9000 + (sbyte)tile);
						} else { // use current tile as unsigned
							addr = (ushort)(Control.BGWindowTileDataArea + tile);
						}

						// var addr = Control.BGWindowTileDataArea;
						// addr |= (ushort)(CurrentTile << 4);
						// if (Control.BGWindowTileDataArea == 0x8800 && (CurrentTile & 0x80) == 0) { // In 0x8800 mode, bit 12 is set to negation of bit 7 of tile id
						// 	addr |= 0b1000000000000;
						// }
						// // if (isWindow) {
						// // 	addr |= (ushort)(WY % 8 << 1);
						// // } else {
						// 	addr |= (ushort)((LY + SCY) % 8 << 1);
						// // }
						CurrentTileDataHi = board.Read(addr);
						break;
					}
					case 6:
						// Sleep, do nothing
						break;
					case >= 8: {
						// Push current tile to FIFO
						// Only if FIFOs have less than or equal to 8 pixels
						if (BGFIFO.Count <= 8) {
							// push here
							for (int i = 0; i < 8; i++) {
								var tile = ((CurrentTileDataHi & 1) << 1) | CurrentTileDataLo & 1;
								PixelData data = new PixelData {
									Color = (byte)tile
								};
								BGFIFO.Enqueue(data);

								CurrentTileDataHi >>= 1;
								CurrentTileDataLo >>= 1;

							}
							FIFOState = -1;
						}
						break;
					}
				}
				FIFOState++;

				break;
			}
		}

		if (DotsDrawn == 456) {
			DotsDrawn = 0;
			if (!FirstLine) {
				LY++;
			}
			FirstLine = false;
		}

		switch (LY) {
			case 143:
				// Entering VBlank, set interrupt
				RenderMode = 1;
				board.TriggerInterrupt(InterruptType.VBlank);
				TriggerSTAT(board, STATInterrupt.Mode1);
				break;
			case 153:
				LY = 0;
				RenderMode = 2;
				FirstLine = true;
				TriggerSTAT(board, STATInterrupt.Mode2);
				break;
		}

		switch (LX) {
			case 160:
				LX = 0;
				RenderMode = 0;
				TriggerSTAT(board, STATInterrupt.Mode0);
				break;
		}

		if (!LYCEqual && (LY == LYC)) {
			TriggerSTAT(board, STATInterrupt.LYC);
		}

		LYCEqual = LY == LYC;
	}

	void TriggerSTAT(Board board, STATInterrupt interrupt) {
		if ((EnabledSTATInterrupts & interrupt) == interrupt) {
			board.TriggerInterrupt(InterruptType.LCD);
		}
	}

	public void Draw(ref nint renderer, ref nint texture) {
		unsafe {
			SDL.SDL_Rect rect;
			rect.w = SCREEN_WIDTH * SCREEN_MULTIPLIER;
			rect.h = SCREEN_HEIGHT * SCREEN_MULTIPLIER;
			rect.x = 0;
			rect.y = 0;

			fixed (uint* pArray = frameBuffer) {
				var intPtr = new nint(pArray);

				_ = SDL.SDL_UpdateTexture(texture, ref rect, intPtr, SCREEN_WIDTH * 4);
			}

			_ = SDL.SDL_RenderCopy(renderer, texture, nint.Zero, ref rect);
			SDL.SDL_RenderPresent(renderer);
		}
	}
}
