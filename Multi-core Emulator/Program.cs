global using ImGuiNET;
global using Veldrid;
global using static SDL2.SDL;

using MultiCoreEmulator.Cores;
using MultiCoreEmulator.Gui;
using SDL2;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace MultiCoreEmulator;

public static class Program {
    static SDL_AudioSpec SDLAudioSpec;

    static int SelectedAudioDevice;
    static List<string> AudioDevices;
    static int AudioDeviceId;

    static int SelectedController;
    static List<string> Controllers;
    static SDL_GameController ActiveController;

    static bool IsShiftPressed;

    static short[] AudioSamplesOut;
    static string CurrentFileName;

    static EmulatorCoreBase? EmuCore;

    static Sdl2Window SDL2Window;
    static Texture? Texture;

    static GraphicsDevice GraphicsDevice;
    static InputSnapshot InputSnapshot;
    static CommandList CommandList;
    static ImGuiRenderer ImGuiRenderer;

    static Stopwatch Stopwatch;


    public static void Main(string[] args) {
        InitImGui();
        InitSDL();

        Sdl2Events.Subscribe(ProcessSDLEvent);
        while (SDL2Window.Exists) {
            Sdl2Events.ProcessEvents();
            InputSnapshot = SDL2Window.PumpEvents();
            UpdateImGui();
        }
        Sdl2Events.Unsubscribe(ProcessSDLEvent);

        SDL_CloseAudioDevice((uint)AudioDeviceId);
    }

    private static void LoadFile(string fileName) {
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

    private static void InitSDL() {
        AudioDevices = new List<string>();
        Controllers = new List<string>();

        SDL_SetHint(SDL_HINT_GAMECONTROLLER_USE_BUTTON_LABELS, "0");
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
        SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "nearest");
        if (SDL_Init(SDL_INIT_AUDIO | SDL_INIT_GAMECONTROLLER) < 0) {
            Console.WriteLine($"There was an issue initializing  {SDL_GetError()}");
            return;
        }

        // Register first found controller and audio device
        for (var i = 0; i < SDL_NumJoysticks(); i++) {
            if (SDL_IsGameController(i) != SDL_bool.SDL_TRUE) {
                continue;
            }
            ActiveController = SDL_GameControllerOpen(i);
            break;
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

    private static void InitTextures() {
        if (Texture != null) {
            Texture.Dispose();
            Texture = null;
        }

        var windowWidth = EmuCore!.WindowWidth;
        var windowHeight = EmuCore!.WindowHeight;

        Texture = GraphicsDevice.ResourceFactory.CreateTexture(new TextureDescription {
            Depth = 1,
            Format = PixelFormat.R8_G8_B8_A8_UNorm,
            Height = (uint)windowHeight,
            Width = (uint)windowWidth,
            Type = TextureType.Texture2D,
            Usage = TextureUsage.Sampled,
            ArrayLayers = 1,
            MipLevels =  1,
            SampleCount = TextureSampleCount.Count1
        });
    }

    private static void ProcessSDLEvent(ref Veldrid.Sdl2.SDL_Event ev) {
        // This employs some EXTREME fuckery to cast
        // Veldrid.Sdl2 Events -> SDL2.Net events
        // This ONLY works because they have the same structure
        switch (ev.type) {
            case Veldrid.Sdl2.SDL_EventType.KeyDown: {
                var keyEvent = Unsafe.As<Veldrid.Sdl2.SDL_Event, SDL.SDL_KeyboardEvent>(ref ev);
                HandleKeyDown(EmuCore, keyEvent);
                break;
            }
            case Veldrid.Sdl2.SDL_EventType.KeyUp: {
                var keyEvent = Unsafe.As<Veldrid.Sdl2.SDL_Event, SDL.SDL_KeyboardEvent>(ref ev);
                HandleKeyUp(EmuCore, keyEvent);
                break;
            }
            case Veldrid.Sdl2.SDL_EventType.ControllerButtonDown: {
                var buttonEvent = Unsafe.As<Veldrid.Sdl2.SDL_Event, SDL.SDL_ControllerButtonEvent>(ref ev);
                HandleButtonDown(EmuCore, (SDL.SDL_GameControllerButton)buttonEvent.button);
                break;
            }
            case Veldrid.Sdl2.SDL_EventType.ControllerButtonUp: {
                var buttonEvent = Unsafe.As<Veldrid.Sdl2.SDL_Event, SDL.SDL_ControllerButtonEvent>(ref ev);
                HandleButtonUp(EmuCore, (SDL.SDL_GameControllerButton)buttonEvent.button);
                break;
            }
        }
    }

    private static void InitImGui() {
        // Used for deltatime
        Stopwatch = new Stopwatch();
        Stopwatch.Start();

        VeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(100, 100, 1370, 900, WindowState.Normal, "Emu :)"),
            out SDL2Window,
            out GraphicsDevice);

        ImGuiRenderer = new ImGuiRenderer(
            GraphicsDevice,
            GraphicsDevice.MainSwapchain.Framebuffer.OutputDescription,
            (int)GraphicsDevice.MainSwapchain.Framebuffer.Width,
            (int)GraphicsDevice.MainSwapchain.Framebuffer.Height);

        CommandList = GraphicsDevice.ResourceFactory.CreateCommandList();
    }

    private static void UpdateImGui() {
        const int gameWindowWidth = 768;
        const int gameWindowHeight = 720;

        const int gameWindowPadding = 32;

        const int gameWidth = gameWindowWidth - gameWindowPadding;
        const int gameHeight = gameWindowHeight - gameWindowPadding;

        var deltaTime = Stopwatch.Elapsed;
        Stopwatch.Reset();
        Stopwatch.Start();

        ImGuiRenderer.Update((float)deltaTime.TotalSeconds, InputSnapshot); // Compute actual value for deltaSeconds.

        ImGui.Begin("Info", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove);
        ImGui.SetWindowPos(Vector2.Zero);
        ImGui.SetWindowSize(new Vector2(300, gameWindowHeight));

        ImGui.Text("ImGui frame-time:");
        ImGui.SameLine();
        ImGui.Text(deltaTime.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        if (ImGuiUtils.FilePicker(out var fileName)) {
            LoadFile(fileName);
            InitTextures();
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
        if (ImGui.ListBox("Audio devices", ref SelectedAudioDevice, AudioDevices.ToArray(), AudioDevices.Count, 5)) {
            SDL_PauseAudioDevice((uint)AudioDeviceId, 1);
            SDL_CloseAudioDevice((uint)AudioDeviceId);

            AudioDeviceId = (int)SDL_OpenAudioDevice(AudioDevices[SelectedAudioDevice], 0, ref SDLAudioSpec, out var received, 0);

            if (AudioDeviceId == 0) {
                Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
                return;
            }

            // If the audio device is unpaused while there is no game running (and just sending empty buffers to the driver)
            // It causes the most unholy audio, so thats why this is here
            if (EmuCore != null) {
                SDL_PauseAudioDevice((uint)AudioDeviceId, 0);
            }
        }

        Controllers.Clear();
        for (var i = 0; i < SDL_NumJoysticks(); i++) {
            if (SDL_IsGameController(i) != SDL_bool.SDL_TRUE) {
                continue;
            }
            Controllers.Add(SDL_JoystickNameForIndex(i));
        }

        ImGui.NewLine();
        ImGui.Text("Controllers");
        ImGui.PushItemWidth(-1);
        if (ImGui.ListBox("Controllers", ref SelectedController, Controllers.ToArray(), Controllers.Count, 5)) {
            SDL_GameControllerClose(ActiveController.NativePointer);
            SDL_GameControllerOpen(SelectedController);
        }

        if (EmuCore != null) {
            ImGui.Begin("Game", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove);
            ImGui.SetWindowPos(new Vector2(300, 0));
            ImGui.SetWindowSize(new Vector2(gameWindowWidth, gameWindowHeight));

            var windowRatio = (float)EmuCore.WindowHeight / EmuCore.WindowWidth;

            int imageWidth;
            int imageHeight;
            if (windowRatio < 1) {
                imageWidth = gameWidth;
                imageHeight = (int)(imageWidth * windowRatio);
            } else {
                imageHeight = gameHeight;
                imageWidth = (int)(imageHeight * windowRatio);
            }

            ImGui.SetCursorPos(new Vector2(
                (ImGui.GetWindowSize().X - imageWidth) * 0.5f,
                (ImGui.GetWindowSize().Y - imageHeight) * 0.5f
            ));
            var imgPtr = ImGuiRenderer.GetOrCreateImGuiBinding(GraphicsDevice.ResourceFactory, Texture);
            ImGui.Image(imgPtr, new Vector2(imageWidth, imageHeight));

            ImGui.Begin("Debug Info", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove);
            ImGui.SetWindowPos(new Vector2(gameWindowWidth + 300, 0));
            ImGui.SetWindowSize(new Vector2(300, gameWindowHeight + 180));

            EmuCore.DrawDebugInfo();

            ImGui.Begin("Instruction Log", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove);
            ImGui.SetWindowPos(new Vector2(0, gameWindowHeight));
            ImGui.SetWindowSize(new Vector2(gameWindowWidth + 300, 180));

            EmuCore.DrawInstructionLog();
        }

        CommandList.Begin();
        CommandList.SetFramebuffer(GraphicsDevice.MainSwapchain.Framebuffer);
        CommandList.ClearColorTarget(0, RgbaFloat.Black);
        ImGuiRenderer.Render(GraphicsDevice, CommandList);
        CommandList.End();
        GraphicsDevice.SubmitCommands(CommandList);
        GraphicsDevice.SwapBuffers(GraphicsDevice.MainSwapchain);
    }

    private static void CoreAudioCallback(IntPtr userdata, IntPtr stream, int num) {
        unsafe {
            if (EmuCore == null) {
                return;
            }

            var streamPtr = (short*)stream;
            // Not sure why num is double the required amount here?
            var numSamples = num / 2;

            if (SDL2Window?.Exists ?? false) {
                EmuCore.ClockSamples(numSamples, ref GraphicsDevice, ref Texture);
                AudioSamplesOut = EmuCore.GetSamples(numSamples);
                for (var i = 0; i < numSamples; i++) {
                    streamPtr[i] = AudioSamplesOut[i];
                }
            }
        }
    }

    private static void HandleSaveState(EmulatorCoreBase core, int slot) {
        if (IsShiftPressed) {
            core.SaveState(slot);
        } else {
            core.LoadState(slot);
        }
    }

    // Key/button events used for global functions such as savestates or framecap
    private static void HandleKeyDown(EmulatorCoreBase? core, SDL.SDL_KeyboardEvent keyboardEvent) {
        switch (keyboardEvent.keysym.sym) {
            case SDL.SDL_Keycode.SDLK_F1:
                HandleSaveState(core, 1);
                break;
            case SDL.SDL_Keycode.SDLK_F2:
                HandleSaveState(core, 2);
                break;
            case SDL.SDL_Keycode.SDLK_F3:
                HandleSaveState(core, 3);
                break;
            case SDL.SDL_Keycode.SDLK_F4:
                HandleSaveState(core, 4);
                break;
            case SDL.SDL_Keycode.SDLK_F5:
                HandleSaveState(core, 5);
                break;
            case SDL.SDL_Keycode.SDLK_F6:
                HandleSaveState(core, 6);
                break;
            case SDL.SDL_Keycode.SDLK_F7:
                HandleSaveState(core, 7);
                break;
            case SDL.SDL_Keycode.SDLK_F8:
                HandleSaveState(core, 8);
                break;
            case SDL.SDL_Keycode.SDLK_LSHIFT:
                IsShiftPressed = true;
                break;
        }

        // Pass event down to the core level
        core?.HandleKeyDown(keyboardEvent);
    }

    private static void HandleKeyUp(EmulatorCoreBase? core, SDL.SDL_KeyboardEvent keyboardEvent) {
        // Pass event down to the core level
        core?.HandleKeyUp(keyboardEvent);
    }

    private static void HandleButtonDown(EmulatorCoreBase? core, SDL.SDL_GameControllerButton button) {
        switch (button) {
            case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE:
                core.Reset();
                break;
        }

        // Pass event down to the core level
        core?.HandleButtonDown(button);
    }

    private static void HandleButtonUp(EmulatorCoreBase? core, SDL.SDL_GameControllerButton button) {
        // Pass event down to the core level
        core?.HandleButtonUp(button);
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