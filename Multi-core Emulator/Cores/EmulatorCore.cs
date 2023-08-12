using MultiCoreEmulator.Utility.SDL;
using static SDL2.SDL;

namespace MultiCoreEmulator.Cores {
    public interface EmulatorCoreBase {
        public nint InitializeWindow(string windowName = "Game", int windowWidth = 256, int windowHeight = 240);
        public void CloseWindow();
        public void LoadBytes(string fileName, byte[] bytes);
        public void ClockSamples(int numAudioSamples);
        public void Reset();
        public short[] GetSamples(int numAudioSamples);
        public void SaveState(int slot);
        public void LoadState(int slot);

        public void HandleKeyDown(SDL_KeyboardEvent keyboardEvent);
        public void HandleKeyUp(SDL_KeyboardEvent keyboardEvent);

        public void HandleButtonDown(SDL_GameControllerButton button);
        public void HandleButtonUp(SDL_GameControllerButton button);

        public void RenderDebugView(DebugWindow debugWindow);
    }
}
