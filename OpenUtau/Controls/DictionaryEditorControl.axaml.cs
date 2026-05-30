using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;
using System.Diagnostics;

namespace OpenUtau.App.Controls {
    public partial class DictionaryEditorControl : UserControl {
        public DictionaryEditorViewModel ViewModel { get; } = new DictionaryEditorViewModel();

        public static readonly StyledProperty<UVoicePart?> PartProperty =
            AvaloniaProperty.Register<DictionaryEditorControl, UVoicePart?>(nameof(Part));

        public UVoicePart? Part {
            get => GetValue(PartProperty);
            set => SetValue(PartProperty, value);
        }
        public DictionaryEditorControl() {
            InitializeComponent();

            ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ViewModel.SelectedCategory)) {
                    RebuildGridColumns(ViewModel.SelectedCategory);
                    
                    if (ViewModel.SelectedCategory != null && ViewModel.SelectedCategory.Columns.Count > 0) {
                        ViewModel.ReplaceColumn = ViewModel.SelectedCategory.Columns[0];
                    }
                }
            };
            
            ViewModel.ColumnsChanged += () => {
                RebuildGridColumns(ViewModel.SelectedCategory);
                
                if (ViewModel.SelectedCategory != null && ViewModel.SelectedCategory.Columns.Count > 0) {
                    if (string.IsNullOrEmpty(ViewModel.ReplaceColumn) || !ViewModel.SelectedCategory.Columns.Contains(ViewModel.ReplaceColumn)) {
                        ViewModel.ReplaceColumn = ViewModel.SelectedCategory.Columns[0];
                    }
                } else {
                    ViewModel.ReplaceColumn = null;
                }
            };

            this.Loaded += (s, e) => LoadDictionaryForPart(Part);
        }

        private void CommentGrid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
            if (sender is Grid grid && grid.DataContext is DynamicYamlRow row) {
                var gridControl = this.FindControl<DataGrid>("EditorGrid");
                if (gridControl == null) return;

                var point = e.GetCurrentPoint(grid);

                if (point.Properties.IsRightButtonPressed) {
                    if (!gridControl.SelectedItems.Contains(row)) {
                        gridControl.SelectedItem = row;
                    }
                    return; 
                }

                var modifiers = e.KeyModifiers;
                
                if (modifiers.HasFlag(KeyModifiers.Control)) {
                    if (gridControl.SelectedItems.Contains(row)) gridControl.SelectedItems.Remove(row);
                    else gridControl.SelectedItems.Add(row);
                } 
                else if (modifiers.HasFlag(KeyModifiers.Shift)) {
                    var lastSelected = gridControl.SelectedItem as DynamicYamlRow;
                    int startIndex = ViewModel.SelectedCategory?.Rows.IndexOf(lastSelected ?? row) ?? 0;
                    int endIndex = ViewModel.SelectedCategory?.Rows.IndexOf(row) ?? 0;
                    
                    gridControl.SelectedItems.Clear();
                    int min = Math.Min(startIndex, endIndex);
                    int max = Math.Max(startIndex, endIndex);
                    for (int i = min; i <= max; i++) {
                        if (ViewModel.SelectedCategory?.Rows.Count > i) {
                            gridControl.SelectedItems.Add(ViewModel.SelectedCategory.Rows[i]);
                        }
                    }
                } 
                else {
                    gridControl.SelectedItem = row;
                }
            }
        }

        private void CommentGrid_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) {
            if (sender is Grid grid && grid.DataContext is DynamicYamlRow row) {
                ViewModel.SelectedRow = row;
                row.IsEditingComment = true; 
                
                if (row.CommentText == "# New Comment..." || row.CommentText == "# New comment...") {
                    row.CommentText = "# ";
                }
                
                Dispatcher.UIThread.Post(() => {
                    var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (textBox != null) {
                        textBox.Focus();
                        textBox.CaretIndex = textBox.Text?.Length ?? 0;
                    }
                }, DispatcherPriority.Normal);
            }
        }

        private void CommentTextBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
            if (sender is TextBox tb && tb.DataContext is DynamicYamlRow row) {
                row.IsEditingComment = false; 
            }
        }

        private void CommentTextBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e) {
            if (e.Key == Avalonia.Input.Key.Enter || e.Key == Avalonia.Input.Key.Escape) {
                if (sender is TextBox tb && tb.DataContext is DynamicYamlRow row) {
                    row.IsEditingComment = false; 
                }
                e.Handled = true; 
            }
        }

        private void CommentTextBox_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) {
            if (sender is TextBox tb && tb.DataContext is DynamicYamlRow row) {
                row.CommentText = tb.Text ?? "";
            }
        }

        private void EditorGrid_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e) {
            if (EditorGrid.SelectedItem != null) {
                EditorGrid.ScrollIntoView(EditorGrid.SelectedItem, null);
            }
        }
        private void EditorGrid_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
            // Intentionally blank to allow right click
        }
        
        protected override void OnDataContextChanged(EventArgs e) {
            base.OnDataContextChanged(e);
            
            if (this.DataContext is DictionaryEditorViewModel vm) {
                vm.RefreshIndices = () => {
                    Dispatcher.UIThread.Post(() => {
                        var rows = EditorGrid.GetVisualDescendants().OfType<Avalonia.Controls.DataGridRow>();
                        foreach (var row in rows) {
                            row.Header = (row.Index + 1).ToString();
                        }
                    }, DispatcherPriority.Background);
                };
            }
        }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == PartProperty) {
                Log.Information("DictionaryEditor: PartProperty changed in UI.");
                LoadDictionaryForPart((UVoicePart?)change.NewValue);
            }
        }
        private void RebuildGridColumns(YamlCategory? category) {
            var grid = this.FindControl<DataGrid>("EditorGrid");
            if (grid == null) return;

            var currentData = grid.ItemsSource;
            grid.ItemsSource = null;

            grid.Columns.Clear();
            if (category != null) {
                foreach (var colName in category.Columns) {
                    var column = new DataGridTextColumn {
                        Header = colName,
                        Binding = new Binding($"[{colName}]"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                    };
                    grid.Columns.Add(column);
                }
            }
            grid.ItemsSource = currentData;
        }
        private void OnRefreshClicked(object? sender, RoutedEventArgs e) {
            Log.Information("DictionaryEditor: Refresh button clicked.");
            LoadDictionaryForPart(Part);
        }

        private void OnOpenFileClicked(object? sender, RoutedEventArgs e) {
            string filePath = ViewModel.GetSelectedFileFullPath();

            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) {
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                } catch (Exception ex) {
                    Serilog.Log.Error(ex, $"DictionaryEditor: Failed to open file in external editor: {filePath}");
                }
            }
        }

        private void LoadDictionaryForPart(UVoicePart? part) {
            Log.Information("--- DictionaryEditor: Attempting to load dictionary ---");

            if (part == null) {
                Log.Information("DictionaryEditor: ABORT - Part is null.");
                ViewModel.ClearContext();
                return;
            }

            var project = DocManager.Inst.Project;
            if (project == null || part.trackNo >= project.tracks.Count) {
                ViewModel.ClearContext();
                return;
            }

            var track = project.tracks[part.trackNo];
            var singer = track.Singer;

            if (singer == null || string.IsNullOrEmpty(singer.Location) || !Directory.Exists(singer.Location)) {
                ViewModel.ClearContext();
                return;
            }

            Log.Information($"DictionaryEditor: Found singer '{singer.Name}'. Location path is: '{singer.Location}'");

            var allFiles = Directory.GetFiles(singer.Location, "*.*", SearchOption.AllDirectories);
            var excludedFiles = new HashSet<string> { "character.yaml", "dsconfig.yaml", "vocoder.yaml" };

            var validFiles = allFiles
                .Where(f => {
                    string fileName = Path.GetFileName(f).ToLower();
                    bool isValidYaml = fileName.EndsWith(".yaml") && !excludedFiles.Contains(fileName);
                    bool isPresamp = fileName == "presamp.ini";
                    
                    return isValidYaml || isPresamp;
                })
                .ToList();

            var groupedFiles = validFiles.GroupBy(f => Path.GetFileName(f).ToLower()).ToList();
            var displayNames = new List<string>();
            var fileMap = new Dictionary<string, string>();

            foreach (var group in groupedFiles) {
                if (group.Count() == 1) {
                    var filePath = group.First();
                    var fileName = Path.GetFileName(filePath);
                    var relativePath = Path.GetRelativePath(singer.Location, filePath);

                    displayNames.Add(fileName);
                    fileMap[fileName] = relativePath;
                } else {
                    foreach (var filePath in group) {
                        var fileName = Path.GetFileName(filePath);
                        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath));
                        var isRoot = Path.GetFullPath(Path.GetDirectoryName(filePath)!) == Path.GetFullPath(singer.Location);

                        string displayName = isRoot ? $"{fileName}" : $"{fileName} ({folderName})";

                        int counter = 1;
                        string finalName = displayName;
                        while (fileMap.ContainsKey(finalName)) {
                            finalName = $"{displayName} ({counter++})";
                        }

                        displayNames.Add(finalName);
                        fileMap[finalName] = Path.GetRelativePath(singer.Location, filePath);
                    }
                }
            }

            Log.Information($"DictionaryEditor: Found {displayNames.Count} valid dictionary/presamp files.");
            ViewModel.SetSingerContext(singer.Location, fileMap);
        }
    }
}