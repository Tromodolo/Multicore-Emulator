**This emulator is only made for learning's sake. If you want a proper emulator, just use something else like Bizhawk or FCEUX or Higan**

*I will also not be taking pull request, due to this being by nature a very personal project.*

If you want to run this project, simply clone it and build it using `dotnet build`


# Implemented Mappers
- NROM (0)
- MMC1 (1)
- UxROM (2)
- CNROM (3)
- MMC3 (4)

# Known Problems
- Certain games in Mapper 3 (CNROM) fail to start or render properly. Unsure why

# To Do
- GBC Emulation Core
- Implement more mappers
- Creating self-written APU instead of using bizhawk's implementation.
