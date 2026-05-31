using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace OpenUtau.App.ViewModels {
    public class DynamicYamlRow : ReactiveObject {
        private readonly Dictionary<string, string> _data = new();
        public string PrimaryColumnKey { get; }

        public DynamicYamlRow(string primaryColumnKey = "Key") {
            PrimaryColumnKey = primaryColumnKey;
        }

        public string this[string key] {
            get => _data.ContainsKey(key) ? _data[key] : string.Empty;
            set {
                _data[key] = value;
                this.RaisePropertyChanged("Item");
                this.RaisePropertyChanged(nameof(IsComment));
                this.RaisePropertyChanged(nameof(IsNotComment));
                this.RaisePropertyChanged(nameof(CommentText));
            }
        }

        public bool IsComment {
            get {
                string val = _data.ContainsKey(PrimaryColumnKey) ? _data[PrimaryColumnKey] : "";
                return val.TrimStart().StartsWith("#") || val.TrimStart().StartsWith(";");
            }
        }
        
        public bool IsNotComment => !IsComment;

        public string CommentText {
            get => _data.ContainsKey(PrimaryColumnKey) ? _data[PrimaryColumnKey] : "";
            set {
                _data[PrimaryColumnKey] = value;
                this.RaisePropertyChanged("Item");
                this.RaisePropertyChanged(nameof(IsComment));
                this.RaisePropertyChanged(nameof(IsNotComment));
                this.RaisePropertyChanged(nameof(CommentText));
            }
        }

        private bool _isEditingComment = false;
        public bool IsEditingComment {
            get => _isEditingComment;
            set {
                _isEditingComment = value;
                this.RaisePropertyChanged(nameof(IsEditingComment));
                this.RaisePropertyChanged(nameof(IsNotEditingComment));
            }
        }
        public bool IsNotEditingComment => !IsEditingComment;

        public Dictionary<string, string> GetData() => _data;
    }

    public class YamlCategory : ReactiveObject {
        public string Name { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
        public HashSet<string> ListColumns { get; set; } = new();
        public bool IsDictionaryFormat { get; set; } = false;
        public bool IsRootScalars { get; set; } = false;
        public ObservableCollection<DynamicYamlRow> Rows { get; } = new();
    }

    public class DictionaryEditorViewModel : ViewModelBase {
        private string _currentDirectory = string.Empty;
        private System.Text.Encoding _currentPresampEncoding = System.Text.Encoding.UTF8;
        private Dictionary<string, string> _filePaths = new();
        public ObservableCollection<string> AvailableFiles { get; } = new();
        [Reactive] public string SelectedFile { get; set; } = string.Empty;
        public string CurrentFileType => !string.IsNullOrEmpty(SelectedFile) && SelectedFile.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? "ini" : "yaml";

        public ObservableCollection<YamlCategory> Categories { get; } = new();
        [Reactive] public YamlCategory? SelectedCategory { get; set; }
        public event Action? ColumnsChanged;
        [Reactive] public DynamicYamlRow? SelectedRow { get; set; }
        [Reactive] public bool IsCreatingNewCategory { get; set; } = false;
        [Reactive] public string NewCategoryName { get; set; } = string.Empty;
        [Reactive] public string NewCategoryColumns { get; set; } = string.Empty;
        [Reactive] public bool IsManagingColumns { get; set; } = false;
        [Reactive] public string ManageColumnName { get; set; } = string.Empty;
        [Reactive] public bool IsConfirmingDelete { get; set; } = false;
        [Reactive] public bool IsCreatingNewFile { get; set; } = false;
        [Reactive] public string NewFileName { get; set; } = string.Empty;
        public Action? RefreshIndices { get; set; }
        [Reactive] public string? ReplaceColumn { get; set; }
        [Reactive] public string FindText { get; set; } = string.Empty;
        [Reactive] public string ReplaceText { get; set; } = string.Empty;
        [Reactive] public bool UseRegex { get; set; } = false;
        private List<DynamicYamlRow> _internalClipboard = new();

        public void DeselectAll() {
            SelectedRow = null;
        }
        private DynamicYamlRow CloneRow(DynamicYamlRow original) {
            var category = SelectedCategory;
            string firstCol = category?.Columns.FirstOrDefault() ?? "Key";
            var clone = new DynamicYamlRow(firstCol);
            if (category != null) {
                foreach (var col in category.Columns) {
                    clone[col] = original[col];
                }
            }
            return clone;
        }
        public void CopyRow(object? parameter) {
            _internalClipboard.Clear();
            if (parameter is System.Collections.IList selectedItems && selectedItems.Count > 0) {
                foreach (var item in selectedItems.Cast<DynamicYamlRow>()) {
                    _internalClipboard.Add(CloneRow(item));
                }
            } 
            else if (parameter is DynamicYamlRow singleRow) {
                _internalClipboard.Add(CloneRow(singleRow));
            }
            else if (SelectedRow != null) {
                _internalClipboard.Add(CloneRow(SelectedRow));
            }
        }
        public void CutRow(object? parameter) {
            CopyRow(parameter);
            DeleteSelectedRow(parameter); 
        }

        public void PasteRow() {
            var category = SelectedCategory;
            if (category == null || _internalClipboard.Count == 0) return;
            int insertIndex = category.Rows.Count;
            if (SelectedRow != null) {
                insertIndex = category.Rows.IndexOf(SelectedRow) + 1;
            }

            foreach (var copiedItem in _internalClipboard) {
                var newRow = CloneRow(copiedItem);
                category.Rows.Insert(insertIndex, newRow);
                insertIndex++;
                SelectedRow = newRow;
            }
            RefreshIndices?.Invoke(); 
        }

        public void DeleteSelectedRow(object? parameter) {
            var category = SelectedCategory;
            if (category == null) return;
            if (parameter is System.Collections.IList selectedItems && selectedItems.Count > 0) {
                var itemsToDelete = selectedItems.Cast<DynamicYamlRow>().ToList();
                foreach (var item in itemsToDelete) {
                    category.Rows.Remove(item);
                }
            }
            else if (parameter is DynamicYamlRow singleRow) {
                category.Rows.Remove(singleRow);
            }
            else if (SelectedRow != null) {
                category.Rows.Remove(SelectedRow);
            }
            RefreshIndices?.Invoke();
        }

        public DictionaryEditorViewModel() {
            this.WhenAnyValue(x => x.SelectedFile)
                .Subscribe(file => {
                    this.RaisePropertyChanged(nameof(CurrentFileType)); 
                    if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(_currentDirectory)) {
                        LoadSelectedFile(); 
                    }
                });
        }
        private void Find(bool searchUp) {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;

            int startIndex = 0;
            if (SelectedRow != null) {
                startIndex = category.Rows.IndexOf(SelectedRow);
                startIndex += searchUp ? -1 : 1;
            }
            int count = category.Rows.Count;
            if (count == 0) return;

            for (int i = 0; i < count; i++) {
                int offset = searchUp ? -i : i;
                int index = (startIndex + offset) % count;
                if (index < 0) index += count;

                var row = category.Rows[index];
                string currentVal = row[ReplaceColumn];
                if (string.IsNullOrEmpty(currentVal)) continue;

                bool isMatch = false;
                if (UseRegex) {
                    try { isMatch = System.Text.RegularExpressions.Regex.IsMatch(currentVal, FindText); } catch { }
                } else {
                    isMatch = currentVal.Contains(FindText);
                }

                if (isMatch) {
                    SelectedRow = row;
                    return; 
                }
            }
        }
        public void ExecuteFindNext() => Find(searchUp: false);
        public void ExecuteFindPrevious() => Find(searchUp: true);
        public void ExecuteFindAll(object? parameter) {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;

            if (parameter is System.Collections.IList selectedItems) {
                selectedItems.Clear(); 
                foreach (var row in category.Rows) {
                    string currentVal = row[ReplaceColumn];
                    if (string.IsNullOrEmpty(currentVal)) continue;

                    bool isMatch = false;
                    if (UseRegex) {
                        try { isMatch = System.Text.RegularExpressions.Regex.IsMatch(currentVal, FindText); } catch { }
                    } else {
                        isMatch = currentVal.Contains(FindText);
                    }
                    if (isMatch) selectedItems.Add(row);
                }
            }
        }
        public void ExecuteReplace(object? parameter) {
            if (SelectedCategory == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;
            bool replacedMultiple = false;

            if (parameter is System.Collections.IList selectedItems && selectedItems.Count > 1) {
                var itemsToProcess = selectedItems.Cast<DynamicYamlRow>().ToList();
                
                foreach (var row in itemsToProcess) {
                    string currentVal = row[ReplaceColumn];
                    if (!string.IsNullOrEmpty(currentVal)) {
                        if (UseRegex) {
                            try { row[ReplaceColumn] = System.Text.RegularExpressions.Regex.Replace(currentVal, FindText, ReplaceText); } catch { }
                        } else {
                            row[ReplaceColumn] = currentVal.Replace(FindText, ReplaceText);
                        }
                    }
                }
                replacedMultiple = true;
            } 
            else if (SelectedRow != null) {
                string currentVal = SelectedRow[ReplaceColumn];
                if (!string.IsNullOrEmpty(currentVal)) {
                    if (UseRegex) {
                        try { SelectedRow[ReplaceColumn] = System.Text.RegularExpressions.Regex.Replace(currentVal, FindText, ReplaceText); } catch { }
                    } else {
                        SelectedRow[ReplaceColumn] = currentVal.Replace(FindText, ReplaceText);
                    }
                }
            }
            if (!replacedMultiple) {
                ExecuteFindNext();
            }
        }
        public void ExecuteReplaceAll() {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrEmpty(ReplaceColumn) || string.IsNullOrEmpty(FindText)) return;
            foreach (var row in category.Rows) {
                string currentVal = row[ReplaceColumn];
                if (string.IsNullOrEmpty(currentVal)) continue;

                if (UseRegex) {
                    try { row[ReplaceColumn] = System.Text.RegularExpressions.Regex.Replace(currentVal, FindText, ReplaceText); } catch { }
                } else {
                    row[ReplaceColumn] = currentVal.Replace(FindText, ReplaceText);
                }
            }
        }
        public void ToggleNewFilePanel() {
            IsCreatingNewFile = !IsCreatingNewFile;
            IsCreatingNewCategory = false;
            IsManagingColumns = false;
            IsConfirmingDelete = false;
            NewFileName = string.Empty;
        }
        public void ToggleConfirmDeletePanel() {
            if (string.IsNullOrEmpty(SelectedFile)) return;
            IsConfirmingDelete = !IsConfirmingDelete;
            IsCreatingNewFile = false;
            IsCreatingNewCategory = false;
            IsManagingColumns = false;
        }
        public void ToggleNewCategoryPanel() {
            IsCreatingNewCategory = !IsCreatingNewCategory;
            IsCreatingNewFile = false;
            IsManagingColumns = false;
            IsConfirmingDelete = false;
            NewCategoryName = string.Empty;
            NewCategoryColumns = string.Empty;
        }
        public void ToggleManageColumnsPanel() {
            IsManagingColumns = !IsManagingColumns;
            IsCreatingNewFile = false;
            IsCreatingNewCategory = false;
            IsConfirmingDelete = false;
            ManageColumnName = string.Empty;
        }

        public string GetSelectedFileFullPath() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return string.Empty;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                return Path.Combine(_currentDirectory, relativePath);
            }
            return string.Empty;
        }
        public void ConfirmNewFile() {
            if (string.IsNullOrWhiteSpace(NewFileName) || string.IsNullOrEmpty(_currentDirectory)) return;
            string fileName = NewFileName.Trim();
            if (!fileName.EndsWith(".yaml") && !fileName.EndsWith(".yml")) {
                fileName += ".yaml";
            }
            string filePath = Path.Combine(_currentDirectory, fileName);
            if (!File.Exists(filePath)) {
                File.WriteAllText(filePath, "# Created with OpenUtau Dictionary Editor\n");
                AvailableFiles.Add(fileName);
                _filePaths[fileName] = fileName; 
            }
            SelectedFile = fileName;
            LoadYaml(filePath); 
            ToggleNewFilePanel();
            NewFileName = string.Empty;
        }
        public void DeleteSelectedFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string filePath = Path.Combine(_currentDirectory, relativePath);
                if (File.Exists(filePath)) {
                    File.Delete(filePath);
                }
            }
            AvailableFiles.Remove(SelectedFile);
            if (AvailableFiles.Count > 0) SelectedFile = AvailableFiles[0];
            else ClearContext();
        }
        public void ConfirmDeleteFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string filePath = Path.Combine(_currentDirectory, relativePath);
                if (File.Exists(filePath)) {
                    File.Delete(filePath);
                }
            }
            AvailableFiles.Remove(SelectedFile);
            if (AvailableFiles.Count > 0) SelectedFile = AvailableFiles[0];
            else ClearContext();
            IsConfirmingDelete = false;
        }

        public void ConfirmNewCategory() {
            if (string.IsNullOrWhiteSpace(NewCategoryName) || string.IsNullOrWhiteSpace(NewCategoryColumns)) return;
            var columns = NewCategoryColumns.Split(',').Select(c => c.Trim()).Where(c => !string.IsNullOrEmpty(c)).ToList();
            if (columns.Count == 0) return;
            var newCat = new YamlCategory { Name = NewCategoryName.Trim(), Columns = columns };
            Categories.Add(newCat);
            SelectedCategory = newCat;
            ToggleNewCategoryPanel();
        }

        public void DeleteSelectedCategory() {
            if (SelectedCategory != null) {
                Categories.Remove(SelectedCategory);
                SelectedCategory = Categories.FirstOrDefault();
            }
        }

        public void AddNewColumn() {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrWhiteSpace(ManageColumnName)) return;
            var columnsToAdd = ManageColumnName.Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            bool changed = false;
            foreach (var col in columnsToAdd) {
                if (!category.Columns.Contains(col)) {
                    category.Columns.Add(col);
                    changed = true;
                }
            }
            if (changed) {
                ColumnsChanged?.Invoke();
            }
            
            ManageColumnName = string.Empty;
        }

        public void RemoveColumn() {
            var category = SelectedCategory;
            if (category == null || string.IsNullOrWhiteSpace(ManageColumnName)) return;
            var columnsToRemove = ManageColumnName.Split(',')
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            bool changed = false;
            foreach (var col in columnsToRemove) {
                if (category.Columns.Contains(col)) {
                    category.Columns.Remove(col);
                    changed = true;
                }
            }
            if (changed) {
                ColumnsChanged?.Invoke();
            }
            ManageColumnName = string.Empty;
        }

        public void AddNewRow() {
            var category = SelectedCategory;
            if (category == null) return;
            string firstCol = category.Columns.FirstOrDefault() ?? "Key";
            var newRow = new DynamicYamlRow(firstCol);

            if (SelectedRow != null) {
                int index = category.Rows.IndexOf(SelectedRow);
                if (index >= 0) {
                    category.Rows.Insert(index + 1, newRow);
                    SelectedRow = newRow;
                    RefreshIndices?.Invoke(); 
                    return;
                }
            }
            category.Rows.Add(newRow);
            SelectedRow = newRow;
            RefreshIndices?.Invoke(); 
        }

        public void AddNewCommentRow() {
            var category = SelectedCategory;
            if (CurrentFileType != "yaml" || category == null) return;
            string firstCol = category.Columns.FirstOrDefault() ?? "Key";
            var newRow = new DynamicYamlRow(firstCol);
            newRow[firstCol] = "# New Comment...";

            if (SelectedRow != null) {
                int index = category.Rows.IndexOf(SelectedRow);
                if (index >= 0) {
                    category.Rows.Insert(index + 1, newRow);
                    SelectedRow = newRow;
                    RefreshIndices?.Invoke(); 
                    return;
                }
            }
            category.Rows.Add(newRow);
            SelectedRow = newRow;
            RefreshIndices?.Invoke(); 
        }

        public void SetSingerContext(string dir, Dictionary<string, string> fileMap) {
            _currentDirectory = dir;
            _filePaths = fileMap;
            AvailableFiles.Clear();
            foreach (var name in fileMap.Keys) {
                AvailableFiles.Add(name);
            }
            if (AvailableFiles.Count > 0) SelectedFile = AvailableFiles[0];
        }
        public void ClearContext() {
            _currentDirectory = string.Empty;
            AvailableFiles.Clear();
            Categories.Clear();
        }

        public void LoadPresamp(string filePath) {
            Categories.Clear();
            if (!File.Exists(filePath)) return;
            
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            byte[] rawBytes = File.ReadAllBytes(filePath);
            var strictUtf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

            try {
                strictUtf8.GetString(rawBytes);
                _currentPresampEncoding = new System.Text.UTF8Encoding(true); 
            } 
            catch (System.Text.DecoderFallbackException) {
                _currentPresampEncoding = System.Text.Encoding.GetEncoding("shift_jis");
            }
            string[] lines = File.ReadAllLines(filePath, _currentPresampEncoding);
            YamlCategory? currentCategory = null;

            foreach (var rawLine in lines) {
                string lineToProcess = rawLine.TrimEnd('\r', '\n');
                if (string.IsNullOrEmpty(lineToProcess)) continue;

                string headerCheck = lineToProcess.Trim();

                if (headerCheck.StartsWith("[") && headerCheck.EndsWith("]")) {
                    string sectionName = headerCheck.Substring(1, headerCheck.Length - 2);
                    currentCategory = new YamlCategory { Name = sectionName };
                    Categories.Add(currentCategory);

                    if (sectionName == "VOWEL") currentCategory.Columns = new List<string> { "ID", "Base", "Phonemes", "Vol" };
                    else if (sectionName == "CONSONANT") currentCategory.Columns = new List<string> { "ID", "Phonemes", "Crossfade" };
                    else if (sectionName == "REPLACE" || sectionName == "ALIAS") currentCategory.Columns = new List<string> { "Key", "Value" };
                    else currentCategory.Columns = new List<string> { "Value" };
                    continue;
                }

                if (currentCategory == null) continue;
                string firstCol = currentCategory.Columns.FirstOrDefault() ?? "Key";
                var newRow = new DynamicYamlRow(firstCol);
                
                if (currentCategory.Name == "VOWEL") {
                    var parts = lineToProcess.Split('=');
                    newRow["ID"] = parts.Length > 0 ? parts[0] : "";
                    newRow["Base"] = parts.Length > 1 ? parts[1] : "";
                    newRow["Phonemes"] = parts.Length > 2 ? parts[2] : "";
                    newRow["Vol"] = parts.Length > 3 ? parts[3] : "";
                } 
                else if (currentCategory.Name == "CONSONANT") {
                    var parts = lineToProcess.Split('=');
                    newRow["ID"] = parts.Length > 0 ? parts[0] : "";
                    newRow["Phonemes"] = parts.Length > 1 ? parts[1] : "";
                    newRow["Crossfade"] = parts.Length > 2 ? parts[2] : "";
                } 
                else if (currentCategory.Name == "REPLACE" || currentCategory.Name == "ALIAS") {
                    var parts = lineToProcess.Split(new[] { '=' }, 2);
                    newRow["Key"] = parts.Length > 0 ? parts[0].TrimEnd() : "";
                    newRow["Value"] = parts.Length > 1 ? parts[1] : "";
                } 
                else {
                    newRow["Value"] = lineToProcess;
                }

                currentCategory.Rows.Add(newRow);
            }

            if (Categories.Count > 0) SelectedCategory = Categories[0];
            ColumnsChanged?.Invoke(); 
        }

        public void SavePresamp(string filePath) {
            var lines = new List<string>();
            foreach (var cat in Categories) {
                lines.Add($"[{cat.Name}]");
                foreach (var row in cat.Rows) {
                    if (cat.Name == "VOWEL") lines.Add($"{row["ID"]}={row["Base"]}={row["Phonemes"]}={row["Vol"]}");
                    else if (cat.Name == "CONSONANT") lines.Add($"{row["ID"]}={row["Phonemes"]}={row["Crossfade"]}");
                    else if (cat.Name == "REPLACE" || cat.Name == "ALIAS") lines.Add($"{row["Key"]}={row["Value"]}");
                    else {
                        if (cat.Columns.Count > 0) {
                            string firstCol = cat.Columns[0];
                            string val = row[firstCol] ?? "";
                            if (!string.IsNullOrEmpty(val)) lines.Add(val);
                        }
                    }
                }
            }
            File.WriteAllLines(filePath, lines, _currentPresampEncoding);
        }

        public void LoadSelectedFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string targetPath = Path.Combine(_currentDirectory, relativePath);
                if (SelectedFile.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) LoadPresamp(targetPath);
                else LoadYaml(targetPath);
            }
        }

        public void SaveCurrentFile() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string targetPath = Path.Combine(_currentDirectory, relativePath);
                if (SelectedFile.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) SavePresamp(targetPath);
                else SaveYaml(); 
            }
        }

        public void LoadYaml(string filePath) {
            Categories.Clear();
            if (!File.Exists(filePath)) return;

            try {
                var yamlContent = File.ReadAllText(filePath);
                string[] rawLines = yamlContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var yaml = new YamlDotNet.RepresentationModel.YamlStream();
                yaml.Load(new StringReader(yamlContent));

                if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode rootMapping) return;
                YamlCategory? metaCategory = null;
                
                int lastProcessedLine = 1;

                var sortedRoots = rootMapping.Children.OrderBy(kvp => kvp.Key.Start.Line).ToList();

                foreach (var kvp in sortedRoots) {
                    var rootKeyNode = kvp.Key as YamlDotNet.RepresentationModel.YamlScalarNode;
                    string rootKey = rootKeyNode?.Value ?? "";
                    var rootValue = kvp.Value;
                    int currentStartLine = kvp.Key.Start.Line;
                    
                    var preComments = new List<string>();
                    for (int i = lastProcessedLine; i < currentStartLine; i++) {
                        if (i >= 1 && i <= rawLines.Length) {
                            string gapLine = rawLines[i - 1].TrimStart();
                            if (gapLine.StartsWith("#")) preComments.Add(gapLine);
                        }
                    }
                    
                    lastProcessedLine = currentStartLine + 1;

                    if (rootValue is YamlDotNet.RepresentationModel.YamlSequenceNode seqNode) {
                        var category = new YamlCategory { Name = rootKey };
                        var allColumns = new HashSet<string>();

                        foreach (var rowNode in seqNode.Children) {
                            if (rowNode is YamlDotNet.RepresentationModel.YamlMappingNode rowDict) {
                                foreach (var keyNode in rowDict.Children.Keys) {
                                    if (keyNode is YamlDotNet.RepresentationModel.YamlScalarNode scalarKey) {
                                        allColumns.Add(scalarKey.Value ?? "");
                                    }
                                }
                            }
                        }
                        category.Columns = allColumns.ToList();

                        if (category.Columns.Count > 0) {
                            string firstCol = category.Columns[0];
                            foreach (var c in preComments) {
                                var cRow = new DynamicYamlRow(firstCol);
                                cRow[firstCol] = c;
                                category.Rows.Add(cRow);
                            }

                            var sortedRows = seqNode.Children.OrderBy(n => n.Start.Line).ToList();
                            
                            foreach (var rowNode in sortedRows) {
                                int actualLine = rowNode.Start.Line;
                                for (int i = lastProcessedLine; i < actualLine; i++) {
                                    if (i >= 1 && i <= rawLines.Length) {
                                        string gapLine = rawLines[i - 1].TrimStart();
                                        if (gapLine.StartsWith("#")) {
                                            var cRow = new DynamicYamlRow(firstCol);
                                            cRow[firstCol] = gapLine;
                                            category.Rows.Add(cRow);
                                        }
                                    }
                                }
                                
                                if (rowNode is YamlDotNet.RepresentationModel.YamlMappingNode rowDict) {
                                    var row = new DynamicYamlRow(firstCol);
                                    foreach (var col in category.Columns) {
                                        var keyMatch = rowDict.Children.Keys.FirstOrDefault(k => (k as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value == col);
                                        if (keyMatch != null) {
                                            var valNode = rowDict.Children[keyMatch];
                                            if (valNode is YamlDotNet.RepresentationModel.YamlSequenceNode listNode) {
                                                var formattedList = listNode.Children.Select(x => {
                                                    var scalar = x as YamlDotNet.RepresentationModel.YamlScalarNode;
                                                    string s = scalar?.Value ?? "";
                                                    if (scalar != null && (scalar.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalar.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted)) return $"\"{s}\"";
                                                    return s;
                                                });
                                                row[col] = string.Join(" ", formattedList);
                                                category.ListColumns.Add(col);
                                            } else if (valNode is YamlDotNet.RepresentationModel.YamlScalarNode scalarVal) {
                                                string s = scalarVal.Value ?? "";
                                                if (scalarVal.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalarVal.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted) row[col] = $"\"{s}\"";
                                                else row[col] = s;
                                            }
                                        }
                                    }
                                    category.Rows.Add(row);
                                }
                                lastProcessedLine = Math.Max(lastProcessedLine, rowNode.End.Line + 1);
                            }
                        }
                        Categories.Add(category);

                    } else if (rootValue is YamlDotNet.RepresentationModel.YamlMappingNode dictNode) {
                        var category = new YamlCategory { Name = rootKey, Columns = new List<string> { "Key", "Value" }, IsDictionaryFormat = true };

                        foreach (var c in preComments) {
                            var cRow = new DynamicYamlRow("Key");
                            cRow["Key"] = c;
                            category.Rows.Add(cRow);
                        }

                        var sortedInner = dictNode.Children.OrderBy(k => k.Key.Start.Line).ToList();

                        foreach (var innerKvp in sortedInner) {
                            int actualLine = innerKvp.Key.Start.Line;
                            for (int i = lastProcessedLine; i < actualLine; i++) {
                                if (i >= 1 && i <= rawLines.Length) {
                                    string gapLine = rawLines[i - 1].TrimStart();
                                    if (gapLine.StartsWith("#")) {
                                        var cRow = new DynamicYamlRow("Key");
                                        cRow["Key"] = gapLine;
                                        category.Rows.Add(cRow);
                                    }
                                }
                            }
                            
                            var row = new DynamicYamlRow("Key");
                            var innerKeyNode = innerKvp.Key as YamlDotNet.RepresentationModel.YamlScalarNode;
                            string keyStr = innerKeyNode?.Value ?? "";
                            if (innerKeyNode != null && (innerKeyNode.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || innerKeyNode.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted)) row["Key"] = $"\"{keyStr}\"";
                            else row["Key"] = keyStr;

                            var innerValNode = innerKvp.Value;
                            if (innerValNode is YamlDotNet.RepresentationModel.YamlSequenceNode listNode) {
                                var formattedList = listNode.Children.Select(x => {
                                    var scalar = x as YamlDotNet.RepresentationModel.YamlScalarNode;
                                    string s = scalar?.Value ?? "";
                                    if (scalar != null && (scalar.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalar.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted)) return $"\"{s}\"";
                                    return s;
                                });
                                row["Value"] = string.Join(" ", formattedList);
                                category.ListColumns.Add("Value");
                            } else if (innerValNode is YamlDotNet.RepresentationModel.YamlScalarNode scalarVal) {
                                string s = scalarVal.Value ?? "";
                                if (scalarVal.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalarVal.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted) row["Value"] = $"\"{s}\"";
                                else row["Value"] = s;
                            }
                            category.Rows.Add(row);
                            
                            lastProcessedLine = Math.Max(lastProcessedLine, innerKvp.Value.End.Line + 1);
                        }
                        Categories.Add(category);

                    } else if (rootValue is YamlDotNet.RepresentationModel.YamlScalarNode scalarRoot) {
                        if (metaCategory == null) {
                            metaCategory = new YamlCategory { Name = "Metadata", Columns = new List<string> { "Key", "Value" }, IsRootScalars = true };
                            Categories.Insert(0, metaCategory);
                        }

                        foreach (var c in preComments) {
                            var cRow = new DynamicYamlRow("Key") { ["Key"] = c };
                            metaCategory.Rows.Add(cRow);
                        }

                        var row = new DynamicYamlRow("Key") { ["Key"] = rootKey };
                        string s = scalarRoot.Value ?? "";
                        if (scalarRoot.Style == YamlDotNet.Core.ScalarStyle.DoubleQuoted || scalarRoot.Style == YamlDotNet.Core.ScalarStyle.SingleQuoted) row["Value"] = $"\"{s}\"";
                        else row["Value"] = s;
                        metaCategory.Rows.Add(row);
                        
                        lastProcessedLine = Math.Max(lastProcessedLine, scalarRoot.End.Line + 1);
                    }
                    
                    lastProcessedLine = Math.Max(lastProcessedLine, rootValue.End.Line + 1);
                }

                if (Categories.Count > 0) {
                    var lastCat = Categories.Last();
                    string firstCol = lastCat.Columns.FirstOrDefault() ?? "Key";
                    for (int i = lastProcessedLine; i <= rawLines.Length; i++) {
                        if (i >= 1 && i <= rawLines.Length) {
                            string gapLine = rawLines[i - 1].TrimStart();
                            if (gapLine.StartsWith("#")) {
                                var cRow = new DynamicYamlRow(firstCol);
                                cRow[firstCol] = gapLine;
                                lastCat.Rows.Add(cRow);
                            }
                        }
                    }
                }
                
                if (Categories.Count > 0) SelectedCategory = Categories[0];
            } catch (Exception ex) {
                Serilog.Log.Error(ex, $"Failed to parse YAML: {filePath}");
                Categories.Clear();
            }
        }

        public void SaveYaml() {
            if (string.IsNullOrEmpty(SelectedFile) || string.IsNullOrEmpty(_currentDirectory)) return;
            var dictToSave = new Dictionary<string, object>();

            foreach (var cat in Categories) {
                if (cat.IsRootScalars) {
                    foreach (var row in cat.Rows) {
                        string key = row["Key"] ?? "";
                        if (key.TrimStart().StartsWith("#")) {
                            dictToSave[$"__comment_{Guid.NewGuid():N}__"] = key.TrimStart();
                            continue;
                        }
                        string val = row["Value"] ?? "";
                        if (!string.IsNullOrWhiteSpace(key)) {
                            if (double.TryParse(val, out double numVal)) dictToSave[key] = numVal;
                            else dictToSave[key] = val;
                        }
                    }
                } else if (cat.IsDictionaryFormat) {
                    var dictNode = new Dictionary<string, object>();
                    foreach (var row in cat.Rows) {
                        string key = row["Key"] ?? "";
                        if (key.TrimStart().StartsWith("#")) {
                            dictNode[$"__comment_{Guid.NewGuid():N}__"] = key.TrimStart();
                            continue;
                        }
                        string val = row["Value"] ?? "";
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        if (!string.IsNullOrWhiteSpace(val)) {
                            string trimmedVal = val.Trim();
                            bool isExplicitList = trimmedVal.StartsWith("[") && trimmedVal.EndsWith("]");
                            
                            var matches = System.Text.RegularExpressions.Regex.Matches(trimmedVal, @"\""[^\""]*\""|'[^']*'|[^ ,]+");
                            
                            if (matches.Count > 1 || isExplicitList) {
                                dictNode[key] = matches.Cast<System.Text.RegularExpressions.Match>()
                                                       .Select(m => m.Value.Trim('[', ']'))
                                                       .Where(s => !string.IsNullOrWhiteSpace(s))
                                                       .ToList();
                            } else {
                                dictNode[key] = trimmedVal; 
                            }
                        } else {
                            dictNode[key] = val;
                        }
                    }
                    dictToSave[cat.Name] = dictNode;
                } else {
                    var rowList = new List<Dictionary<string, object>>();
                    foreach (var row in cat.Rows) {
                        string firstColVal = row[cat.Columns.FirstOrDefault() ?? "Key"] ?? "";
                        if (firstColVal.TrimStart().StartsWith("#")) {
                            var commentRow = new Dictionary<string, object>();
                            commentRow["__full_row_comment__"] = firstColVal.TrimStart();
                            rowList.Add(commentRow);
                            continue;
                        }

                        var newRow = new Dictionary<string, object>();
                        foreach (var col in cat.Columns) {
                            string val = row[col] ?? "";
                            if (string.IsNullOrWhiteSpace(val)) continue;

                            string trimmedVal = val.Trim();
                            bool isExplicitList = trimmedVal.StartsWith("[") && trimmedVal.EndsWith("]");
                            
                            bool isPhonemesColumn = col.Equals("phonemes", StringComparison.OrdinalIgnoreCase);
                            var matches = System.Text.RegularExpressions.Regex.Matches(trimmedVal, @"\""[^\""]*\""|[^ ,]+");
                            
                            // It becomes a list IF: multiple items, explicit brackets, OR if it is the phonemes column!
                            if (matches.Count > 1 || isExplicitList || isPhonemesColumn) {
                                newRow[col] = matches.Cast<System.Text.RegularExpressions.Match>()
                                                     .Select(m => m.Value.Trim('[', ']'))
                                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                                     .ToList();
                            } else {
                                newRow[col] = trimmedVal; // Otherwise, preserve as standard string
                            }
                        }
                        if (newRow.Count > 0) rowList.Add(newRow);
                    }
                    dictToSave[cat.Name] = rowList;
                }
            }

            var serializer = new SerializerBuilder()
                .DisableAliases()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .WithIndentedSequences()
                .WithEventEmitter(next => new BracketStyleEmitter(next))
                .Build();

            if (_filePaths.TryGetValue(SelectedFile, out string? relativePath) && relativePath != null) {
                string rawYaml = serializer.Serialize(dictToSave);
                
                rawYaml = System.Text.RegularExpressions.Regex.Replace(
                    rawYaml,
                    @"^([ \t]*)-\s*\{?\s*__full_row_comment__:\s*(?:>-\s*)?(?:""|')?(.*?)(?:""|')?\s*\}?\s*$",
                    m => $"{m.Groups[1].Value}{m.Groups[2].Value}",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                rawYaml = System.Text.RegularExpressions.Regex.Replace(
                    rawYaml,
                    @"^([ \t]*)\{?\s*__comment_[a-f0-9]+__:\s*(?:>-\s*)?(?:""|')?(.*?)(?:""|')?\s*\}?\s*$",
                    m => $"{m.Groups[1].Value}{m.Groups[2].Value}",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );
                
                rawYaml = System.Text.RegularExpressions.Regex.Replace(
                    rawYaml,
                    @"(?m)^(?=[a-zA-Z0-9_]+:)", 
                    "\n"
                );
                rawYaml = rawYaml.TrimStart('\n');

                File.WriteAllText(Path.Combine(_currentDirectory, relativePath), rawYaml);
            }
        }
    }

    public class BracketStyleEmitter : ChainedEventEmitter {
        private int _depth = 0;
        public BracketStyleEmitter(IEventEmitter nextEmitter) : base(nextEmitter) { }
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter) {
            if (eventInfo.Source.Value is string val && val.Length >= 2 && val.StartsWith("\"") && val.EndsWith("\"")) {
                string strippedValue = val.Substring(1, val.Length - 2);
                var newSource = new YamlDotNet.Serialization.ObjectDescriptor(strippedValue, typeof(string), typeof(string));
                var newEventInfo = new ScalarEventInfo(newSource) { Style = ScalarStyle.DoubleQuoted };
                base.Emit(newEventInfo, emitter);
                return;
            }
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter) {
            _depth++;
            eventInfo.Style = _depth >= 3 ? MappingStyle.Flow : MappingStyle.Block;
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(MappingEndEventInfo eventInfo, IEmitter emitter) {
            _depth--;
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter) {
            _depth++;
            eventInfo.Style = _depth >= 3 ? SequenceStyle.Flow : SequenceStyle.Block;
            base.Emit(eventInfo, emitter);
        }
        public override void Emit(SequenceEndEventInfo eventInfo, IEmitter emitter) {
            _depth--;
            base.Emit(eventInfo, emitter);
        }
    }
}