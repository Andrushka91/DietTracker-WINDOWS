using DietTracker.Core.Models;

namespace DietTracker.Core.Validation;

/// <summary>Validates reminder configuration.</summary>
public static class ReminderValidator
{
    public const int MinSnoozeMinutes = 1;

    public const int MaxSnoozeMinutes = 120;

    public static ValidationResult Validate(ReminderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var failures = new List<ValidationFailure>();

        if (settings.Weekdays == 0)
        {
            failures.Add(new ValidationFailure("reminder.weekdays.empty", nameof(settings.Weekdays), "Select at least one weekday or disable the reminder."));
        }

        if ((settings.Weekdays & ~ReminderSettings.AllDays) != 0)
        {
            failures.Add(new ValidationFailure("reminder.weekdays.invalid", nameof(settings.Weekdays), "Weekday mask contains undefined bits."));
        }

        if (settings.SnoozeMinutes is < MinSnoozeMinutes or > MaxSnoozeMinutes)
        {
            failures.Add(new ValidationFailure("reminder.snooze.outOfRange", nameof(settings.SnoozeMinutes), $"Snooze must be between {MinSnoozeMinutes} and {MaxSnoozeMinutes} minutes."));
        }

        if (settings.MealCategoryId == Guid.Empty)
        {
            failures.Add(new ValidationFailure("reminder.category.missing", nameof(settings.MealCategoryId), "Reminder is not linked to a permanent meal category."));
        }

        return failures.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(failures);
    }
}

/// <summary>Validates templates before they are stored.</summary>
public static class MealTemplateValidator
{
    public const int MaxNameLength = 100;

    public static ValidationResult Validate(MealTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            failures.Add(new ValidationFailure("template.name.required", nameof(template.Name), "Template name is required."));
        }
        else if (template.Name.Length > MaxNameLength)
        {
            failures.Add(new ValidationFailure("template.name.tooLong", nameof(template.Name), $"Template name must not exceed {MaxNameLength} characters."));
        }

        if (template.IsFullDay && template.TemplateDay.Count == 0)
        {
            failures.Add(new ValidationFailure("template.day.empty", nameof(template.TemplateDay), "A full-day template must contain at least one meal."));
        }

        if (!template.IsFullDay && (template.TemplateMeal is null || template.TemplateMealSlot is null))
        {
            failures.Add(new ValidationFailure("template.meal.empty", nameof(template.TemplateMeal), "A single-meal template must carry meal content."));
        }

        return failures.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(failures);
    }
}
