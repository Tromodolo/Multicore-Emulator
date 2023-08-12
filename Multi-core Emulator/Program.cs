using MultiCoreEmulator.Cores;
using MultiCoreEmulator.Utility;
using System.Diagnostics;
using static SDL2.SDL;

namespace MultiCoreEmulator;

public static class Program {
    static string UsedAudioDevice;
    static SDL_AudioSpec SDLAudioSpec;
    static int AudioDeviceId;

    static ulong CurrentFrame = 0;
    static nint CoreWindow;
    static bool IsRunning;
    static bool IsFrameCap = true;
    static bool IsShiftPressed;
    static bool IsSaveStateHappening;

    static short[] AudioSamplesOut;
    static string CurrentFileName;

    public static void Main(string[] args) {
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
        var emulatorCore = GetApplicableEmulatorCore(fileName);
        CoreWindow = emulatorCore.InitializeWindow();
        emulatorCore.LoadBytes(fileName, fileByteArr);

        // Initialize SDL2
        SDL_SetHint(SDL_HINT_GAMECONTROLLER_USE_BUTTON_LABELS, "0");
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
        if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO | SDL_INIT_GAMECONTROLLER) < 0) {
            Console.WriteLine($"There was an issue initializing  {SDL_GetError()}");
            return;
        }
        
        SDL_SetWindowTitle(CoreWindow, $"Playing {CurrentFileName}");

        nint activeController;
        for (var i = 0; i < SDL_NumJoysticks(); i++) {
            if (SDL_IsGameController(i) != SDL_bool.SDL_TRUE) {
                continue;
            }
            activeController = SDL_GameControllerOpen(i);
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
        SDLAudioSpec.callback = (userdata, stream, num) => {
            unsafe {
                var streamPtr = (short*)stream;
                // Not sure why num is double the required amount here?
                var numSamples = num / 2;
                
                if (IsRunning && !IsSaveStateHappening) {
                    emulatorCore.ClockSamples(numSamples);
                    AudioSamplesOut = emulatorCore.GetSamples(numSamples);
                    for (var i = 0; i < numSamples; i++) {
                        streamPtr[i] = AudioSamplesOut[i];
                    }
                }
            }
        };
        
        AudioDeviceId = (int)SDL_OpenAudioDevice(UsedAudioDevice, 0, ref SDLAudioSpec, out var received, 0);
        if (AudioDeviceId == 0) {
            Console.WriteLine($"There was an issue opening the audio device. {SDL_GetError()}");
            return;
        }

        Console.WriteLine($"Audio Device Initialized: {UsedAudioDevice}");
        SDL_PauseAudioDevice((uint)AudioDeviceId, 0);

        IsRunning = true;
        
#if DEBUG
        // var debugWindow = new DebugWindow();
        // ThreadPool.QueueUserWorkItem((callback) => {
        //
        //     SDL_GetWindowPosition(CoreWindow, out int coreX, out int coreY);
        //     SDL_GetWindowSize(CoreWindow, out int coreWidth, out int _);
        //     SDL_SetWindowPosition(debugWindow.Window, coreX + coreWidth, coreY);
        //
        //     while (IsRunning) {
        //         emulatorCore.RenderDebugView(debugWindow);
        //         Thread.Sleep(50);
        //     }
        // });
#endif

        while (IsRunning) {
            while (SDL_PollEvent(out var e) == 1) {
                var key = e.key;
                switch (e.type) {
                    case SDL_EventType.SDL_KEYDOWN:
                        HandleKeyDown(emulatorCore, key);
                        break;
                    case SDL_EventType.SDL_KEYUP:
                        HandleKeyUp(emulatorCore, key);
                        break;
                    case SDL_EventType.SDL_CONTROLLERBUTTONDOWN:
                        var down = (SDL_GameControllerButton)e.cbutton.button;
                        HandleButtonDown(emulatorCore, down);
                        break;
                    case SDL_EventType.SDL_CONTROLLERBUTTONUP:
                        var up = (SDL_GameControllerButton)e.cbutton.button;
                        HandleButtonUp(emulatorCore, up);
                        break;
                    case SDL_EventType.SDL_QUIT:
                        IsRunning = false;
                        break;
                    case SDL_EventType.SDL_WINDOWEVENT:
#if DEBUG
                        // SDL_GetWindowPosition(CoreWindow, out int coreX, out int coreY);
                        // SDL_GetWindowSize(CoreWindow, out int coreWidth, out int _);
                        // SDL_SetWindowPosition(debugWindow.Window, coreX + coreWidth, coreY);
#endif
                        break;
                    default:
                        break;
                }
            }
        }

#if DEBUG
        // debugWindow.Close();
#endif
        emulatorCore.CloseWindow();
        SDL_CloseAudioDevice((uint)AudioDeviceId);
        Main(args);
    }

    private static void HandleSaveState(EmulatorCoreBase core, int slot) {
        IsSaveStateHappening = true;
        if (IsShiftPressed) {
            core.SaveState(slot);
        } else {
            core.LoadState(slot);
        }
        IsSaveStateHappening = false;
    }

    // Key/button events used for global functions such as savestates or framecap
    private static void HandleKeyDown(EmulatorCoreBase core, SDL_KeyboardEvent keyboardEvent) {
        switch (keyboardEvent.keysym.sym) {
            case SDL_Keycode.SDLK_F1:
                HandleSaveState(core, 1);
                break;
            case SDL_Keycode.SDLK_F2:
                HandleSaveState(core, 2);
                break;
            case SDL_Keycode.SDLK_F3:
                HandleSaveState(core, 3);
                break;
            case SDL_Keycode.SDLK_F4:
                HandleSaveState(core, 4);
                break;
            case SDL_Keycode.SDLK_F5:
                HandleSaveState(core, 5);
                break;
            case SDL_Keycode.SDLK_F6:
                HandleSaveState(core, 6);
                break;
            case SDL_Keycode.SDLK_F7:
                HandleSaveState(core, 7);
                break;
            case SDL_Keycode.SDLK_F8:
                HandleSaveState(core, 8);
                break;
            case SDL_Keycode.SDLK_TAB:
                IsFrameCap = false;
                break;
            case SDL_Keycode.SDLK_LSHIFT:
                IsShiftPressed = true;
                break;
        }

        // Pass event down to the core level
        core.HandleKeyDown(keyboardEvent);
    }

    private static void HandleKeyUp(EmulatorCoreBase core, SDL_KeyboardEvent keyboardEvent) {
        switch (keyboardEvent.keysym.sym) {
            case SDL_Keycode.SDLK_TAB:
                IsFrameCap = true;
                break;
            case SDL_Keycode.SDLK_ESCAPE:
                IsRunning = false;
                break;
            case SDL_Keycode.SDLK_LSHIFT:
                IsShiftPressed = false;
                break;
        }

        // Pass event down to the core level
        core.HandleKeyUp(keyboardEvent);
    }

    private static void HandleButtonDown(EmulatorCoreBase core, SDL_GameControllerButton button) {
        switch (button) {
            case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                IsFrameCap = false;
                break;
            case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE:
                core.Reset();
                break;
        }

        // Pass event down to the core level
        core.HandleButtonDown(button);
    }

    private static void HandleButtonUp(EmulatorCoreBase core, SDL_GameControllerButton button) {
        switch (button) {
            case SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                IsFrameCap = true;
                break;
        }

        // Pass event down to the core level
        core.HandleButtonUp(button);
    }

    private static EmulatorCoreBase GetApplicableEmulatorCore(string fileName) {
        if (fileName.EndsWith(".nez") || fileName.EndsWith(".nes")) {
            return new Cores.NES.Core();
        }
        return null;
    }
}