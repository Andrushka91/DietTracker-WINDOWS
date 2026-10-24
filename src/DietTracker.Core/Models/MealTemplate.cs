namespace DietTracker.Core.Models;

/// <summary>
/// Reusable template, either for a single meal or for a whole day. Templates are deliberately
/// kept apart from real consumption history: they never carry status, completion data or photographs.
/// </summary>
public sealed class MealTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LocalProfileId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>True when the template describes a full day (all five slots).</summary>
    public bool IsFullDay { get; set; }

    /// <summary>
    /// Structured template payload (JSON). A single-meal template uses <see cref="TemplateMealSlot"/>
    /// plus <see cref="TemplateMeal"/>; a full-day template fills <see cref="TemplateDay"/>.
    /// </summary>
    public string TemplateData { get; set; } = "{}";

    /// <summary>Optional recurrence rule, for example "every Monday and Thursday" (iCalendar-like RRULE string).</summary>
    public string? RecurrenceRules { get; set; }

    /// <summary>Category of a single-meal template.</summary>
    public Enums.MealCategoryKey? TemplateMealSlot { get; set; }

    /// <summary>Serialized single-meal payload.</summary>
    public MealTemplateContent? TemplateMeal { get; set; }

    /// <summary>Serialized full-day payload (up to five slots).</summary>
    public IList<MealTemplateContent> TemplateDay { get; set; } = new List<MealTemplateContent>();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Enums.SyncStatus SyncStatus { get; set; } = Enums.SyncStatus.PendingUpload;
}

/// <summary>Content of one meal slot inside a template. Never contains status or photographs.</summary>
public sealed class MealTemplateContent
{
    public Enums.MealCategoryKey MealCategoryKey { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Ingredients { get; set; } = string.Empty;

    public TimeOnly? PlannedTime { get; set; }
}
