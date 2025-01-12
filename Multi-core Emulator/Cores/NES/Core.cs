using BizHawk.NES;
using NesEmu.Bus;
using NesEmu.CPU;
using NesEmu.PPU;
using NesEmu.Rom;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using static NesEmu.PPU.PPU;

namespace MultiCoreEmulator.Cores.NES {
    internal class Core : EmulatorCoreBase {
        PPU PPU;
        Bus Bus;
        APU APU;
        NesCpu CPU;
        Rom Rom;

        short[]? samplesOut;

        bool isSaveStateHappening;
        bool isLoadStateHappening;

        int stateSlot;
        
        bool isFastForward;

        public int WindowHeight => SCREEN_HEIGHT - SCREEN_OFFSET_TOP - SCREEN_OFFSET_BOTTOM;

        public int WindowWidth => SCREEN_WIDTH;

        public void LoadBytes(string fileName, byte[] bytes) {
            Rom = new Rom(bytes, fileName);

            CPU = new NesCpu();
            PPU = new PPU(Rom.ChrRom);
            APU = new APU(CPU, null, false);
            Bus = new Bus(CPU, PPU, APU, Rom);

            CPU.RegisterBus(Bus);
            PPU.RegisterBus(Bus);
        }

        public void Reset() {
            APU.NESHardReset();
            CPU.Reset();
            Bus.Reset();
        }

        //, ref GraphicsDevice gd, ref Texture tex
        public void ClockSamples(int numAudioSamples, ref uint[] frameBuffer, ref bool frameDrawn) {
            while (Bus.SamplesCollected < numAudioSamples) {
                if (isSaveStateHappening) {
                    InternalSaveState(stateSlot);
                    isSaveStateHappening = false;
                    continue;
                }
                if (isLoadStateHappening) {
                    InternalLoadState(stateSlot);
                    isLoadStateHappening = false;
                    continue;
                }

                Bus.Clock();

                if (isFastForward) {
                    Bus.SamplesCollected = 0;
                }
                
                if (Bus.PendingFrame) {
                    PPU.GetFrameBuffer(out frameBuffer);
                    Bus.PendingFrame = false;
                    frameDrawn = true;
                }
            }

            Bus.SamplesCollected -= numAudioSamples;
        }

        public short[] GetSamples(int numAudioSamples) {
            if (samplesOut == null || samplesOut.Length != numAudioSamples) {
                samplesOut = new short[numAudioSamples];
            }
            
            Bus.AudioBuffer.FillBuffer(ref samplesOut);
            return samplesOut;
        }

        public void SaveState(int slot) {
            isSaveStateHappening = true;
            stateSlot = slot;
        }

        private void InternalSaveState(int slot) {
            string gameName = Rom.Filename.Split('\\').LastOrDefault();
            gameName = gameName.Replace(".nes", "");
            gameName = gameName.Replace(".nez", "");
            var stateFileName = $"{gameName}.{slot}.state";

            var fileStream = new FileStream(stateFileName, FileMode.OpenOrCreate);
            var binaryWriter = new BinaryWriter(fileStream);
            
            CPU.Save(binaryWriter);
            PPU.Save(binaryWriter);
            Bus.Save(binaryWriter);
            
            fileStream.Close();
        }

        public void LoadState(int slot) {
            isLoadStateHappening = true;
            stateSlot = slot;
        }

        private void InternalLoadState(int slot) {
            string gameName = Rom.Filename.Split('\\').LastOrDefault();
            gameName = gameName.Replace(".nes", "");
            gameName = gameName.Replace(".nez", "");
            var stateFileName = $"{gameName}.{slot}.state";

            if (!File.Exists(stateFileName))
                return;
            var fileStream = new FileStream(stateFileName, FileMode.Open);
            var binaryReader = new BinaryReader(fileStream);
            
            CPU.Load(binaryReader);
            PPU.Load(binaryReader);
            Bus.Load(binaryReader);
            
            fileStream.Close();
        }

        public void HandleKeyDown(KeyboardKeyEventArgs keyboardEvent) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (keyboardEvent.Key) {
                case Keys.R:
                    Reset();
                    break;
                case Keys.J:
                    currentKeys |= 0b10000000;
                    break;
                case Keys.K:
                    currentKeys |= 0b01000000;
                    break;
                case Keys.RightShift:
                    currentKeys |= 0b00100000;
                    break;
                case Keys.Enter:
                    currentKeys |= 0b00010000;
                    break;
                case Keys.W:
                    currentKeys |= 0b00001000;
                    break;
                case Keys.S:
                    currentKeys |= 0b00000100;
                    break;
                case Keys.A:
                    currentKeys |= 0b00000010;
                    break;
                case Keys.D:
                    currentKeys |= 0b00000001;
                    break;
                case Keys.Tab:
                    isFastForward = true;
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void HandleKeyUp(KeyboardKeyEventArgs keyboardEvent) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (keyboardEvent.Key) {
                case Keys.J:
                    currentKeys &= 0b01111111;
                    break;
                case Keys.K:
                    currentKeys &= 0b10111111;
                    break;
                case Keys.RightShift:
                    currentKeys &= 0b11011111;
                    break;
                case Keys.Enter:
                    currentKeys &= 0b11101111;
                    break;
                case Keys.W:
                    currentKeys &= 0b11110111;
                    break;
                case Keys.S:
                    currentKeys &= 0b11111011;
                    break;
                case Keys.A:
                    currentKeys &= 0b11111101;
                    break;
                case Keys.D:
                    currentKeys &= 0b11111110;
                    break;
                case Keys.Tab:
                    isFastForward = false;
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void HandleButtonDown(SDL_GameControllerButton button) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (button) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B:
                    currentKeys |= 0b10000000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A:
                    currentKeys |= 0b01000000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK:
                    currentKeys |= 0b00100000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START:
                    currentKeys |= 0b00010000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP:
                    currentKeys |= 0b00001000;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN:
                    currentKeys |= 0b00000100;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT:
                    currentKeys |= 0b00000010;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT:
                    currentKeys |= 0b00000001;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    isFastForward = true;
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }

        public void HandleButtonUp(SDL_GameControllerButton button) {
            byte currentKeys = Bus.GetAllButtonState();

            switch (button) {
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B:
                    currentKeys &= 0b01111111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A:
                    currentKeys &= 0b10111111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK:
                    currentKeys &= 0b11011111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START:
                    currentKeys &= 0b11101111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP:
                    currentKeys &= 0b11110111;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN:
                    currentKeys &= 0b11111011;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT:
                    currentKeys &= 0b11111101;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT:
                    currentKeys &= 0b11111110;
                    break;
                case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    isFastForward = false;
                    break;
            }

            Bus.UpdateControllerState(currentKeys);
        }
    }
}
