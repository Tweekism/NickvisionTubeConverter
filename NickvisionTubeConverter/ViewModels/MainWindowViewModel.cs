using FluentAvalonia.UI.Controls;
using Nickvision.Avalonia.Extensions;
using Nickvision.Avalonia.Models;
using Nickvision.Avalonia.MVVM;
using Nickvision.Avalonia.MVVM.Commands;
using Nickvision.Avalonia.MVVM.Services;
using Nickvision.Avalonia.Update;
using NickvisionTubeConverter.Extensions;
using NickvisionTubeConverter.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Enums;

namespace NickvisionTubeConverter.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly ServiceCollection _serviceCollection;
    private readonly HttpClient _httpClient;
    private string? _videoURL;
    private string? _saveFolder;
    private FileFormat? _fileFormat;
    private string? _newFilename;
    private int _activeDownloadsCount;

    public string Status => $"Remaining Downloads: {_activeDownloadsCount}";

    public ObservableCollection<FileFormat> FileFormats { get; init; }
    public ObservableCollection<Download> Downloads { get; init; }
    public DelegateAsyncCommand<object?> OpenedCommand { get; init; }
    public DelegateAsyncCommand<object?> ClosingCommand { get; init; }
    public DelegateAsyncCommand<object> SelectSaveFolderCommand { get; init; }
    public DelegateCommand<object> GoToSaveFolderCommand { get; init; }
    public DelegateCommand<ICloseable?> ExitCommand { get; init; }
    public DelegateAsyncCommand<object?> SettingsCommand { get; init; }
    public DelegateAsyncCommand<object> DownloadVideoCommand { get; init; }
    public DelegateCommand<object> ClearCompletedDownloadsCommand { get; init; }
    public DelegateAsyncCommand<ICloseable?> CheckForUpdatesCommand { get; init; }
    public DelegateCommand<object?> GitHubRepoCommand { get; init; }
    public DelegateCommand<object?> ReportABugCommand { get; init; }
    public DelegateAsyncCommand<object?> ChangelogCommand { get; init; }
    public DelegateAsyncCommand<object?> AboutCommand { get; init; }

    public MainWindowViewModel(ServiceCollection serviceCollection)
    {
        Title = "Nickvision Tube Converter";
        _serviceCollection = serviceCollection;
        _httpClient = new HttpClient();
        _activeDownloadsCount = 0;
        FileFormats = EnumExtensions.GetObservableCollection<FileFormat>();
        Downloads = new ObservableCollection<Download>();
        OpenedCommand = new DelegateAsyncCommand<object?>(Opened);
        ClosingCommand = new DelegateAsyncCommand<object?>(Closing);
        SelectSaveFolderCommand = new DelegateAsyncCommand<object>(SelectSaveFolder);
        GoToSaveFolderCommand = new DelegateCommand<object>(GoToSaveFolder, () => !string.IsNullOrEmpty(SaveFolder));
        ExitCommand = new DelegateCommand<ICloseable?>(Exit);
        SettingsCommand = new DelegateAsyncCommand<object?>(Settings);
        DownloadVideoCommand = new DelegateAsyncCommand<object>(DownloadVideo, () => !string.IsNullOrEmpty(VideoURL) && (VideoURL.StartsWith("https://www.youtube.com/watch?v=") || VideoURL.StartsWith("http://www.youtube.com/watch?v=")) && !string.IsNullOrEmpty(SaveFolder) && FileFormat != null && !string.IsNullOrEmpty(NewFilename));
        ClearCompletedDownloadsCommand = new DelegateCommand<object>(ClearCompletedDownloads, () => Downloads.Count != 0);
        CheckForUpdatesCommand = new DelegateAsyncCommand<ICloseable?>(CheckForUpdates);
        GitHubRepoCommand = new DelegateCommand<object?>(GitHubRepo);
        ReportABugCommand = new DelegateCommand<object?>(ReportAbug);
        ChangelogCommand = new DelegateAsyncCommand<object?>(Changelog);
        AboutCommand = new DelegateAsyncCommand<object?>(About);
    }

    public string? VideoURL
    {
        get => _videoURL;

        set
        {
            SetProperty(ref _videoURL, value);
            DownloadVideoCommand.RaiseCanExecuteChanged();
        }
    }

    public string? SaveFolder
    {
        get => _saveFolder;

        set
        {
            SetProperty(ref _saveFolder, value);
            GoToSaveFolderCommand.RaiseCanExecuteChanged();
        }
    }

    public FileFormat? FileFormat
    {
        get => _fileFormat;

        set
        {
            SetProperty(ref _fileFormat, value);
            DownloadVideoCommand.RaiseCanExecuteChanged();
            if(value != null)
            {
                var configuration = Configuration.Load();
                configuration.PreviousFileFormat = (FileFormat)FileFormat!;
                configuration.Save();
            }
        }
    }

    public string? NewFilename
    {
        get => _newFilename;

        set
        {
            SetProperty(ref _newFilename, value);
            DownloadVideoCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task Opened(object? parameter)
    {
        var config = await Configuration.LoadAsync();
        _serviceCollection.GetService<IThemeService>()?.ChangeTheme(config.Theme);
        _serviceCollection.GetService<IThemeService>()?.ChangeAccentColor(config.AccentColor);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            _serviceCollection.GetService<IThemeService>()?.ForceWin32WindowToTheme();
        }
        if (Directory.Exists(config.PreviousSaveFolder))
        {
            SaveFolder = config.PreviousSaveFolder;
        }
        FileFormat = config.PreviousFileFormat;
        FFmpeg.ExecutablesPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}{Path.DirectorySeparatorChar}Nickvision{Path.DirectorySeparatorChar}NickvisionTubeConverter";
        await _serviceCollection.GetService<IProgressDialogService>()?.ShowAsync("Downloading required dependencies...", async () =>
        {
            try
            {
                await FFmpeg.GetLatestVersion(FFmpegVersion.Official);
            }
            catch
            {
                _serviceCollection.GetService<IInfoBarService>()?.ShowCloseableNotification("Download Failed", "Unable To download required dependencies. Please make sure you are connect to the internet and restart the application to try again.", InfoBarSeverity.Error);
            }
        })!;
    }

    private async Task Closing(object? parameter)
    {
        var config = await Configuration.LoadAsync();
        await config.SaveAsync();
    }

    private async Task SelectSaveFolder(object? parameter)
    {
        var result = await _serviceCollection.GetService<IIOService>()?.ShowOpenFolderDialogAsync("Select Save Folder")!;
        if (result != null)
        {
            SaveFolder = result;
            var configuration = await Configuration.LoadAsync();
            configuration.PreviousSaveFolder = SaveFolder;
            await configuration.SaveAsync();
        }
    }

    private void GoToSaveFolder(object? parameter) => Process.Start(new ProcessStartInfo(SaveFolder!) { UseShellExecute = true });

    private void Exit(ICloseable? parameter) => parameter?.Close();

    private async Task Settings(object? parameter) => await _serviceCollection.GetService<IContentDialogService>()?.ShowCustomAsync(new SettingsDialogViewModel(_serviceCollection))!;

    private async Task DownloadVideo(object? parameter)
    {
        if (_activeDownloadsCount < (await Configuration.LoadAsync()).MaxNumberOfActiveDownloads)
        {
            var download = new Download(VideoURL!, SaveFolder!, NewFilename!, (FileFormat)FileFormat);
            Downloads.Add(download);
            _activeDownloadsCount++;
            OnPropertyChanged("Status");
            VideoURL = "";
            NewFilename = "";
            DownloadVideoCommand.IsExecuting = false;
            try
            {
                await download.DownloadAsync();
            }
            catch (Exception ex)
            {
                download.Status = "Error";
                await _serviceCollection.GetService<IContentDialogService>()?.ShowMessageAsync(new ContentDialogMessageInfo()
                {
                    Title = $"Error: {ex.Message}",
                    Message = ex.StackTrace,
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close
                })!;
            }
            _activeDownloadsCount--;
            OnPropertyChanged("Status");
            ClearCompletedDownloadsCommand.RaiseCanExecuteChanged();
        }
        else
        {
            _serviceCollection.GetService<IInfoBarService>()?.ShowCloseableNotification("Please Wait", "The max number of active downloads has been reached. Please wait for one to finish before continuing. You can change the max number of active downloads in settings.", InfoBarSeverity.Warning);
        }
    }

    private void ClearCompletedDownloads(object? parameter)
    {
        Downloads.Clear();
        ClearCompletedDownloadsCommand.RaiseCanExecuteChanged();
    }

    private async Task CheckForUpdates(ICloseable? parameter)
    {
        var updater = new Updater(_httpClient, new Uri("https://raw.githubusercontent.com/nlogozzo/NickvisionTubeConverter/main/UpdateConfig.json"), new Version("2022.2.0"));
        await _serviceCollection.GetService<IProgressDialogService>()?.ShowAsync("Checking for updates...", async () => await updater.CheckForUpdatesAsync())!;
        if (updater.UpdateAvailable)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var result = await _serviceCollection.GetService<IContentDialogService>()?.ShowMessageAsync(new ContentDialogMessageInfo()
                {
                    Title = "Update Available",
                    Message = $"===V{updater.LatestVersion} Changelog===\n{updater.Changelog}\n\nNickvisionApp will automatically download and install the update, please save all work before continuing. Are you ready to update?",
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No",
                    DefaultButton = ContentDialogButton.Close
                })!;
                if (result == ContentDialogResult.Primary)
                {
                    var updateSuccess = false;
                    await _serviceCollection.GetService<IProgressDialogService>()?.ShowAsync("Downloading and installing the update...", async () => updateSuccess = await updater.WindowsUpdateAsync(parameter!))!;
                    if (!updateSuccess)
                    {
                        _serviceCollection.GetService<IInfoBarService>()?.ShowCloseableNotification("Error", "An unknown error occurred when trying to download and install the update.", InfoBarSeverity.Error);
                    }
                }
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                var result = await _serviceCollection.GetService<IContentDialogService>()?.ShowMessageAsync(new ContentDialogMessageInfo()
                {
                    Title = "Update Available",
                    Message = $"===V{updater.LatestVersion} Changelog===\n{updater.Changelog}\n\nNickvisionApp will automatically download the updated application to your downloads directory. If the app is currently running from your downloads directory, please move it before updating. Are you ready to update?",
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No",
                    DefaultButton = ContentDialogButton.Close
                })!;
                if (result == ContentDialogResult.Primary)
                {
                    var updateSuccess = false;
                    await _serviceCollection.GetService<IProgressDialogService>()?.ShowAsync("Downloading the update...", async () => updateSuccess = await updater.LinuxUpdateAsync("NickvisionTubeConverter"))!;
                    if (updateSuccess)
                    {
                        _serviceCollection.GetService<IInfoBarService>()?.ShowCloseableNotification("Update Completed", "The update has been downloaded to your downloads directory. We recommend moving the exe out of your downloads directory and running it somewhere else.", InfoBarSeverity.Success);
                    }
                    else
                    {
                        _serviceCollection.GetService<IInfoBarService>()?.ShowCloseableNotification("Error", "An unknown error occurred when trying to download and install the update.", InfoBarSeverity.Error);
                    }
                }
            }
            else
            {
                await _serviceCollection.GetService<IContentDialogService>()?.ShowMessageAsync(new ContentDialogMessageInfo()
                {
                    Title = "Update Available",
                    Message = $"===V{updater.LatestVersion} Changelog===\n{updater.Changelog}\n\nPlease visit the GitHub repo to download the latest release.",
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close
                })!;
            }
        }
        else
        {
            _serviceCollection.GetService<IInfoBarService>()?.ShowCloseableNotification("No Update Available", "There is no update at this time. Please try again later.", InfoBarSeverity.Error);
        }
    }

    private void GitHubRepo(object? parameter) => new Uri("https://github.com/nlogozzo/NickvisionTubeConverter").OpenInBrowser();

    private void ReportAbug(object? parameter) => new Uri("https://github.com/nlogozzo/NickvisionTubeConverter/issues/new").OpenInBrowser();

    private async Task Changelog(object? parameter)
    {
        await _serviceCollection.GetService<IContentDialogService>()?.ShowMessageAsync(new ContentDialogMessageInfo()
        {
            Title = "What's New?",
            Message = "- Rewrote application in C# and Avalonia",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        })!;
    }

    private async Task About(object? parameter)
    {
        await _serviceCollection.GetService<IContentDialogService>()?.ShowMessageAsync(new ContentDialogMessageInfo()
        {
            Title = "About",
            Message = "Nickvision Tube Converter Version 2022.2.0-alpha1\nA template for creating Nickvision applications.\n\nBuilt with C# and Avalonia\n(C) Nickvision 2021-2022",
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close
        })!;
    }
}