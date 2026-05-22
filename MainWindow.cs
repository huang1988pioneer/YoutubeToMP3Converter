using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace YoutubeToMP3Converter;

public sealed class MainWindow : Window
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding SystemAnsiEncoding = GetSystemAnsiEncoding();

    private readonly TextBox[] _urlBoxes;
    private readonly TextBox _outputBox;
    private readonly Button _chooseFolderButton;
    private readonly Button _clearUrlsButton;
    private readonly Button _convertButton;
    private readonly TextBlock _statusText;
    private readonly TextBox _logBox;
    private readonly ProgressBar _progressBar;

    private CancellationTokenSource? _conversionTokenSource;

    public MainWindow()
    {
        Title = "YouTube to MP3 Converter";
        Width = 820;
        Height = 620;
        MinWidth = 680;
        MinHeight = 520;
        Background = Brush.Parse("#F6F7F9");

        _urlBoxes =
        [
            CreateUrlBox("網址 1"),
            CreateUrlBox("網址 2"),
            CreateUrlBox("網址 3")
        ];

        _outputBox = new TextBox
        {
            Text = AppSettings.Load().LastOutputFolder,
            IsReadOnly = false,
            FontSize = 14,
            MinHeight = 38
        };
        _outputBox.LostFocus += (_, _) => SaveOutputFolderIfValid();

        _chooseFolderButton = new Button
        {
            Content = "選擇資料夾",
            MinWidth = 112,
            MinHeight = 38
        };
        _chooseFolderButton.Click += ChooseFolderAsync;

        _clearUrlsButton = new Button
        {
            Content = "清除網址",
            MinWidth = 112,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _clearUrlsButton.Click += ClearUrls;

        _convertButton = new Button
        {
            Content = "轉成 MP3",
            MinWidth = 128,
            MinHeight = 42,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _convertButton.Click += ConvertOrCancelAsync;

        _statusText = new TextBlock
        {
            Text = "準備就緒",
            FontSize = 14,
            Foreground = Brush.Parse("#394150"),
            VerticalAlignment = VerticalAlignment.Center
        };

        _progressBar = new ProgressBar
        {
            IsIndeterminate = false,
            Height = 6,
            Minimum = 0,
            Maximum = 100
        };

        _logBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Microsoft JhengHei UI, monospace"),
            FontSize = 12,
            Background = Brush.Parse("#111827"),
            Foreground = Brush.Parse("#F9FAFB"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14),
            MinHeight = 210
        };

        Content = BuildLayout();
        Opened += (_, _) => CheckTools();
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(28)
        };

        var header = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 22)
        };
        header.Children.Add(new TextBlock
        {
            Text = "YouTube to MP3 Converter",
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#111827")
        });
        header.Children.Add(new TextBlock
        {
            Text = "貼上最多三個 YouTube 連結，選擇輸出資料夾後轉換成 MP3。",
            FontSize = 14,
            Foreground = Brush.Parse("#5F6877")
        });

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"),
            RowSpacing = 16
        };

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        body.Children.Add(CreateField("YouTube 網址", BuildUrlInputs(), 0));

        var outputRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        outputRow.Children.Add(_outputBox);
        Grid.SetColumn(_chooseFolderButton, 1);
        outputRow.Children.Add(_chooseFolderButton);
        body.Children.Add(CreateField("輸出資料夾", outputRow, 1));

        var actionRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 10
        };
        actionRow.Children.Add(_statusText);
        Grid.SetColumn(_clearUrlsButton, 1);
        actionRow.Children.Add(_clearUrlsButton);
        Grid.SetColumn(_convertButton, 2);
        actionRow.Children.Add(_convertButton);
        Grid.SetRow(actionRow, 2);
        body.Children.Add(actionRow);

        Grid.SetRow(_progressBar, 3);
        body.Children.Add(_progressBar);

        var logPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8
        };
        logPanel.Children.Add(new TextBlock
        {
            Text = "記錄",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#394150")
        });
        Grid.SetRow(_logBox, 1);
        logPanel.Children.Add(_logBox);
        Grid.SetRow(logPanel, 4);
        body.Children.Add(logPanel);

        return root;
    }

    private static Control CreateField(string label, Control content, int row)
    {
        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#394150")
        });
        panel.Children.Add(content);
        Grid.SetRow(panel, row);
        return panel;
    }

    private static TextBox CreateUrlBox(string label)
    {
        return new TextBox
        {
            PlaceholderText = $"{label}: 貼上 YouTube 影片或播放清單網址",
            FontSize = 15,
            MinHeight = 40
        };
    }

    private Control BuildUrlInputs()
    {
        var panel = new StackPanel { Spacing = 8 };

        foreach (var urlBox in _urlBoxes)
        {
            panel.Children.Add(urlBox);
        }

        return panel;
    }

    private async void ChooseFolderAsync(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "選擇 MP3 輸出資料夾",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is not null && folder.TryGetLocalPath() is { } path)
        {
            _outputBox.Text = path;
            SaveOutputFolderIfValid();
            SetStatus("輸出資料夾已更新");
        }
    }

    private void ClearUrls(object? sender, RoutedEventArgs e)
    {
        foreach (var urlBox in _urlBoxes)
        {
            urlBox.Text = "";
        }

        SetStatus("網址已清除");
    }

    private async void ConvertOrCancelAsync(object? sender, RoutedEventArgs e)
    {
        if (_conversionTokenSource is not null)
        {
            _conversionTokenSource.Cancel();
            return;
        }

        var urls = _urlBoxes
            .Select(box => box.Text?.Trim())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Cast<string>()
            .Take(3)
            .ToArray();

        if (urls.Length == 0)
        {
            SetStatus("請至少輸入一個 YouTube 網址");
            return;
        }

        var outputPath = _outputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
        {
            SetStatus("請選擇有效的輸出資料夾");
            return;
        }
        AppSettings.Save(outputPath);

        var ytDlpPath = ToolLocator.FindExecutable("yt-dlp");
        if (ytDlpPath is null)
        {
            SetStatus("找不到 yt-dlp，請先安裝後再試");
            AppendInstallHint();
            return;
        }

        _conversionTokenSource = new CancellationTokenSource();
        SetBusy(true);
        _logBox.Text = "";
        AppendLog($"yt-dlp: {ytDlpPath}");
        AppendLog($"輸出資料夾: {outputPath}");
        AppendLog($"準備轉換 {urls.Length} 個項目");

        try
        {
            var successCount = 0;
            for (var index = 0; index < urls.Length; index++)
            {
                var current = index + 1;
                SetStatus($"正在轉換 {current}/{urls.Length}...");
                AppendLog("");
                AppendLog($"[{current}/{urls.Length}] {urls[index]}");

                var code = await RunYtDlpAsync(ytDlpPath, urls[index], outputPath, _conversionTokenSource.Token);
                if (code == 0)
                {
                    successCount++;
                }
                else
                {
                    AppendLog($"[{current}/{urls.Length}] 轉換失敗，結束碼 {code}");
                }
            }

            SetStatus(successCount == urls.Length
                ? $"完成，已輸出 {successCount} 個 MP3"
                : $"完成 {successCount}/{urls.Length} 個，請查看記錄");
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消");
            AppendLog("使用者取消轉換。");
        }
        catch (Exception ex)
        {
            SetStatus("轉換時發生錯誤");
            AppendLog(ex.Message);
        }
        finally
        {
            _conversionTokenSource?.Dispose();
            _conversionTokenSource = null;
            SetBusy(false);
        }
    }

    private async Task<int> RunYtDlpAsync(string ytDlpPath, string url, string outputPath, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ytDlpPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUTF8"] = "1";

        startInfo.ArgumentList.Add("--extract-audio");
        startInfo.ArgumentList.Add("--audio-format");
        startInfo.ArgumentList.Add("mp3");
        startInfo.ArgumentList.Add("--audio-quality");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--encoding");
        startInfo.ArgumentList.Add("utf-8");
        startInfo.ArgumentList.Add("--embed-thumbnail");
        startInfo.ArgumentList.Add("--add-metadata");
        startInfo.ArgumentList.Add("--paths");
        startInfo.ArgumentList.Add(outputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add("%(title)s.%(ext)s");
        startInfo.ArgumentList.Add(url);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => AppendLog(args.Data);
        process.ErrorDataReceived += (_, args) => AppendLog(args.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException("無法啟動 yt-dlp。");
        }

        var outputTask = ReadProcessStreamAsync(process.StandardOutput.BaseStream, token);
        var errorTask = ReadProcessStreamAsync(process.StandardError.BaseStream, token);

        try
        {
            await process.WaitForExitAsync(token);
            await Task.WhenAll(outputTask, errorTask);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return process.ExitCode;
    }

    private async Task ReadProcessStreamAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        var pending = new List<byte>();

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++)
            {
                var value = buffer[index];
                if (value == (byte)'\n')
                {
                    AppendDecodedLogLine(pending);
                    pending.Clear();
                    continue;
                }

                pending.Add(value);
            }
        }

        AppendDecodedLogLine(pending);
    }

    private void AppendDecodedLogLine(List<byte> bytes)
    {
        while (bytes.Count > 0 && bytes[^1] == (byte)'\r')
        {
            bytes.RemoveAt(bytes.Count - 1);
        }

        if (bytes.Count == 0)
        {
            return;
        }

        AppendLog(DecodeProcessText(bytes.ToArray()));
    }

    private static string DecodeProcessText(byte[] bytes)
    {
        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SystemAnsiEncoding.GetString(bytes);
        }
    }

    private static Encoding GetSystemAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private void CheckTools()
    {
        var ytDlp = ToolLocator.FindExecutable("yt-dlp");
        var ffmpeg = ToolLocator.FindExecutable("ffmpeg");

        if (ytDlp is null || ffmpeg is null)
        {
            SetStatus("需要 yt-dlp 和 ffmpeg 才能轉換 MP3");
            AppendInstallHint();
            AppendLog($"yt-dlp: {ytDlp ?? "找不到"}");
            AppendLog($"ffmpeg: {ffmpeg ?? "找不到"}");
            return;
        }

        AppendLog($"yt-dlp: {ytDlp}");
        AppendLog($"ffmpeg: {ffmpeg}");
    }

    private void AppendInstallHint()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            AppendLog("Windows 第一次使用前，請先安裝轉檔工具：");
            AppendLog("1. 按 Windows 鍵，輸入「終端機」");
            AppendLog("2. 在「終端機」上按右鍵，選擇「以系統管理員身分執行」");
            AppendLog("3. 貼上這行指令後按 Enter：");
            AppendLog("   winget install yt-dlp.yt-dlp Gyan.FFmpeg");
            AppendLog("4. 如果畫面詢問是否同意，輸入 Y 後按 Enter");
            AppendLog("5. 安裝完成後，重新開啟這個程式");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            AppendLog("macOS 可用 Homebrew 安裝：brew install yt-dlp ffmpeg");
            return;
        }

        AppendLog("請用系統套件管理器安裝 yt-dlp 和 ffmpeg，並確認兩者可從 PATH 執行。");
    }

    private void SetBusy(bool busy)
    {
        _progressBar.IsIndeterminate = busy;
        _chooseFolderButton.IsEnabled = !busy;
        _clearUrlsButton.IsEnabled = !busy;
        foreach (var urlBox in _urlBoxes)
        {
            urlBox.IsEnabled = !busy;
        }
        _outputBox.IsEnabled = !busy;
        _convertButton.Content = busy ? "取消" : "轉成 MP3";
    }

    private void SaveOutputFolderIfValid()
    {
        var outputPath = _outputBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(outputPath) && Directory.Exists(outputPath))
        {
            AppSettings.Save(outputPath);
        }
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _logBox.Text += $"{line}{Environment.NewLine}";
            _logBox.CaretIndex = _logBox.Text.Length;
        });
    }
}

internal sealed class AppSettings
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YoutubeToMP3Converter");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public string LastOutputFolder { get; init; } = GetDefaultOutputFolder();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings is not null && Directory.Exists(settings.LastOutputFolder))
                {
                    return settings;
                }
            }
        }
        catch
        {
            // Invalid settings should not stop the app from opening.
        }

        return new AppSettings();
    }

    public static void Save(string outputFolder)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var settings = new AppSettings { LastOutputFolder = outputFolder };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // The converter can still work even if preferences cannot be saved.
        }
    }

    private static string GetDefaultOutputFolder()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}

internal static class ToolLocator
{
    private static readonly string[] UnixSearchPaths =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/usr/bin",
        "/bin"
    ];

    public static string? FindExecutable(string name)
    {
        var executableNames = GetExecutableNames(name);
        var searchPaths = GetSearchPaths();

        foreach (var path in searchPaths)
        {
            foreach (var executableName in executableNames)
            {
                var candidate = Path.Combine(path, executableName);
                if (File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetExecutableNames(string name)
    {
        yield return name;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Path.HasExtension(name))
        {
            yield break;
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var extension in extensions)
        {
            yield return $"{name}{extension.ToLowerInvariant()}";
        }
    }

    private static IEnumerable<string> GetSearchPaths()
    {
        IEnumerable<string> paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            paths = paths.Concat(UnixSearchPaths);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
