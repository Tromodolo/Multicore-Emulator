using BizHawk.NES;
using NesEmu.Bus;
using NesEmu.CPU;
using NesEmu.PPU;
using NesEmu.Rom;
using static SDL2.SDL;

namespace MultiCoreEmulator.Cores.NES {
    internal class Core : IEmulatorCore {
        PPU PPU;
        Bus Bus;
        APU APU;
        NesCpu CPU;
        Rom Rom;

        int SamplesPerFrame = (44100 / 60) + 1;

        ulong CurrentFrame = 0;

        nint Texture;
        nint Window;
        nint Renderer;

        public Core() {}

        public nint InitializeWindow() {
            // Create a new window given a title, size, and passes it a flag indicating it should be shown.
            Window = SDL_CreateWindow("Nes", SDL_WINDOWPOS_UNDEFINED, SDL_WINDOWPOS_UNDEFINED, 256 * 3, 240 * 3, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_SHOWN);

            if (Window == nint.Zero) {
                Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
                return nint.Zero;
            }

            // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
            nint renderer = SDL_CreateRenderer(Window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            if (renderer == nint.Zero) {
                Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
                return nint.Zero;
            }

            Texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 256, 240);
            Renderer = renderer;

            return Window;
        }

        public void CloseWindow() {
            // Clean up the resources that were created.
            SDL_DestroyRenderer(Renderer);
            SDL_DestroyWindow(Window);
            SDL_Quit();
        }

        public void LoadBytes(string fileName, byte[] bytes) {
            var rom = new Rom(bytes, fileName);

            CPU = new NesCpu();
            PPU = new PPU(rom.ChrRom);
            APU = new APU(CPU, null, false);
            Bus = new Bus(CPU, PPU, APU, rom);
            Rom = rom;

            CPU.RegisterBus(Bus);
            PPU.RegisterBus(Bus);
        }

        public void Reset() {
            APU.NESHardReset();
            CPU.Reset();
            Bus.Reset();
        }

        public bool Clock() {
            Bus.Clock();
            if (!Bus.GetDrawFrame())
                return false;

            CurrentFrame++;
            PPU.DrawFrame(ref Renderer, ref Texture);
            return true;
        }

        public short[] GetFrameSamples(out int numAvailable) {
            uint count = APU.sampleclock;
            Bus.Blip.EndFrame(count);
            APU.sampleclock = 0;

            var samples = new short[SamplesPerFrame];
            numAvailable = Bus.Blip.SamplesAvailable();
            int samplesSelected = Math.Min(numAvailable, SamplesPerFrame);
            Bus.Blip.ReadSamples(samples, samplesSelected, false);

            if (numAvailable != SamplesPerFrame) {
                samples = Resample(samples, samplesSelected, SamplesPerFrame);
            }

            return samples;
        }

        public void SaveState(int slot) {
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

        // Taken from BizHawk, their license should be in my license file - Tromo
        // This uses simple linear interpolation which is supposedly not a great idea for
        // resampling audio, but it sounds surprisingly good to me. Maybe it works well
        // because we are typically stretching by very small amounts.
        private static short[] Resample(short[] input, int inputCount, int outputCount) {
            if (inputCount == outputCount) {
                return input;
            }

            const int channels = 1;
            var output = new short[outputCount * channels];

            if (inputCount == 0 || outputCount == 0) {
                Array.Clear(output, 0, outputCount * channels);
                return output;
            }

            for (var iOutput = 0; iOutput < outputCount; iOutput++) {
                double iInput = ((double)iOutput / (outputCount - 1)) * (inputCount - 1);
                var iInput0 = (int)iInput;
                int iInput1 = iInput0 + 1;
                double input0Weight = iInput1 - iInput;
                double input1Weight = iInput - iInput0;

                if (iInput1 == inputCount)
                    iInput1 = inputCount - 1;

                for (var iChannel = 0; iChannel < channels; iChannel++) {
                    double value =
                        input[iInput0 * channels + iChannel] * input0Weight +
                        input[iInput1 * channels + iChannel] * input1Weight;

                    output[iOutput * channels + iChannel] = (short)((int)(value + 32768.5) - 32768);
                }
            }

            return output;
        }

        public void HandleKeyDown(SDL_KeyboardEvent keyboardEvent) {
            byte currentKeys = Bus.Controller1.GetAllButtons();

            switch (keyboardEvent.keysym.sym) {
                case SDL_Keycode.SDLK_r:
                    Reset();
                    break;
                case SDL_Keycode.SDLK_j:
                    currentKeys |= 0b10000000;
                    break;
                case SDL_Keycode.SDLK_k:
                    currentKeys |= 0b01000000;
                    break;
                case SDL_Keycode.SDLK_RSHIFT:
                    currentKeys |= 0b00100000;
                    break;
                case SDL_Keycode.SDLK_RETURN:
                    currentKeys |= 0b00010000;
                    break;
                case SDL_Keycode.SDLK_w:
                    currentKeys |= 0b00001000;
                    break;
                case SDL_Keycode.SDLK_s:
                    currentKeys |= 0b00000100;
                    break;
                case SDL_Keycode.SDLK_a:
                    currentKeys |= 0b00000010;
                    break;
                case SDL_Keycode.SDLK_d:
                    currentKeys |= 0b00000001;
                    break;
                default:
                    break;
            }

            Bus.Controller1.Update(currentKeys);
        }

        public void HandleKeyUp(SDL_KeyboardEvent keyboardEvent) {
            byte currentKeys = Bus.Controller1.GetAllButtons();

            switch (keyboardEvent.keysym.sym) {
                case SDL_Keycode.SDLK_j:
                    currentKeys &= 0b01111111;
                    break;
                case SDL_Keycode.SDLK_k:
                    currentKeys &= 0b10111111;
                    break;
                case SDL_Keycode.SDLK_RSHIFT:
                    currentKeys &= 0b11011111;
                    break;
                case SDL_Keycode.SDLK_RETURN:
                    currentKeys &= 0b11101111;
                    break;
                case SDL_Keycode.SDLK_w:
                    currentKeys &= 0b11110111;
                    break;
                case SDL_Keycode.SDLK_s:
                    currentKeys &= 0b11111011;
                    break;
                case SDL_Keycode.SDLK_a:
                    currentKeys &= 0b11111101;
                    break;
                case SDL_Keycode.SDLK_d:
                    currentKeys &= 0b11111110;
                    break;
                default:
                    break;
            }

            Bus.Controller1.Update(currentKeys);
        }

        public void HandleButtonDown(SDL_GameControllerButton button) {
            byte currentKeys = Bus.Controller1.GetAllButtons();

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
                default:
                    break;
            }

            Bus.Controller1.Update(currentKeys);
        }

        public void HandleButtonUp(SDL_GameControllerButton button) {
            byte currentKeys = Bus.Controller1.GetAllButtons();

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
                default:
                    break;
            }

            Bus.Controller1.Update(currentKeys);
        }
    }
}
