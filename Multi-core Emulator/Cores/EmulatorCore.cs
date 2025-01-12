using OpenTK.Windowing.Common;

namespace MultiCoreEmulator.Cores {
    public interface EmulatorCoreBase {
        public int WindowWidth { get; }
        public int WindowHeight { get; }

        public void LoadBytes(string fileName, byte[] bytes);
        // , ref GraphicsDevice gd, ref Texture tex
        public void ClockSamples(int numAudioSamples, ref uint[] frameBuffer, ref bool frameDrawn);
        public void Reset();
        public short[] GetSamples(int numAudioSamples);
        public void SaveState(int slot);
        public void LoadState(int slot);

        public void HandleKeyDown(KeyboardKeyEventArgs keyboardEvent);
        public void HandleKeyUp(KeyboardKeyEventArgs keyboardEvent);
        //
        // public void HandleButtonDown(KeyboardKeyEventArgs button);
        // public void HandleButtonUp(KeyboardKeyEventArgs button);
    }
}
