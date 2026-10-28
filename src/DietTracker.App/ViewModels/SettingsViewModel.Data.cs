using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Data lifecycle commands of the settings screen: export, restore and the integrity check.
/// </summary>
/// <remarks>
/// The three actions are deliberately non destructive - a backup is written, a backup is merged and
/// the database is inspected - so none of them needs a confirmation that deletes something. The
/// destructive operations still exist in the services (the sign-out flow uses them), they are simply
/// not offered as buttons any more.
/// </remarks>
public sealed partial class SettingsViewModel
{
    /// <summary>Extension of the portable archive written by <see cref="IBackupService"/>.</summary>
    private const string BackupExtension = ".dtbackup";

    /// <summary>Extension of the printable report written by <see cref="IMealReportService"/>.</summary>
    private const string PdfExtension = ".pdf";

    /// <summary>
    /// Export asks for the file first and the chosen extension decides the content: the same button
    /// writes the portable archive (restorable in the application) or the printable PDF report (for a
    /// doctor, a dietitian or a printout).
    /// </summary>
    [RelayCommand]
    private async Task ExportDataAsync()
    {
        var path = await _filePicker.PickSaveFileAsync(
            $"diettracker-{DateTime.Now:yyyyMMdd}",
            [
                new SaveFileChoice(_localization.Get("Settings_ExportFormatBackup"), BackupExtension),
                new SaveFileChoice(_localization.Get("Settings_ExportFormatPdf"), PdfExtension),
            ]);

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        BackupStatusMessage = string.Empty;
        try
        {
            if (string.Equals(Path.GetExtension(path), PdfExtension, StringComparison.OrdinalIgnoreCase))
            {
                var report = await _reports.ExportPdfAsync(path, BuildReportOptions());
                BackupStatusMessage = _localization.Format("Settings_PdfCreated", report.MealEntries, report.Days);
                return;
            }

            var summary = await _backup.ExportAsync(path);
            BackupStatusMessage = _localization.Format("Settings_BackupCreated", summary.MealEntries, summary.Photos);
        }
        catch (Exception)
        {
            // The export may be the only copy the user has, so a failure is reported instead of
            // leaving an empty status line behind.
            BackupStatusMessage = _localization.Get("Settings_ExportFailed");
        }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        var path = await _filePicker.PickOpenFileAsync(BackupExtension);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync("Settings_RestoreTitle", "Settings_RestoreMessage");
        if (!confirmed)
        {
            return;
        }

        var summary = await _backup.RestoreAsync(path);
        BackupStatusMessage = _localization.Format("Settings_RestoreDone", summary.MealEntries, summary.Photos);
    }

    [RelayCommand]
    private async Task CheckIntegrityAsync()
    {
        var issues = await _integrity.CheckAsync();
        BackupStatusMessage = issues.Count == 0
            ? _localization.Get("Settings_IntegrityOk")
            : _localization.Format("Settings_IntegrityIssues", issues.Count, string.Join("; ", issues.Select(i => i.Code)));
    }

    /// <summary>
    /// Collects the localized texts and the culture of the PDF report. The report service has no
    /// access to the resource files, so naming the slots and the states is the job of this layer -
    /// which keeps exactly the labels the dashboard shows ("Mic dejun", "Finalizată", ...).
    /// </summary>
    private MealReportOptions BuildReportOptions() => new()
    {
        CultureName = CultureInfo.CurrentCulture.Name,
        TimeFormat = Use12HourClock ? "h:mm tt" : "HH:mm",
        DocumentTitle = _localization.Get("Report_Title"),
        PeriodFormat = _localization.Get("Report_Period"),
        GeneratedFormat = _localization.Get("Report_Generated"),
        SummaryFormat = _localization.Get("Report_Summary"),
        EmptyText = _localization.Get("Report_Empty"),
        PageFormat = _localization.Get("Report_Page"),
        CategoryNames = MealSlotRules.AllSlots.ToDictionary(
            key => key,
            key => _localization.Get($"Meal_{key}_Name")),
        StatusNames = new Dictionary<MealStatus, string>
        {
            [MealStatus.Done] = _localization.Get("Status_Done"),
            [MealStatus.Missed] = _localization.Get("Status_Missed"),
            [MealStatus.Pending] = _localization.Get("Status_Pending"),
        },
    };
}
