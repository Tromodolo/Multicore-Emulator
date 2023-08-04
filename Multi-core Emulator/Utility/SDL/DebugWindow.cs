using static SDL2.SDL;

namespace MultiCoreEmulator.Utility.SDL;


public class DebugWindow {
    public nint Window;
    nint Texture;
    nint Renderer;

    const int WindowWidth = 512;
    const int WindowHeight = 680;
    const int WindowSizeMultiplier = 2;

    public uint[] FrameBuffer = new uint[WindowWidth * WindowHeight];

    public DebugWindow() {
        if (SDL_Init(SDL_INIT_VIDEO) < 0) {
            Console.WriteLine($"There was an issue initializing  {SDL_GetError()}");
            return;
        }

        // Create a new window given a title, size, and passes it a flag indicating it should be shown.
        Window = SDL_CreateWindow(
            "Debug",
            SDL_WINDOWPOS_UNDEFINED, SDL_WINDOWPOS_UNDEFINED,
            WindowWidth * WindowSizeMultiplier,
            WindowHeight * WindowSizeMultiplier,
            SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_SHOWN
        );

        if (Window == nint.Zero) {
            Console.WriteLine($"There was an issue creating the window. {SDL_GetError()}");
            return;
        }

        // Creates a new SDL hardware renderer using the default graphics device with VSYNC enabled.
        nint renderer = SDL_CreateRenderer(Window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

        if (renderer == nint.Zero) {
            Console.WriteLine($"There was an issue creating the renderer. {SDL_GetError()}");
            return;
        }

        Texture = SDL_CreateTexture(
            renderer,
            SDL_PIXELFORMAT_RGB888,
            (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
            WindowWidth,
            WindowHeight
        );
        Renderer = renderer;

        ThreadPool.QueueUserWorkItem((callback) => {
            while(true) {
                DrawFrame();
                Thread.Sleep(50);
            }
        });
    }

    public void Close() {
        SDL_DestroyRenderer(Renderer);
        SDL_DestroyWindow(Window);
    }

    public void DrawText(string text, int textX, int textY, Colors.ColorSpec color, bool clearBehind = true, int fontSize = 8) {
        text = text.ToUpper();

        int xOffset = 0;
        // The font is offset by 1 pixel,
        // possibly more if fontsize isnt 8
        var yOffset = fontSize / 8;
        textY += yOffset;

        foreach (char character in text) {
            // Fetch font character from array
            // it is stores as uint64, and every bit represents a pixel
            var fontCharacter = Font.Data[character];

            DrawData(fontCharacter, textX + xOffset, textY, color, clearBehind, fontSize);

            xOffset += fontSize;
        }
    }

    /// <summary>
    /// Takes in a ulong and iterates through the binary for the values to get sprite shape
    /// </summary>
    /// <param name="data"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="color"></param>
    /// <param name="clearBehind"></param>
    /// <param name="size"></param>
    private void DrawData(ulong data, int x, int y, Colors.ColorSpec color, bool clearBehind = true, int size = 8) {
        for (var yPos = 0; yPos < size; yPos++) {
            for (var xPos = 0; xPos < size; xPos++) {
                    // Don't forget to add positions + offset
                var renderAtX = x + xPos;
                var renderAtY = y + yPos;

                // In cases where data is not 8 pixels, translate the iterated
                // size and map it to a position in the original 8x8 sprite
                var translatedX = 8 * xPos / size;
                var translatedY = 8 * yPos / size;

                    // Get which pixel to render and then if it is 1, render it
                    // This was hellish to get to work without inverted text
                var bitPos = translatedY * 8 + translatedX;
                if (((data >>> (63 - bitPos)) & 1) == 1) {
                    SetPixel(renderAtX, renderAtY, color);
                    } else {
                    if (clearBehind) {
                        SetPixel(renderAtX, renderAtY, Colors.Black);
                    }
                }
            }
        }
    }

    public void SetPixel(int x, int y, Colors.ColorSpec color) {
        if (x < 0 || x > WindowWidth || y < 0 || y >= WindowHeight) {
            return;
        }
        if (color.Equals(Colors.Empty)) {
            return;
        }

        FrameBuffer[
            x +
            (y * WindowWidth)
        ] = (uint)((color.r << 16) | (color.g << 8 | (color.b << 0)));
    }

    private void ClearFrame() {
        unsafe {
            fixed (uint* pArray = FrameBuffer) {
                for (var i = 0; i < WindowWidth * WindowHeight; i++) {
                    *(pArray + i) = 0;
                }
            }
        }
    }

    private void DrawFrame() {
        unsafe {
            SDL_Rect rect;
            rect.w = WindowWidth * WindowSizeMultiplier;
            rect.h = WindowHeight * WindowSizeMultiplier;
            rect.x = 0;
            rect.y = 0;

            fixed (uint* pArray = FrameBuffer) {
                var intPtr = new nint(pArray);

                _ = SDL_UpdateTexture(Texture, ref rect, intPtr, WindowWidth * 4);
            }

            _ = SDL_RenderCopy(Renderer, Texture, nint.Zero, ref rect);
            SDL_RenderPresent(Renderer);
        }
    }
}
