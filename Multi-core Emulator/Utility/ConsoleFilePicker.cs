namespace MultiCoreEmulator.Utility; 

internal class ConsoleFilePicker {
	string currentDirectory;
	string[] allowedFileTypes;

	string? selectedFile;
	string filter;

	List<string> allOptions;
	List<string> options;
	
	int optionIndexOffset;
	int currIndex;
	int currentRenderHeight;
	
	internal ConsoleFilePicker(string[] fileTypes, string startDirectory) {
		allowedFileTypes = fileTypes;
		currentDirectory = startDirectory;
		allOptions = new List<string>();
		options = new List<string>();
		selectedFile = null;
		filter = "";
	}

	internal string OpenSelector() {
		Console.Clear();

		currIndex = 0;
		optionIndexOffset = 0;
		
		ReadCurrentDirectory();
		RenderView();

		while (selectedFile == null) {
			ReadInput();
			FilterOptions();
			RenderView();
		}
		
		return selectedFile;
	}

	private void ReadInput() {
		var key = Console.ReadKey(true);
		switch (key.Key) {
			case ConsoleKey.UpArrow:
				if (currIndex == 0 && optionIndexOffset > 0) {
					optionIndexOffset--;
				} 
				if (currIndex > 0) {
					currIndex--;
				}
				break;
			case ConsoleKey.DownArrow:
				if (currIndex == currentRenderHeight - 1 && optionIndexOffset < options.Count - currentRenderHeight) {
					optionIndexOffset++;
				}
				if (currIndex < currentRenderHeight - 1 && currIndex < options.Count - 1) {
					currIndex++;
				} 
				break;
			case ConsoleKey.RightArrow:
			case ConsoleKey.Enter:
				// Can only happen when searching
				if (options.Count == 0) {
					break;
				}
				
				var pressed = options[currIndex + optionIndexOffset];
				pressed = pressed.Trim();
				if (pressed.EndsWith(Path.DirectorySeparatorChar)) {
					currentDirectory = Path.GetFullPath(
						Path.Join(currentDirectory, pressed)
					);
					
					Console.Clear();
					ReadCurrentDirectory();
					optionIndexOffset = 0;
					currIndex = 0;
				} else {
					selectedFile = Path.GetFullPath(
						Path.Join(currentDirectory, pressed)
					);
				}
				break;
			case ConsoleKey.Backspace:
				if (filter.Length > 0) {
					filter = filter.Remove(filter.Length - 1);
					optionIndexOffset = 0;
					currIndex = 0;
					Console.Clear();
				}
				break;
			default:
				if (char.IsLetterOrDigit(key.KeyChar) || key.KeyChar.Equals('.') || 
				    key.KeyChar.Equals(' ') || key.KeyChar.Equals('_')) {
					filter += key.KeyChar;
					optionIndexOffset = 0;
					currIndex = 0;
					Console.Clear();
				}
				break;
		}
	}

	private void FilterOptions() {
		if (filter.Length > 0) {
			options = allOptions
				.Where(x => x.Contains(filter))
				.ToList();
		} else {
			options = allOptions.ToList();
		}
	}
	
	private void RenderView() {
		Console.CursorLeft = 0;
		Console.CursorTop = 0;
		
		Console.Write("Current Directory: {0}\n", currentDirectory);
		Console.Write("Filter: {0}\n", filter.PadRight(Console.WindowWidth - 100));
		Console.CursorTop += 1;

		currentRenderHeight = Console.WindowHeight - Console.CursorTop - 1;
		
		for (var i = 0; i < options.Count && i < currentRenderHeight; i++) {
			if (i == currIndex) {
				Console.Write("*");
			} else {
				Console.Write(' ');
			}
			
			Console.Write("{0}\n", options[i + optionIndexOffset]);
		}
	}

	private void ReadCurrentDirectory() {
		allOptions.Clear();

		if (Directory.GetParent(currentDirectory) != null) {
			var back = ".." + Path.DirectorySeparatorChar;
			allOptions.Add(back.PadRight(Console.WindowWidth - 5));
		}
		
		allOptions.AddRange(
			Directory.EnumerateDirectories(currentDirectory)
				.Select(x =>
					(x.Replace(currentDirectory, "") + Path.DirectorySeparatorChar)
					.PadRight(Console.WindowWidth - 5)
				)
		);
		allOptions.AddRange(
			Directory.EnumerateFiles(currentDirectory)
				.Select(x =>
					x.Replace(currentDirectory, "")
						.PadRight(Console.WindowWidth - 5)
				)
				.Where(x => allowedFileTypes.Length == 0 || allowedFileTypes.Any(x.Trim().EndsWith))
		);

		options = allOptions.ToList();
	}
}