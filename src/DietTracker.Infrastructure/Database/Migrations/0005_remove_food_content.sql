-- DietTracker schema v5
-- Removes all food content except the title: the free-form description and the structured ingredient
-- lines are gone from the model. The columns are dropped and the obsolete keys are scrubbed from the
-- template JSON payloads, so no description or ingredient text is left anywhere in the local database.

ALTER TABLE MealEntry DROP COLUMN Ingredients;
ALTER TABLE MealEntry DROP COLUMN Quantities;

-- Template payloads keep their shape ("single" / "day"); only the obsolete keys are removed.
UPDATE MealTemplate
SET TemplateData = json_object(
    'single', CASE
        WHEN json_type(TemplateData, '$.single') = 'object'
        THEN json_remove(json_extract(TemplateData, '$.single'), '$.ingredients', '$.quantities')
        ELSE json('null')
    END,
    'day', CASE
        WHEN json_type(TemplateData, '$.day') = 'array'
        THEN (SELECT json_group_array(json_remove(value, '$.ingredients', '$.quantities')) FROM json_each(TemplateData, '$.day'))
        ELSE json('[]')
    END
)
WHERE TemplateData IS NOT NULL AND json_valid(TemplateData) AND json_type(TemplateData) = 'object';