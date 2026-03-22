using NativeFileDialogSharp;

namespace KernSmith.Ui.Services;

public class FileDialogService
{
    public string? OpenFontFile()
    {
        var result = Dialog.FileOpen("ttf,otf,woff,ttc");
        return result.IsOk ? result.Path : null;
    }

    public string? SaveFile(string defaultName, string filter)
    {
        var result = Dialog.FileSave(filter);
        return result.IsOk ? result.Path : null;
    }
}
