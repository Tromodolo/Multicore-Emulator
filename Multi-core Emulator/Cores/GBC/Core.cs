using MultiCoreEmulator.Utility.SDL;
using SDL2;
using static SDL2.SDL;

namespace MultiCoreEmulator.Cores.GBC {
    internal class Core : EmulatorCoreBase {
        public const int SCREEN_WIDTH = 160;
        public const int SCREEN_HEIGHT = 144;
        public const int SCREEN_MULTIPLIER = 4; 
        
        nint texture;
        nint window;
        nint renderer;
        
        Board board;

        short[]? samplesOut;

        public nint InitializeWindow() {
            string windowName = "NES";
            int windowWidth = SCREEN_WIDTH;
            int windowHeight = SCREEN_HEIGHT;
            
            // Create a new window given a title, size, and passes it a flag indicating it should be shown.
            window = SDL_CreateWindow(
                windowName, 
                SDL_WINDOWPOS_UNDEFINED, 
                SDL_WINDOWPOS_UNDEFINED,
                windowWidth * SCREEN_MULTIPLIER, 
                windowHeight * SCREEN_MULTIPLIER,
                SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_SHOWN
            );

            if (window == nint.Zero) {
                Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
                return nint.Zero;
            }

            // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
            nint renderer = SDL_CreateRenderer(
                window, 
                -1,
                SDL_RendererFlags.SDL_RENDERER_ACCELERATED
            );

            if (renderer == nint.Zero) {
                Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
                return nint.Zero;
            }

            texture = SDL_CreateTexture(
                renderer,
                SDL_PIXELFORMAT_RGB888,
                (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, 
                windowWidth, 
                windowHeight
            );
            
            this.renderer = renderer;

            return window;
        }

        public void CloseWindow() {
            // Clean up the resources that were created.
            SDL_DestroyRenderer(renderer);
            SDL_DestroyWindow(window);
            SDL_Quit();
        }
        
        public void LoadBytes(string fileName, byte[] bytes) {
            board = new Board(fileName, bytes);
        }

        public void ClockSamples(int numAudioSamples) {
            // for (int i = 0; i < numAudioSamples; i++) {
            for (int i = 0; i < 70224; i++) {
                board.Clock();
            }
            // }
            board.Draw(ref renderer, ref texture);
        }
        
        public void Reset() {}
        
        public short[] GetSamples(int numAudioSamples) {
            if (samplesOut == null || samplesOut.Length != numAudioSamples) {
                samplesOut = new short[numAudioSamples];
            }
            
            board.AudioBuffer.FillBuffer(ref samplesOut);
            return samplesOut;
        }
        
        public void SaveState(int slot) {}
        
        public void LoadState(int slot) {}
        
        public void HandleKeyDown(SDL.SDL_KeyboardEvent keyboardEvent) {}
        
        public void HandleKeyUp(SDL.SDL_KeyboardEvent keyboardEvent) {}
        
        public void HandleButtonDown(SDL.SDL_GameControllerButton button) {}
        
        public void HandleButtonUp(SDL.SDL_GameControllerButton button) {}
        
        public void RenderDebugView(DebugWindow debugWindow) {}
    }
}
