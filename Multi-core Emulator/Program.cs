global using ImGuiNET;
global using Veldrid;
global using static SDL2.SDL;

using MultiCoreEmulator.Cores;
using MultiCoreEmulator.Utility;
using SDL2;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace MultiCoreEmulator;

public static class Program {
    static string UsedAudioDevice;
    static SDL_AudioSpec SDLAudioSpec;
    static int AudioDeviceId;
    static SDL_GameController ActiveController;

    static bool IsShiftPressed;

    static short[] AudioSamplesOut;
    static string CurrentFileName;

    static EmulatorCoreBase EmuCore;

    static Sdl2Window SDL2Window;
    static Texture Texture;

    static GraphicsDevice GraphicsDevice;
    static InputSnapshot InputSnapshot;
    static CommandList CommandList;
    static ImGuiRenderer ImGuiRenderer;

    public static void Main(string[] args) {
        LoadFile();
        InitImGui();
        InitSDL();
        InitTextures();

        Sdl2Events.Subscribe(ProcessSDLEvent);
        while (SDL2Window.Exists) {
            Sdl2Events.ProcessEvents();
            InputSnapshot = SDL2Window.PumpEvents();
            UpdateImGui();
        }
        Sdl2Events.Unsubscribe(ProcessSDLEvent);

        SDL_CloseAudioDevice((uint)AudioDeviceId);
    }

    private static void LoadFile() {
        var picker = new ConsoleFilePicker(new[] {
            ".nes", ".nez", ".gbc", ".gb"
        }, Directory.GetCurrentDirectory());
        string fileName = picker.OpenSelector();

        byte[] fileByteArr;
        try {
            fileByteArr = File.ReadAllBytes(fileName);
            CurrentFileName = Path.GetFileName(fileName);
        } catch (Exception e) {
            throw new("Couldn't find file, try again");
        }

        // Initialize and clock game cores
        EmuCore = GetApplicableEmulatorCore(fileName);
        EmuCore.LoadBytes(fileName, fileByteArr);
    }

    private static void InitSDL() {
        SDL_SetHint(SDL_HINT_GAMECONTROLLER_USE_BUTTON_LABELS, "0");
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
        SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "nearest");
        if (SDL_Init(SDL_INIT_AUDIO | SDL_INIT_GAMECONTROLLER) < 0) {
            Console.WriteLine($"There was an issue initializing  {SDL_GetError()}");
            return;
        }

        // Use Veldrid here to handle gamepad through that instead of ourselves
        // This makes sure it comes up in ProcessSDLEvent
        for (var i = 0; i < SDL_NumJoysticks(); i++) {
            if (SDL_IsGameController(i) != SDL_bool.SDL_TRUE) {
                continue;
            }
            ActiveController = SDL_GameControllerOpen(i);
            break;
        }

        UsedAudioDevice = "";
        if (File.Exists("audio_device.conf")) {
            UsedAudioDevice = File.ReadAllText("audio_device.conf");
        }

        if (string.IsNullOrEmpty(UsedAudioDevice)) {
            //Console.CursorTop++;
            Console.Clear();

            var devices = new List<string>();
            int count = SDL_GetNumAudioDevices(0);
            for (var i = 0; i < count; ++i) {
                devices.Add(SDL_GetAudioDeviceName(i, 0));
            }

            Console.WriteLine($"Select your audio device: ");

            var marked = 0;
            int selected = -1;
            int initialRow = Console.CursorTop;
            while (selected < 0) {
                Console.CursorTop = initialRow;
                var index = 0;
                foreach (string dev in devices) {
                    if (index == marked) {
                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write($"> {dev}\n");
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.ForegroundColor = ConsoleColor.White;
                    } else {
                        Console.Write($"  {dev}\n");
                    }
                    index++;
                }

                var nextKey = Console.ReadKey();
                switch (nextKey.Key) {
                    case ConsoleKey.DownArrow when marked == devices.Count - 1:
                        continue;
                    case ConsoleKey.DownArrow:
                        marked++;
                        break;
                    case ConsoleKey.UpArrow when marked == 0:
                        continue;
                    case ConsoleKey.UpArrow:
                        marked--;
                        break;
                    case ConsoleKey.Enter:
                        selected = marked;
                        break;
                    default:
                        break;
                }
            }
            UsedAudioDevice = devices[selected];
            File.WriteAllText("audio_device.conf", UsedAudioDevice);
        }

        SDLAudioSpec.channels = 1;
        SDLAudioSpec.freq = 44100;
        SDLAudioSpec.samples = 1024;
        SDLAudioSpec.format = AUDIO_S16LSB;
        SDLAudioSpec.callback = CoreAudioCallback;

        AudioDeviceId = (int)SDL_OpenAudioDevice(UsedAudioDevice, 0, ref SDLAudioSpec, out var received, 0);
        if (AudioDeviceId == 0) {
            Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
            return;
        }

        Console.WriteLine($"Audio Device Initialized: {UsedAudioDevice}");
        SDL_PauseAudioDevice((uint)AudioDeviceId, 0);
    }

    private static void InitTextures() {
        var windowWidth = EmuCore.WindowWidth;
        var windowHeight = EmuCore.WindowHeight;

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

        ImGuiRenderer.Update(1f / 60f, InputSnapshot); // Compute actual value for deltaSeconds.

        ImGui.Begin("Info", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove);
        ImGui.SetWindowPos(Vector2.Zero);
        ImGui.SetWindowSize(new Vector2(300, gameWindowHeight));
        ImGui.Button("Load file");

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
    private static void HandleKeyDown(EmulatorCoreBase core, SDL.SDL_KeyboardEvent keyboardEvent) {
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
        core.HandleKeyDown(keyboardEvent);
    }

    private static void HandleKeyUp(EmulatorCoreBase core, SDL.SDL_KeyboardEvent keyboardEvent) {
        // Pass event down to the core level
        core.HandleKeyUp(keyboardEvent);
    }

    private static void HandleButtonDown(EmulatorCoreBase core, SDL.SDL_GameControllerButton button) {
        switch (button) {
            case SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE:
                core.Reset();
                break;
        }

        // Pass event down to the core level
        core.HandleButtonDown(button);
    }

    private static void HandleButtonUp(EmulatorCoreBase core, SDL.SDL_GameControllerButton button) {
        // Pass event down to the core level
        core.HandleButtonUp(button);
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