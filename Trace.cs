using NesEmu.CPU;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu {
    public static class Trace {
        public static string Log(NesCpu cpu) {
            var opcode = cpu.MemRead(cpu.ProgramCounter);
            var op = OpCodeList.OpCodes[opcode];
            if (op == null) {
                return "";
            }

            List<byte> hexDump = new List<byte>();
            hexDump.Add(op.Code);

            StringBuilder sb = new StringBuilder();
            sb.Append($"{cpu.ProgramCounter:X4}  ");

            ushort memAddr, storedValue;
            switch (op.Mode) {
                case AddressingMode.Immediate:
                case AddressingMode.NoneAddressing:
                case AddressingMode.Accumulator:
                case AddressingMode.Relative:
                case AddressingMode.Indirect:
                    memAddr = 0;
                    storedValue = 0;
                    break;
                default:
                    memAddr = cpu.GetAbsoluteAdddress(op.Mode, (ushort)(cpu.ProgramCounter + 1)).programCounter;
                    storedValue = cpu.MemRead(memAddr);
                    break;
            }

            string param = "";
            switch (op.NumBytes) {
                case 1:
                    if (op.Name is "ASL_ACC" or "LSR_ACC" or "ROL_ACC" or "ROR_ACC") {
                        param = "A ";
                    }
                    break;
                case 2: {
                    var address = cpu.MemRead((ushort)(cpu.ProgramCounter + 1));
                    hexDump.Add(address);

                    switch (op.Mode) {
                        case AddressingMode.Immediate:
                            param = $"#${address:X2}";
                            break;
                        case AddressingMode.ZeroPage:
                            param = $"${address:X2} = {storedValue:X2}";
                            break;
                        case AddressingMode.ZeroPageX:
                            param = $"${address:X2},X @ {memAddr:X2} = {storedValue:X2}";
                            break;
                        case AddressingMode.ZeroPageY:
                            param = $"${address:X2},Y @ {memAddr:X2} = {storedValue:X2}";
                            break;
                        case AddressingMode.IndirectX:
                            byte ad = address;
                            ad += cpu.RegisterX;
                            param = $"(${address:X2},X) @ {ad:X2} = {memAddr:X4} = {storedValue:X2}";
                            break;
                        case AddressingMode.IndirectY:
                            ushort mAd = memAddr;
                            mAd -= cpu.RegisterY;
                            param = $"(${address:X2}),Y = {mAd:X4} @ {memAddr:X4} = {storedValue:X2}";
                            break;
                        default:
                            var updatedAddress = (ushort)(cpu.ProgramCounter + 2 + (sbyte)address);
                            param = $"${updatedAddress:X4}";
                            break;
                    }
                    break;
                }
                case 3: {
                    var loValue = cpu.MemRead((ushort)(cpu.ProgramCounter + 1));
                    var hiValue = cpu.MemRead((ushort)(cpu.ProgramCounter + 2));
                    var address = cpu.MemReadShort((ushort)(cpu.ProgramCounter + 1));
                    hexDump.Add(loValue);
                    hexDump.Add(hiValue);

                    switch (op.Mode) {
                        case AddressingMode.NoneAddressing:
                        case AddressingMode.Indirect: {

                            if (op.Code == 0x6c) {
                                ushort jmpAddr;
                                if ((address & 0x00ff) == 0x00ff) {
                                    var lo = cpu.MemRead(address);
                                    var hi = cpu.MemRead((ushort)(address & 0xff00));
                                    jmpAddr = (ushort)((hi << 8) | lo);
                                } else {
                                    jmpAddr = cpu.MemReadShort(address);
                                }

                                param = $"(${address:X4}) = {jmpAddr:X4}";
                            } else {
                                param = $"${address:X4}";
                            }
                            break;
                        }
                        case AddressingMode.Absolute: {
                            if (op.Code is 0x20 or 0x4c) {
                                param = $"${memAddr:X4}";
                            } else {
                                param = $"${memAddr:X4} = {storedValue:X2}";
                            }
                            break;
                        }
                        case AddressingMode.AbsoluteX: {
                            param = $"${address:X4},X @ {memAddr:X4} = {storedValue:X2}";
                            break;
                        }
                        case AddressingMode.AbsoluteY: {
                            param = $"${address:X4},Y @ {memAddr:X4} = {storedValue:X2}";
                            break;
                        }
                        case AddressingMode.IndirectX: {
                            param = $"(${address:X4},X) @ {address + cpu.RegisterX:X2} = {memAddr:X4} = {storedValue:X2}";
                            break;
                        }
                        default: {
                            param = $"{address:X4}";
                            break;
                        }
                    }

                    break;
                }
            }

            foreach (var b in hexDump) {
                sb.Append(b.ToString("X2"));
            }

            // This is a really stupid way of making sure stuff lines up but I cannot be bothered atm
            var diff = 3 - hexDump.Count;
            for (var i = 1; i <= diff; i++) {
                sb.Append("   ");
            }

            if (op.Name.EndsWith("_ACC")) {
                sb.Append($" {op.Name.Substring(0, 3).PadLeft(4)} {param.PadRight(28)}");
            } else {
                sb.Append($" {op.Name.PadLeft(4)} {param.PadRight(28)}");
            }

            // Registers
            sb.Append($"A:{cpu.Accumulator:X2} X:{cpu.RegisterX:X2} Y:{cpu.RegisterY:X2} P:{(int)cpu.Status:X2} SP:{cpu.StackPointer:X2}");

            //sb.Append($" PPU:{cpu.Bus.PPU.CurrentScanline.ToString().PadLeft(3, ' ')},{cpu.Bus.PPU.CurrentCycle.ToString().PadLeft(3, ' ')} CYC:{cpu.Bus.PPU.TotalCycles}\r\n");

            var trace = sb.ToString();
            return trace;
        }
    }
}
