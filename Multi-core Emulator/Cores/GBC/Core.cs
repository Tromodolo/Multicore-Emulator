namespace MultiCoreEmulator.Cores.GBC {
    internal class Core : EmulatorCoreBase {
        public const int SCREEN_WIDTH = 160;
        public const int SCREEN_HEIGHT = 144;
        public const int SCREEN_MULTIPLIER = 4; 
        
        Board board;

        short[]? samplesOut;

        public int WindowWidth => SCREEN_WIDTH;
        public int WindowHeight => SCREEN_HEIGHT;

        public void LoadBytes(string fileName, byte[] bytes) {
            board = new Board(fileName, bytes);
        }

        //, ref GraphicsDevice gd, ref Texture tex
        public void ClockSamples(int numAudioSamples, ref uint[] frameBuffer, ref bool frameDrawn) {
            // for (int i = 0; i < numAudioSamples; i++) {
            for (int i = 0; i < 70224; i++) {
                board.Clock();
            }
            // }
            // board.Draw(ref gd, ref tex);
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
        
        public void HandleKeyDown(SDL_KeyboardEvent keyboardEvent) {}
        
        public void HandleKeyUp(SDL_KeyboardEvent keyboardEvent) {}
        
        public void HandleButtonDown(SDL_GameControllerButton button) {}
        
        public void HandleButtonUp(SDL_GameControllerButton button) {}
    }
}
