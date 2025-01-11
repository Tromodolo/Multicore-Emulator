namespace MultiCoreEmulator.Cores {
    public interface EmulatorCoreBase {
        public int WindowWidth { get; }
        public int WindowHeight { get; }

        public void LoadBytes(string fileName, byte[] bytes);
        public void ClockSamples(int numAudioSamples, ref GraphicsDevice gd, ref Texture tex);
        public void Reset();
        public short[] GetSamples(int numAudioSamples);
        public void SaveState(int slot);
        public void LoadState(int slot);

        public void HandleKeyDown(SDL_KeyboardEvent keyboardEvent);
        public void HandleKeyUp(SDL_KeyboardEvent keyboardEvent);

        public void HandleButtonDown(SDL_GameControllerButton button);
        public void HandleButtonUp(SDL_GameControllerButton button);
    }
}
