using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using DietTracker.Core.Validation;

namespace DietTracker.Core.Services;

/// <summary>Copying meals and days, and working with reusable templates.</summary>
public sealed partial class MealTrackingService
{
    /// <inheritdoc />
    public async Task<MealEntry> CopyMealToDateAsync(Guid sourceMealEntryId, DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var source = await RequireEntryAsync(sourceMealEntryId, cancellationToken).ConfigureAwait(false);
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var target = await GetOrCreateSlotAsync(targetDate, source.MealCategoryKey, cancellationToken).ConfigureAwait(false);

        // Copying always produces a PENDING meal without completion data and without photographs.
        var copy = MealCopyRules.CopyMealToDate(source, targetDate, profile.Id, _clock.UtcNow);
        copy.Id = target.Id;
        copy.MealCategoryId = target.MealCategoryId;

        await PersistAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy;
    }

    /// <inheritdoc />
    public async Task<int> DuplicateDayAsync(DateOnly sourceDate, DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var sourceMeals = await GetDayAsync(sourceDate, cancellationToken).ConfigureAwait(false);
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var targets = await GetDayAsync(targetDate, cancellationToken).ConfigureAwait(false);

        var copied = 0;
        foreach (var source in sourceMeals)
        {
            if (!MealContentRules.HasUserContent(source))
            {
                continue;
            }

            var target = targets.First(t => t.MealCategoryKey == source.MealCategoryKey);
            var copy = MealCopyRules.CopyMealToDate(source, targetDate, profile.Id, _clock.UtcNow);
            copy.Id = target.Id;
            copy.MealCategoryId = target.MealCategoryId;

            await PersistAsync(copy, cancellationToken).ConfigureAwait(false);
            copied++;
        }

        return copied;
    }

    /// <inheritdoc />
    public async Task<MealTemplate> SaveTemplateAsync(MealEntry entry, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var template = MealCopyRules.CreateTemplate(entry, name, _clock.UtcNow);
        var validation = MealTemplateValidator.Validate(template);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation);
        }

        await _templates.UpsertAsync(template, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.MealTemplate, template.Id, 1, cancellationToken).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task<MealTemplate> SaveDayTemplateAsync(DateOnly date, string name, CancellationToken cancellationToken = default)
    {
        var meals = await GetDayAsync(date, cancellationToken).ConfigureAwait(false);
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        var template = MealCopyRules.CreateDayTemplate(
            meals.Where(MealContentRules.HasUserContent),
            profile.Id,
            name,
            _clock.UtcNow);

        var validation = MealTemplateValidator.Validate(template);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation);
        }

        await _templates.UpsertAsync(template, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.MealTemplate, template.Id, 1, cancellationToken).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task ApplyTemplateAsync(Guid templateId, DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var template = await _templates.GetAsync(templateId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Template {templateId} does not exist locally.");

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var categories = await GetCategoriesAsync(cancellationToken).ConfigureAwait(false);

        var contents = template.IsFullDay
            ? template.TemplateDay.ToArray()
            : template.TemplateMeal is null ? [] : [template.TemplateMeal];

        foreach (var content in contents)
        {
            var category = categories.FirstOrDefault(c => c.FixedCategoryKey == content.MealCategoryKey)
                ?? throw new InvalidOperationException($"Permanent category {content.MealCategoryKey} is missing.");

            var target = await GetOrCreateSlotAsync(targetDate, content.MealCategoryKey, cancellationToken).ConfigureAwait(false);
            var entry = MealCopyRules.ApplyTemplateContent(content, category.Id, targetDate, profile.Id, _clock.UtcNow);
            entry.Id = target.Id;

            await PersistAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        return await _templates.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);
    }
}
