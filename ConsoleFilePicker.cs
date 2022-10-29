using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesEmu {
    internal class ConsoleFilePicker {
        string[] FilteredFileTypes;
        string CurrentDirectory;

        List<string> CurrentFiles;
        string Selected;
        int CurrentIndex;
        int FileOffset;
        int ConsoleHeight;
        bool Redraw;

        internal ConsoleFilePicker(string[] fileTypes, string startDirectory) {
            FilteredFileTypes = fileTypes;
            CurrentDirectory = startDirectory;

            Selected = null;
            ConsoleHeight = Console.WindowHeight;
            Redraw = true;
        }

        internal string SelectFile() {
            PopulateDirs();

            while (Selected == null) {
                Render();
                var key = Console.ReadKey();
                switch (key.Key) {
                    case ConsoleKey.UpArrow:
                        if (CurrentIndex == FileOffset && FileOffset > 0) {
                            CurrentIndex--;
                            FileOffset--;
                            Redraw = true;
                        } else if (CurrentIndex > 0) {
                            CurrentIndex--;
                        }
                        break;
                    case ConsoleKey.DownArrow:
                        if ((CurrentIndex + 1) == (FileOffset - 1 + Console.WindowHeight) && CurrentIndex < (CurrentFiles.Count - 1)) {
                            CurrentIndex++;
                            FileOffset++;
                            Redraw = true;
                        } else if (CurrentIndex < Console.WindowHeight && CurrentIndex < (CurrentFiles.Count - 1)) {
                            CurrentIndex++;
                        }
                        break;
                    case ConsoleKey.LeftArrow:
                        CurrentDirectory = Path.Combine(CurrentDirectory, @"..");
                        CurrentDirectory = Path.GetFullPath(CurrentDirectory);

                        PopulateDirs();

                        CurrentIndex = 0;
                        Redraw = true;
                        break;
                    case ConsoleKey.Enter:
                    case ConsoleKey.RightArrow:
                        var selection = CurrentFiles[CurrentIndex];
                        if (selection == $@"{CurrentDirectory}\..") {
                            CurrentDirectory = Path.Combine(CurrentDirectory, @"..");
                            CurrentDirectory = Path.GetFullPath(CurrentDirectory);

                            PopulateDirs();

                            Redraw = true;
                        } else if (selection.EndsWith(@"\")) {
                            CurrentDirectory = Path.GetFullPath(selection);
                            if (CurrentDirectory.EndsWith(@"\")) {
                                CurrentDirectory = CurrentDirectory.Substring(0, CurrentDirectory.Length - 1);
                            }

                            PopulateDirs();

                            CurrentIndex = 0;
                            Redraw = true;
                        } else {
                            Selected = selection;
                        }
                        break;
                    default:
                        break;
                }
            }

            return Selected;
        }

        private void PopulateDirs() {
            var files = Directory.GetFiles(CurrentDirectory).ToList();
            var filteredFiles = new List<string>();

            foreach (var file in files) {
                foreach (var ft in FilteredFileTypes) {
                    if (file.EndsWith(ft)) {
                        filteredFiles.Add(file);
                    }
                }
            }

            CurrentFiles = filteredFiles;

            CurrentFiles.Insert(0, $@"{CurrentDirectory}\..");
            var folders = Directory.GetDirectories(CurrentDirectory);
            int folderIndex = 1;
            foreach (var folder in folders) {
                CurrentFiles.Insert(folderIndex, $@"{folder}\");
                folderIndex++;
            }
        }

        private void Render() {
            if (Redraw || ConsoleHeight != Console.WindowHeight) {
                Console.Clear();
                ConsoleHeight = Console.WindowHeight;
                Redraw = false;
            }

            Console.CursorLeft = 0;
            Console.CursorTop = 0;
            Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);

            var index = 0;
            var rendered = 1;
            string list = "";
            foreach (var file in CurrentFiles) {
                if (index >= FileOffset && rendered < Console.WindowHeight) {
                    if (index == CurrentIndex) {
                        list += "> ";
                    } else {
                        list += "- ";
                    }

                    list += file;
                    rendered++;
                    list += "\n";
                }

                index++;

                if (rendered >= Console.WindowHeight) {
                    break;
                }
            }

            Console.Write(list);
        }
    }
}
