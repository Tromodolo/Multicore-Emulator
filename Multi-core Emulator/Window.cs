using MultiCoreEmulator.Cores;
using MultiCoreEmulator.Gui;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;

namespace MultiCoreEmulator
{
    public class Window : GameWindow
    {
        static SDL_AudioSpec SDLAudioSpec;

        static int SelectedAudioDevice;
        static List<string> AudioDevices;
        static int AudioDeviceId;

        static int SelectedController;
        static List<string> Controllers;
        static nint ActiveController;

        static bool IsShiftPressed;

        static short[] AudioSamplesOut;
        static string CurrentFileName;

        static EmulatorCoreBase? EmuCore;

        static nint SDL2Window;
        static nint? Texture;

        ImGuiController _controller;

        int textureID = -1;
        bool hasLoaded = false;

        bool shouldUpdateTexture;
        uint[] frameBuffer;
        int width;
        int height;

        public Window() : base(GameWindowSettings.Default, new NativeWindowSettings() {
            Size = new OpenTK.Mathematics.Vector2i(1600, 900),
            APIVersion = new Version(3, 3)
        }) {
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
        }

        public void ClearTexture() {
            GL.DeleteTexture(textureID);
            textureID = -1;
        }

        public void UpdateTexture(uint[] data, int width, int height) {
            bool isNewTexture = textureID == -1;

            if (isNewTexture)
                textureID = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, textureID);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            if (!isNewTexture) {
                GL.TexSubImage2D(TextureTarget.Texture2D,
                    0,
                    0,
                    0,
                    width,
                    height,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    data);
            } else {
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba8,
                    width,
                    height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    data);
            }
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            InitSDL();

            Title += ": OpenGL Version: " + GL.GetString(StringName.Version);
            _controller = new ImGuiController(ClientSize.X, ClientSize.Y);

            hasLoaded = true;
        }

        protected override void OnClosing(CancelEventArgs e) {
            SDL_CloseAudioDevice((uint)AudioDeviceId);

            base.OnClosing(e);
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            // Update the opengl viewport
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);

            // Tell ImGui of the new size
            _controller.WindowResized(ClientSize.X, ClientSize.Y);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            if (EmuCore != null && shouldUpdateTexture) {
                UpdateTexture(frameBuffer, EmuCore.WindowWidth, EmuCore.WindowHeight);
                shouldUpdateTexture = false;
            }

            _controller.Update(this, (float)e.Time);

            GL.ClearColor(new OpenTK.Mathematics.Color4(0, 32, 48, 255));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            // Enable Docking
            ImGui.DockSpaceOverViewport();

            DrawImGui((float)e.Time);

            _controller.Render();

            ImGuiController.CheckGLError("End of frame");

            SwapBuffers();
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            _controller.PressChar((char)e.Unicode);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            _controller.MouseScroll(e.Offset);
        }

        private void OnKeyUp(KeyboardKeyEventArgs obj) {
            EmuCore?.HandleKeyUp(obj);
        }
        private void OnKeyDown(KeyboardKeyEventArgs obj) {
            EmuCore?.HandleKeyDown(obj);
        }

        private void InitSDL() {
            AudioDevices = new List<string>();
            Controllers = new List<string>();

            SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "nearest");
            if (SDL_Init(SDL_INIT_AUDIO) < 0) {
                Console.WriteLine($"There was an issue initializing  {SDL_GetError()}");
                return;
            }

            SDLAudioSpec.channels = 1;
            SDLAudioSpec.freq = 44100;
            SDLAudioSpec.samples = 1024;
            SDLAudioSpec.format = AUDIO_S16LSB;
            SDLAudioSpec.callback = CoreAudioCallback;

            AudioDeviceId = (int)SDL_OpenAudioDevice(null, 0, ref SDLAudioSpec, out var received, 0);
            if (AudioDeviceId == 0) {
                Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
                return;
            }
        }

        private void DrawImGui(float deltaSeconds) {
            var viewport = ImGui.GetWindowViewport();

            ImGui.SetNextWindowSizeConstraints(new Vector2(300, 768), viewport.Size);
            ImGui.Begin("Info", ImGuiWindowFlags.None);

            ImGui.Text("ImGui frame-time:");
            ImGui.SameLine();
            ImGui.Text(deltaSeconds.ToString(CultureInfo.InvariantCulture));

            if (ImGuiUtils.FilePicker(out var fileName)) {
                LoadFile(fileName);
                ImGui.End();
                return;
            }

            int numAudioDevices = SDL_GetNumAudioDevices(0);
            AudioDevices.Clear();
            for (var i = 0; i < numAudioDevices; ++i) {
                AudioDevices.Add(SDL_GetAudioDeviceName(i, 0));
            }

            ImGui.NewLine();
            ImGui.Text("Audio devices");
            ImGui.PushItemWidth(-1);
            if (ImGui.ListBox("##audiodevices", ref SelectedAudioDevice, AudioDevices.ToArray(), AudioDevices.Count, 5)) {
                SDL_PauseAudioDevice((uint)AudioDeviceId, 1);
                SDL_CloseAudioDevice((uint)AudioDeviceId);

                AudioDeviceId = (int)SDL_OpenAudioDevice(AudioDevices[SelectedAudioDevice], 0, ref SDLAudioSpec, out var received, 0);

                if (AudioDeviceId == 0) {
                    Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
                    ImGui.End();
                    return;
                }

                // If the audio device is unpaused while there is no game running (and just sending empty buffers to the driver)
                // It causes the most unholy audio, so thats why this is here
                if (EmuCore != null) {
                    SDL_PauseAudioDevice((uint)AudioDeviceId, 0);
                }
            }

            // TODO: Re-add controller support
            // Controllers.Clear();
            // for (var i = 0; i < JoystickStates.Count; i++) {
            //     // Only support gamepads with dpad
            //     if (JoystickStates[i]?.HatCount > 0)
            //         Controllers.Add(JoystickStates[i].Name);
            // }

            // ImGui.NewLine();
            // ImGui.Text("Controllers");
            // ImGui.PushItemWidth(-1);
            // if (ImGui.ListBox("Controllers", ref SelectedController, Controllers.ToArray(), Controllers.Count, 5)) {
            //     SDL_GameControllerClose(ActiveController);
            //     SDL_GameControllerOpen(SelectedController);
            // }

            if (ImGuiUtils.SaveStates(out var isSave, out var slot)) {
                if (isSave) {
                    EmuCore?.SaveState(slot);
                } else {
                    EmuCore?.LoadState(slot);
                }
            }
            ImGui.End();

            if (EmuCore != null) {
                int gameWindowWidth = (EmuCore?.WindowWidth ?? 256) * 3;
                int gameWindowHeight = (EmuCore?.WindowHeight ?? 224) * 3;

                ImGui.SetNextWindowSizeConstraints(new Vector2(gameWindowWidth, gameWindowHeight), viewport.Size);
                ImGui.Begin("Game", ImGuiWindowFlags.None);

                var windowSize = ImGui.GetWindowSize();
                var gameSizeRatio = (float)gameWindowHeight / gameWindowWidth;

                var imageWidth = (int)(windowSize.X * 0.95);
                var imageHeight = (int)(imageWidth * gameSizeRatio);

                if (imageHeight > (windowSize.Y * 0.95)) {
                    gameSizeRatio = (float)gameWindowWidth / gameWindowHeight;

                    imageHeight = (int)(windowSize.Y * 0.95);
                    imageWidth = (int)(imageHeight * gameSizeRatio);
                }

                var imageSize = new Vector2(imageWidth, imageHeight);

                if (textureID >= 0) {
                    var remainingSpace = (windowSize - imageSize) / 2;
                    ImGui.SetCursorPos(remainingSpace);
                    ImGui.Image(new IntPtr(textureID), imageSize);
                }
                ImGui.End();
            }
        }

        private void LoadFile(string fileName) {
            byte[] fileByteArr;
            try {
                fileByteArr = File.ReadAllBytes(fileName);
                CurrentFileName = Path.GetFileName(fileName);
            } catch (Exception e) {
                throw new("Couldn't find file, try again");
            }

            EmuCore = null;

            // Initialize and clock game cores
            var core =  GetApplicableEmulatorCore(fileName);
            core.LoadBytes(fileName, fileByteArr);

            EmuCore = core;

            // Unpause audio, starting the audio thread
            SDL_PauseAudioDevice((uint)AudioDeviceId, 0);
        }

        private void CoreAudioCallback(IntPtr userdata, IntPtr stream, int num) {
            unsafe {
                if (EmuCore == null) {
                    return;
                }

                var streamPtr = (short*)stream;
                // Not sure why num is double the required amount here?
                var numSamples = num / 2;

                EmuCore.ClockSamples(numSamples, ref frameBuffer, ref shouldUpdateTexture);
                AudioSamplesOut = EmuCore.GetSamples(numSamples);
                for (var i = 0; i < numSamples; i++) {
                    streamPtr[i] = AudioSamplesOut[i];
                }
            }
        }

        private static EmulatorCoreBase GetApplicableEmulatorCore(string fileName) {
            if (fileName.EndsWith(".nez") || fileName.EndsWith(".nes")) {
                return new Cores.NES.Core();
            }
            if (fileName.EndsWith(".gbc") || fileName.EndsWith(".gb")) {
                return new Cores.GBC.Core();
            }
            return null;
        }
    }
}