using Imaging.Core;
using System.Diagnostics;

namespace Windows.Imaging.Utility;

public partial class MainForm
{
    private async Task RunOperationAsync(
        FfuOperationKind kind,
        ImagingDiskInfo disk,
        string imagePath,
        Func<string, IProgress<FfuOperationProgress>, CancellationToken, Task<FfuOperationResult>> operation)
    {
        string operationName = kind == FfuOperationKind.Apply ? "Apply FFU" : "Capture FFU";
        if (!TryBeginOperation(operationName, disk))
            return;

        string operationImagePath = kind == FfuOperationKind.Capture && File.Exists(imagePath)
            ? CreateSiblingTemporaryOutputPath(imagePath)
            : imagePath;

        UpdateSelectedDiskPanel();
        Enabled = false;

        using OperationProgressDialog progressDialog = new(kind, disk, imagePath);
        CancellationTokenSource cts = new();
        progressDialog.CancelRequested += (_, _) => cts.Cancel();
        progressDialog.Show(this);

        Progress<FfuOperationProgress> progress = new(update => progressDialog.UpdateProgress(update));
        FfuOperationResult result;
        try
        {
            result = await operation(operationImagePath, progress, cts.Token);
        }
        catch (Exception ex)
        {
            result = new FfuOperationResult { Success = false, ExitCode = -1, Output = ex.Message };
        }
        finally
        {
            progressDialog.AllowClose();
            progressDialog.Close();
            cts.Dispose();
            EndOperation();
            Enabled = true;
            Activate();
        }

        if (kind == FfuOperationKind.Capture && result.Success && !result.Canceled &&
            !string.Equals(operationImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryCommitTemporaryOutput(operationImagePath, imagePath, out string? commitError))
            {
                result = new FfuOperationResult
                {
                    Success = false,
                    ExitCode = -1,
                    Output = "The replacement FFU was captured successfully, but Windows Imaging Utility could not safely replace the existing file.\n\n" + commitError
                };
            }
        }

        if (kind == FfuOperationKind.Capture && (!result.Success || result.Canceled))
            TryDeletePartialCaptureOutput(operationImagePath);

        if (result.Canceled)
        {
            MessageBox.Show(
                this,
                kind == FfuOperationKind.Apply
                    ? "The FFU apply operation was canceled. The target disk may be incomplete and should not be booted until a successful image is applied."
                    : "The FFU capture operation was canceled.",
                kind == FfuOperationKind.Apply ? "Apply Canceled" : "Capture Canceled",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        else if (!result.Success)
        {
            string details = string.IsNullOrWhiteSpace(result.Output) ? $"DISM exited with code {result.ExitCode}." : result.Output;
            MessageBox.Show(this, details, kind == FfuOperationKind.Apply ? "Apply FFU Failed" : "Capture FFU Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            MessageBox.Show(
                this,
                kind == FfuOperationKind.Apply ? "The FFU was applied successfully." : $"The FFU was captured successfully.\n\n{imagePath}",
                kind == FfuOperationKind.Apply ? "Apply FFU" : "Capture FFU",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        await RequestDiskRefreshAsync(disk.DiskNumber);
    }


    private static void TryDeletePartialCaptureOutput(string imagePath)
    {
        try
        {
            if (File.Exists(imagePath))
                File.Delete(imagePath);
        }
        catch
        {
        }
    }

    private string? RunFilePicker(bool save, string title) =>
        RunFilePicker(save, title, ".ffu");

    private string? RunFilePicker(
        bool save,
        string title,
        string extension,
        string? initialPath = null)
    {
        try
        {
            using FileDialog dialog = save
                ? new SaveFileDialog
                {
                    AddExtension = true,
                    CheckPathExists = true,
                    CreatePrompt = false,
                    OverwritePrompt = true
                }
                : new OpenFileDialog
                {
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false
                };

            // The Windows edition intentionally uses only the modern Common Item
            // Dialog. Do not add a legacy picker fallback here; if the environment
            // cannot create the modern dialog, surface that failure to the user.
            dialog.AutoUpgradeEnabled = true;
            dialog.Title = title;
            dialog.Filter = BuildNativeFileDialogFilter(extension);
            dialog.FilterIndex = 1;
            dialog.RestoreDirectory = true;

            string[] extensions = ParseDialogExtensions(extension);
            if (save && extensions.Length > 0)
                dialog.DefaultExt = extensions[0].TrimStart('.');

            ApplyDialogInitialPath(dialog, initialPath);

            return dialog.ShowDialog(this) == DialogResult.OK
                ? dialog.FileName
                : null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "The modern Windows file picker could not be opened.\n\n" + ex.Message,
                "Windows Imaging Utility",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
    }

    private string? RunFolderPicker(string title)
    {
        try
        {
            using FolderBrowserDialog dialog = new()
            {
                AutoUpgradeEnabled = true,
                Description = title,
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            return dialog.ShowDialog(this) == DialogResult.OK
                ? dialog.SelectedPath
                : null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "The modern Windows folder picker could not be opened.\n\n" + ex.Message,
                "Windows Imaging Utility",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return null;
        }
    }

    private static string[] ParseDialogExtensions(string extension) =>
        extension
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.StartsWith('.') ? value : "." + value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildNativeFileDialogFilter(string extension)
    {
        string[] extensions = ParseDialogExtensions(extension);
        if (extensions.Length == 0)
            return "All files (*.*)|*.*";

        string pattern = string.Join(";", extensions.Select(static value => "*" + value));
        string label = extensions switch
        {
            [".wim"] => "Windows Imaging Format",
            [".swm"] => "Split Windows Image",
            [".ffu"] => "Full Flash Update",
            _ when extensions.All(static value => value is ".wim" or ".swm") => "Windows image files",
            _ => "Supported image files"
        };

        return $"{label} ({pattern})|{pattern}|All files (*.*)|*.*";
    }

    private static void ApplyDialogInitialPath(FileDialog dialog, string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath))
            return;

        try
        {
            string fullPath = Path.GetFullPath(initialPath);
            if (Directory.Exists(fullPath))
            {
                dialog.InitialDirectory = fullPath;
                return;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                dialog.InitialDirectory = directory;

            if (!string.IsNullOrWhiteSpace(Path.GetFileName(fullPath)))
                dialog.FileName = Path.GetFileName(fullPath);
        }
        catch
        {
            // An initial hint should never prevent the picker from opening.
        }
    }

    private void LaunchBitLockerManager(string? mountPoint)
    {
        try
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = "control.exe",
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("/name");
            startInfo.ArgumentList.Add("Microsoft.BitLockerDriveEncryption");

            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Windows could not open BitLocker Drive Encryption.\n\n" + ex.Message,
                "BitLocker Drive Encryption",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

}
