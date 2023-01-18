using static SDL2.SDL;

namespace MultiCoreEmulator.Cores {
    public abstract class EmulatorCoreBase {
        public nint Texture;
        public nint Window;
        public nint Renderer;

        public virtual nint InitializeWindow(string windowName = "Game", int windowWidth = 256, int windowHeight = 240) {
            // Create a new window given a title, size, and passes it a flag indicating it should be shown.
            Window = SDL_CreateWindow(windowName, SDL_WINDOWPOS_UNDEFINED, SDL_WINDOWPOS_UNDEFINED, windowWidth * 3, windowHeight * 3, SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_SHOWN);

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

            Texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, windowWidth, windowHeight);
            Renderer = renderer;

            return Window;
        }

        public virtual void CloseWindow() {
            // Clean up the resources that were created.
            SDL_DestroyRenderer(Renderer);
            SDL_DestroyWindow(Window);
            SDL_Quit();
        }

        public abstract void LoadBytes(string fileName, byte[] bytes);
        public abstract bool Clock();
        public abstract void Reset();
        public abstract short[] GetFrameSamples(out int numAvailable);
        public abstract void SaveState(int slot);
        public abstract void LoadState(int slot);

        public abstract void HandleKeyDown(SDL_KeyboardEvent keyboardEvent);
        public abstract void HandleKeyUp(SDL_KeyboardEvent keyboardEvent);

        public abstract void HandleButtonDown(SDL_GameControllerButton button);
        public abstract void HandleButtonUp(SDL_GameControllerButton button);
    }
}
