using System.Collections.Concurrent;
using System.Text.Json;
using MediaForge.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MediaForge;

public sealed partial class ConverterPage : Page
{
    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "doc", "docx", "docm", "ppt", "pptx", "pptm", "pps", "ppsx", "ppsm", "pot",
        "xls", "xlsx", "xlsm", "xlsb", "odt", "ods", "odp", "rtf", "epub", "csv"
    };
    private readonly ConversionService _conversionService = new();
    private readonly HistoryService _historyService = new();
    private readonly List<string> _selectedPaths = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingDocuments = new();
    private readonly TaskCompletionSource _wasmReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _destinationFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");
    private readonly string _wasmRuntimePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MediaForge",
        "WasmRuntime");
    private Task? _wasmInitialization;
    private Microsoft.UI.Xaml.Controls.WebView2? _documentConverterWebView;
    private CancellationTokenSource? _activeCancellation;
    private IReadOnlyList<string> _availableImageFormats = [];

    public ConverterPage()
    {
        InitializeComponent();
        InputModeComboBox.SelectedIndex = 0;
        DestinationText.Text = _destinationFolder;
        Loaded += ConverterPage_Loaded;
        Unloaded += ConverterPage_Unloaded;
    }

    private async void ConverterPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ConverterPage_Loaded;
        try
        {
            _availableImageFormats = await _conversionService.GetAvailableImageFormatsAsync();
            RefreshFormats();
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"Não foi possível carregar os conversores: {exception.Message}";
        }
    }

    private void ConverterPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _activeCancellation?.Cancel();
    }

    private void InputModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InputModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var mode = item.Tag?.ToString();
        DropZone.Visibility = mode is "file" or "merge" ? Visibility.Visible : Visibility.Collapsed;
        TextInputBox.Visibility = mode == "text" ? Visibility.Visible : Visibility.Collapsed;
        UrlInputPanel.Visibility = mode == "url" ? Visibility.Visible : Visibility.Collapsed;
        ChooseInputButton.Content = mode == "merge" ? "Escolher ficheiros (JSON / MD)" : "Escolher ficheiros";
        SelectedFilesText.Text = string.Empty;
        _selectedPaths.Clear();
        RefreshFormats();
    }

    private async void ChooseInputButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List
        };
        var isMerge = GetInputMode() == "merge";
        if (isMerge)
        {
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".md");
        }
        else
        {
            picker.FileTypeFilter.Add("*");
        }
        InitializePicker(picker);

        try
        {
            var files = await picker.PickMultipleFilesAsync();
            if (files is not null && files.Count > 0)
            {
                SetSelectedFiles(files);
            }
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"Não foi possível escolher o ficheiro: {exception.Message}";
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var files = items.OfType<StorageFile>().ToArray();
            SetSelectedFiles(files);
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"Não foi possível ler os ficheiros largados: {exception.Message}";
        }
    }

    private void SetSelectedFiles(IEnumerable<StorageFile> files)
    {
        var selected = files.ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (GetInputMode() == "merge")
        {
            selected = selected.Where(file => 
                file.FileType.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                file.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (selected.Length < 2)
            {
                _selectedPaths.Clear();
                SelectedFilesText.Text = "Selecione pelo menos dois ficheiros (.json ou .md).";
                StatusText.Text = "A união aceita pelo menos dois ficheiros JSON ou Markdown.";
                return;
            }

            var jsonCount = selected.Count(f => f.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase));
            var mdCount = selected.Count(f => f.FileType.Equals(".md", StringComparison.OrdinalIgnoreCase));
            if (jsonCount > 0 && mdCount > 0)
            {
                _selectedPaths.Clear();
                SelectedFilesText.Text = "Selecione ficheiros do mesmo tipo (todos .json ou todos .md).";
                StatusText.Text = "Não é possível misturar JSON e Markdown na mesma união.";
                return;
            }
        }

        _selectedPaths.Clear();
        foreach (var file in selected)
        {
            _selectedPaths.Add(file.Path);
        }
        SelectedFilesText.Text = string.Join(Environment.NewLine, selected.Select(file => file.Name));
        RefreshFormats();
        StatusText.Text = $"{selected.Length} ficheiro(s) selecionado(s).";
    }

    private async void ChooseDestinationButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);
        try
        {
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                DestinationText.Text = folder.Path;
            }
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"Não foi possível escolher a pasta: {exception.Message}";
        }
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetFormat(out var format))
        {
            StatusText.Text = "Selecione um formato de saída disponível.";
            FormatComboBox.Focus(FocusState.Programmatic);
            return;
        }

        var mode = GetInputMode();
        if (mode == "merge")
        {
            await MergeSelectedFilesAsync();
            return;
        }

        if (mode == "file")
        {
            if (_selectedPaths.Count == 0)
            {
                StatusText.Text = "Escolha pelo menos um ficheiro antes de iniciar.";
                return;
            }
            if (_selectedPaths.Count > 1)
            {
                // If format is json or md and multiple files match that merge format, or user wants to merge them
                var allJson = _selectedPaths.All(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                var allMd = _selectedPaths.All(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                if (format == "json" && allJson)
                {
                    await MergeSelectedFilesAsync();
                    return;
                }
                if (format == "md" && allMd)
                {
                    await MergeSelectedFilesAsync();
                    return;
                }

                // Batch conversion of multiple files
                await ConvertBatchFilesAsync(format);
                return;
            }
        }

        if (mode == "text" && string.IsNullOrWhiteSpace(TextInputBox.Text))
        {
            StatusText.Text = "Escreva ou cole algum texto antes de converter.";
            TextInputBox.Focus(FocusState.Programmatic);
            return;
        }
        if (mode == "url" && string.IsNullOrWhiteSpace(UrlInputBox.Text))
        {
            StatusText.Text = "Insira o endereço do website antes de converter.";
            UrlInputBox.Focus(FocusState.Programmatic);
            return;
        }

        string? temporaryInput = null;
        SetBusy(true);
        _activeCancellation = new CancellationTokenSource();
        ConversionProgressBar.Visibility = Visibility.Visible;
        ConversionProgressBar.IsIndeterminate = true;
        try
        {
            var sourcePath = mode == "file" ? _selectedPaths[0] : await CreateInputFileAsync(mode, _activeCancellation.Token);
            temporaryInput = mode == "file" ? null : sourcePath;
            var options = BuildOptions(format);
            var progress = new Progress<ConversionProgress>(UpdateProgress);
            Func<ReadOnlyMemory<byte>, string, CancellationToken, Task<string>>? officeConverter = null;
            if (OfficeExtensions.Contains(Path.GetExtension(sourcePath).TrimStart('.')))
            {
                officeConverter = ConvertOfficeDocumentAsync;
            }

            var result = await _conversionService.ConvertAsync(
                sourcePath,
                options,
                DestinationText.Text,
                progress,
                officeConverter,
                _activeCancellation.Token);
            await _historyService.AddAsync(new ActivityHistoryItem(
                "Conversão",
                Path.GetFileName(sourcePath),
                result.FileName,
                DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                result.FullPath));
            StatusText.Text = $"Concluído: {result.FileName} · guardado em {DestinationText.Text}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Conversão cancelada.";
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"A conversão falhou: {exception.Message}";
        }
        finally
        {
            if (temporaryInput is not null && File.Exists(temporaryInput))
            {
                File.Delete(temporaryInput);
            }
            ConversionProgressBar.IsIndeterminate = false;
            ConversionProgressBar.Visibility = Visibility.Collapsed;
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            SetBusy(false);
        }
    }

    private async Task ConvertBatchFilesAsync(string format)
    {
        SetBusy(true);
        _activeCancellation = new CancellationTokenSource();
        ConversionProgressBar.Visibility = Visibility.Visible;
        ConversionProgressBar.IsIndeterminate = false;
        ConversionProgressBar.Value = 0;
        var count = _selectedPaths.Count;
        var successes = 0;

        try
        {
            for (var i = 0; i < count; i++)
            {
                _activeCancellation.Token.ThrowIfCancellationRequested();
                var sourcePath = _selectedPaths[i];
                StatusText.Text = $"A converter [{i + 1}/{count}]: {Path.GetFileName(sourcePath)}…";
                ConversionProgressBar.Value = (double)i / count * 100;

                var options = BuildOptions(format);
                Func<ReadOnlyMemory<byte>, string, CancellationToken, Task<string>>? officeConverter = null;
                if (OfficeExtensions.Contains(Path.GetExtension(sourcePath).TrimStart('.')))
                {
                    officeConverter = ConvertOfficeDocumentAsync;
                }

                var result = await _conversionService.ConvertAsync(
                    sourcePath,
                    options,
                    DestinationText.Text,
                    null,
                    officeConverter,
                    _activeCancellation.Token);

                await _historyService.AddAsync(new ActivityHistoryItem(
                    "Conversão",
                    Path.GetFileName(sourcePath),
                    result.FileName,
                    DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    result.FullPath));
                successes++;
            }

            ConversionProgressBar.Value = 100;
            StatusText.Text = $"Concluído com sucesso: {successes} ficheiro(s) convertidos · guardados em {DestinationText.Text}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"Conversão em lote cancelada após {successes} ficheiro(s).";
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"Conversão interrompida ({successes}/{count} concluídos): {exception.Message}";
        }
        finally
        {
            ConversionProgressBar.Visibility = Visibility.Collapsed;
            _activeCancellation?.Dispose();
            _activeCancellation = null;
            SetBusy(false);
        }
    }

    private async Task MergeSelectedFilesAsync()
    {
        if (_selectedPaths.Count < 2)
        {
            StatusText.Text = "Escolha pelo menos dois ficheiros.";
            return;
        }

        var allJson = _selectedPaths.All(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        var allMd = _selectedPaths.All(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

        if (!allJson && !allMd)
        {
            StatusText.Text = "Para juntar ficheiros, selecione apenas ficheiros .json ou apenas ficheiros .md.";
            return;
        }

        SetBusy(true);
        try
        {
            ConversionResult result;
            string activityTitle;

            if (allJson)
            {
                result = await _conversionService.MergeJsonAsync(_selectedPaths, DestinationText.Text);
                activityTitle = "União JSON";
            }
            else
            {
                result = await _conversionService.MergeMarkdownAsync(_selectedPaths, DestinationText.Text);
                activityTitle = "União Markdown";
            }

            await _historyService.AddAsync(new ActivityHistoryItem(
                activityTitle,
                $"{_selectedPaths.Count} ficheiros",
                result.FileName,
                DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                result.FullPath));
            StatusText.Text = $"Ficheiros unidos: {result.FileName} · guardado em {DestinationText.Text}";
        }
        catch (Exception exception) when (IsExpectedConversionException(exception))
        {
            StatusText.Text = $"Não foi possível unir os ficheiros: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<string> CreateInputFileAsync(string mode, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MediaForge",
            "Temp",
            "Input");
        Directory.CreateDirectory(directory);
        var extension = mode == "url" ? ".url" : ".txt";
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
        var content = mode == "url" ? UrlInputBox.Text.Trim() : TextInputBox.Text;
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }

    private ConversionOptions BuildOptions(string format)
    {
        var extension = _selectedPaths.Count == 1 ? Path.GetExtension(_selectedPaths[0]).TrimStart('.').ToLowerInvariant() : string.Empty;
        var usesQuality = extension == "pdf"
            ? new[] { "jpg", "jpeg", "webp", "tiff", "tif", "avif", "heic" }.Contains(format)
            : extension is "mp4" or "m4v" or "mov" or "mkv" or "avi" or "webm" or "wmv" or "flv" or "mpeg" or "mpg" or "3gp"
                || new[] { "jpg", "jpeg", "webp", "tiff", "tif", "avif", "heic" }.Contains(format);
        return new ConversionOptions(
            Format: format,
            Width: null,
            Height: null,
            Quality: usesQuality ? 100 : null,
            Rotation: 0);
    }

    private void RefreshFormats()
    {
        var mode = GetInputMode();
        var selectedPath = _selectedPaths.Count > 0 ? _selectedPaths[0] : null;
        var extension = mode switch
        {
            "text" => "txt",
            "url" => "url",
            "merge" => "md",
            _ when selectedPath is not null => Path.GetExtension(selectedPath).TrimStart('.').ToLowerInvariant(),
            _ => string.Empty
        };

        IEnumerable<string> formats;
        if (mode == "merge")
        {
            var allJson = _selectedPaths.Count > 0 && _selectedPaths.All(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            if (allJson)
            {
                formats = ["json"];
            }
            else
            {
                formats = ["md"];
            }
        }
        else if (mode == "text")
        {
            formats = ["md", "txt"];
        }
        else if (mode == "url" || extension == "url")
        {
            formats = ["md", "txt"];
        }
        else if (extension == "json")
        {
            // If multiple JSON files are selected, allow JSON (merge) or MD / TXT
            formats = _selectedPaths.Count > 1 ? ["json", "md", "txt"] : ["md", "txt"];
        }
        else if (extension == "md")
        {
            // If multiple MD files are selected, allow MD (merge) or TXT
            formats = _selectedPaths.Count > 1 ? ["md", "txt"] : ["md", "txt"];
        }
        else if (extension == "py")
        {
            formats = ["md", "ipynb", "txt"];
        }
        else if (extension is "html" or "htm")
        {
            formats = ["md", "txt"];
        }
        else if (OfficeExtensions.Contains(extension))
        {
            formats = ["md"];
        }
        else if (extension == "pdf")
        {
            formats = _availableImageFormats.Concat(["md", "txt"]);
        }
        else if (new[] { "mp4", "m4v", "mov", "mkv", "avi", "webm", "wmv", "flv", "mpeg", "mpg", "3gp" }.Contains(extension))
        {
            formats = ["mp4", "webm", "mkv", "avi", "mov", "mp3", "wav", "m4a", "aac", "zip"];
        }
        else if (new[] { "mp3", "m4a", "aac", "wav", "flac", "ogg", "opus", "wma" }.Contains(extension))
        {
            formats = ["mp3", "wav", "m4a", "aac"];
        }
        else if (ConversionService.KnownImageExtensions.Contains(extension))
        {
            formats = _availableImageFormats;
        }
        else if (ConversionService.KnownTextExtensions.Contains(extension)
            || (selectedPath is not null && ConversionService.IsTextFile(selectedPath)))
        {
            formats = ["md", "txt"];
        }
        else
        {
            formats = [];
        }

        FormatComboBox.Items.Clear();
        foreach (var format in formats.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            FormatComboBox.Items.Add(new ComboBoxItem
            {
                Content = format.ToUpperInvariant(),
                Tag = format
            });
        }
        if (FormatComboBox.Items.Count > 0)
        {
            FormatComboBox.SelectedIndex = 0;
            if (selectedPath is not null && !string.IsNullOrWhiteSpace(StatusText.Text) && StatusText.Text.Contains("não suportado", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = string.Empty;
            }
        }
        else if (selectedPath is not null && mode == "file")
        {
            StatusText.Text = "Formato de ficheiro não suportado.";
        }
    }

    private string GetInputMode()
    {
        return (InputModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "file";
    }

    private bool TryGetFormat(out string format)
    {
        format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(format);
    }


    private void UpdateProgress(ConversionProgress progress)
    {
        StatusText.Text = progress.Message;
        if (progress.Percent is double percent)
        {
            ConversionProgressBar.IsIndeterminate = false;
            ConversionProgressBar.Value = Math.Clamp(percent, 0, 100);
        }
        else
        {
            ConversionProgressBar.IsIndeterminate = true;
        }
    }

    private void SetBusy(bool busy)
    {
        ConvertButton.IsEnabled = !busy;
        ChooseInputButton.IsEnabled = !busy;
        ChooseDestinationButton.IsEnabled = !busy;
        InputModeComboBox.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _activeCancellation?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "A cancelar a conversão…";
    }


    private async Task<string> ConvertOfficeDocumentAsync(
        ReadOnlyMemory<byte> content,
        string extension,
        CancellationToken cancellationToken)
    {
        await EnsureDocumentRuntimeAsync(cancellationToken);
        var inputDirectory = Path.Combine(_wasmRuntimePath, "inputs");
        Directory.CreateDirectory(inputDirectory);
        var id = Guid.NewGuid().ToString("N");
        var fileName = id + "." + extension;
        var inputPath = Path.Combine(inputDirectory, fileName);
        await File.WriteAllBytesAsync(inputPath, content.ToArray(), cancellationToken);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingDocuments[id] = completion;
        try
        {
            var webView = GetOrCreateDocumentConverterWebView();
            var request = JsonSerializer.Serialize(new { type = "convert", id, fileName, extension });
            webView.CoreWebView2.PostWebMessageAsJson(request);
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
        }
        finally
        {
            _pendingDocuments.TryRemove(id, out _);
            if (File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    private async Task EnsureDocumentRuntimeAsync(CancellationToken cancellationToken)
    {
        _wasmInitialization ??= InitializeDocumentRuntimeAsync();
        await _wasmInitialization.WaitAsync(cancellationToken);
        await _wasmReady.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
    }

    private async Task InitializeDocumentRuntimeAsync()
    {
        Directory.CreateDirectory(_wasmRuntimePath);
        Directory.CreateDirectory(Path.Combine(_wasmRuntimePath, "wasm"));
        var packageAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "Converter");
        CopyIfUpdated(Path.Combine(packageAssets, "bridge.html"), Path.Combine(_wasmRuntimePath, "bridge.html"));
        CopyIfUpdated(Path.Combine(packageAssets, "wasm", "anydoc_wasm.js"), Path.Combine(_wasmRuntimePath, "wasm", "anydoc_wasm.js"));
        CopyIfUpdated(Path.Combine(packageAssets, "wasm", "anydoc_wasm_bg.wasm"), Path.Combine(_wasmRuntimePath, "wasm", "anydoc_wasm_bg.wasm"));
        var webView = GetOrCreateDocumentConverterWebView();
        await webView.EnsureCoreWebView2Async();
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "mediaforge.local",
            _wasmRuntimePath,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        webView.CoreWebView2.WebMessageReceived += DocumentConverterWebView_WebMessageReceived;
        webView.Source = new Uri("https://mediaforge.local/bridge.html");
    }

    private Microsoft.UI.Xaml.Controls.WebView2 GetOrCreateDocumentConverterWebView()
    {
        if (_documentConverterWebView is not null)
        {
            return _documentConverterWebView;
        }

        var webView = new Microsoft.UI.Xaml.Controls.WebView2
        {
            Width = 2,
            Height = 2,
            Opacity = 0,
            IsHitTestVisible = false
        };
        Grid.SetRow(webView, 2);
        PageRoot.Children.Add(webView);
        _documentConverterWebView = webView;
        return webView;
    }

    private void DocumentConverterWebView_WebMessageReceived(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        using var document = JsonDocument.Parse(args.WebMessageAsJson);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        if (type == "ready")
        {
            _wasmReady.TrySetResult();
            return;
        }
        if (!root.TryGetProperty("id", out var idValue) || !_pendingDocuments.TryGetValue(idValue.GetString() ?? string.Empty, out var completion))
        {
            return;
        }
        if (type == "result")
        {
            completion.TrySetResult(root.GetProperty("markdown").GetString() ?? string.Empty);
        }
        else if (type == "error")
        {
            completion.TrySetException(new InvalidOperationException(root.GetProperty("message").GetString() ?? "Erro ao converter o documento."));
        }
    }

    private static void CopyIfUpdated(string source, string destination)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Os recursos do conversor de documentos não foram incluídos na aplicação.", source);
        }
        if (!File.Exists(destination)
            || new FileInfo(source).Length != new FileInfo(destination).Length
            || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(destination))
        {
            File.Copy(source, destination, overwrite: true);
        }
    }

    private void InitializePicker(object picker)
    {
        if (App.MainWindow is null)
        {
            throw new InvalidOperationException("A janela principal ainda não está disponível.");
        }
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker,
            WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
    }

    private static bool IsExpectedConversionException(Exception exception)
    {
        return exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
            or JsonException
            or HttpRequestException
            or TimeoutException
            or OperationCanceledException;
    }
}
