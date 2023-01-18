using static SDL2.SDL;

namespace MultiCoreEmulator.Cores {
    public interface IEmulatorCore {
        public nint InitializeWindow();
        public void CloseWindow();
        public void LoadBytes(string fileName, byte[] bytes);
        public bool Clock();
        public void Reset();
        public short[] GetFrameSamples(out int numAvailable);
        public void SaveState(int slot);
        public void LoadState(int slot);

        public void HandleKeyDown(SDL_KeyboardEvent keyboardEvent);
        public void HandleKeyUp(SDL_KeyboardEvent keyboardEvent);

        public void HandleButtonDown(SDL_GameControllerButton button);
        public void HandleButtonUp(SDL_GameControllerButton button);
    }
}
