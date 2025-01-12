global using ImGuiNET;
global using static SDL2.SDL;

namespace MultiCoreEmulator;

public static class Program {
    public static void Main(string[] args) {
        using var window = new Window();
        window.Run();
    }
}