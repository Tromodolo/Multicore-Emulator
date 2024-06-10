using System.Numerics;

namespace MultiCoreEmulator.Gui;

public static class ImGuiUtils {
	static string CurrentFilePickerDirectory = Directory.GetCurrentDirectory();
	static int CurrentSelectedFile = -1;
	static List<string> CurrentOptions = new List<string>();

	public static bool FilePicker(out string filename) {
		filename = "";

		ImGui.NewLine();
		ImGui.Text("Select file");

		var drives = DriveInfo.GetDrives();
		foreach (var drive in drives) {
			if (ImGui.Button(drive.RootDirectory.FullName)) {
				CurrentFilePickerDirectory = drive.RootDirectory.FullName;
				CurrentOptions.Clear();
				return false;
			}
			ImGui.SameLine();
		}

		if (CurrentOptions.Count == 0) {
			var options = new List<string>();
			options.Insert(0, "..");

			var directories = Directory.GetDirectories(CurrentFilePickerDirectory)
				.Select(x => x[(x.LastIndexOf(Path.DirectorySeparatorChar) + 1)..] + Path.DirectorySeparatorChar)
				.Order()
				.ToList();

			var files = Directory.GetFiles(CurrentFilePickerDirectory)
				.Where(x => x.EndsWith(".gb") || x.EndsWith(".gbc") || x.EndsWith(".nes") || x.EndsWith(".nez"))
				.Select(x => x[(x.LastIndexOf(Path.DirectorySeparatorChar) + 1)..])
				.Order()
				.ToList();

			options.AddRange(directories);
			options.AddRange(files);
			CurrentOptions = options;
		}

		ImGui.NewLine();
		ImGui.PushItemWidth(-1);
		if (!ImGui.ListBox("File picker", ref CurrentSelectedFile, CurrentOptions.ToArray(), CurrentOptions.Count, 20)) {
			return false;
		}

		var selection = CurrentOptions[CurrentSelectedFile];
		if (selection.Equals("..")) {
			CurrentFilePickerDirectory = Path.Join(CurrentFilePickerDirectory, ".." + Path.DirectorySeparatorChar);
			CurrentOptions.Clear();
			return false;
		}

		selection = Path.Join(CurrentFilePickerDirectory, selection);
		if (Directory.Exists(selection)) {
			filename = "";
			CurrentFilePickerDirectory = selection;
			CurrentOptions.Clear();
			return false;
		}

		filename = selection;
		return true;
	}
}
