-- DietTracker schema v4
-- Removes the meal notes completely: the free-form food description covers the use case. The column
-- is dropped and any notes stored inside template JSON payloads are scrubbed, so no note text is
-- left anywhere in the local database. The first migration is left untouched (it must stay applicable
-- to brand new databases, which then run this one right afterwards).

ALTER TABLE MealEntry DROP COLUMN Notes;

-- Template payloads keep their shape ("single" / "day"); only the obsolete notes key is removed.
UPDATE MealTemplate
SET TemplateData = json_object(
    'single', CASE
        WHEN json_type(TemplateData, '$.single') = 'object'
        THEN json_remove(json_extract(TemplateData, '$.single'), '$.notes')
        ELSE json('null')
    END,
    'day', CASE
        WHEN json_type(TemplateData, '$.day') = 'array'
        THEN (SELECT json_group_array(json_remove(value, '$.notes')) FROM json_each(TemplateData, '$.day'))
        ELSE json('[]')
    END
)
WHERE TemplateData IS NOT NULL AND json_valid(TemplateData) AND json_type(TemplateData) = 'object';