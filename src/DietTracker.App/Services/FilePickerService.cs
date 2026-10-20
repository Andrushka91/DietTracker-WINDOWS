using DietTracker.Core.Interfaces;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DietTracker.App.Services;

/// <summary>Native Windows file and folder pickers, correctly parented to the main window.</summary>
public sealed class FilePickerService : IFilePickerService
{
    private readonly IUserInterfaceContext _uiContext;

    public FilePickerService(IUserInterfaceContext uiContext) => _uiContext = uiContext;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickImagesAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var picker = new FileOpenPicker();
        Initialize(picker);

        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.ViewMode = PickerViewMode.Thumbnail;
        foreach (var extension in Core.BusinessRules.PhotoRules.AllowedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var files = await picker.PickMultipleFilesAsync();
        return files is null
            ? []
            : files.Take(Math.Max(1, maxCount)).Select(f => f.Path).ToArray();
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(string suggestedFileName, string extension, CancellationToken cancellationToken = default)
    {
        var picker = new FileSavePicker();
        Initialize(picker);

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = suggestedFileName;
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), [extension]);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <inheritdoc />
    public async Task<string?> PickOpenFileAsync(string extension, CancellationToken cancellationToken = default)
    {
        var picker = new FileOpenPicker();
        Initialize(picker);

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(extension);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var picker = new FolderPicker();
        Initialize(picker);

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    /// <inheritdoc />
    public async Task OpenWithShellAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        // Opens the full size photograph in the user's default image viewer.
        var file = await StorageFile.GetFileFromPathAsync(path);
        await Windows.System.Launcher.LaunchFileAsync(file);
    }

    private void Initialize(object picker)
    {
        var handle = _uiContext.GetWindowHandle();
        if (handle != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        }
    }
}
