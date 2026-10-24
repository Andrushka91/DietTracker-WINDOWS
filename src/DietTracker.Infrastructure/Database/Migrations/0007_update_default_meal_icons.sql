-- DietTracker schema v7
-- Replaces the placeholder pictograms of the permanent meal cards: breakfast and lunch now show a
-- plate with cutlery (U+1F37D + emoji presentation), the two snack cards a cookie (U+1F36A), because
-- the Segoe Fluent icon font has no food glyphs and the previous code points rendered as unrelated
-- shapes (the lunch and afternoon snack glyphs looked like half a star). A row is only rewritten
-- while it still carries a value this application wrote itself - the previous default glyph or an
-- empty icon - so an icon changed by hand is never overwritten. Dinner keeps its round glyph
-- (U+E7C8) and therefore only gets a value when it has none.

UPDATE MealCategory
SET Icon = char(127869, 65039)
WHERE FixedCategoryKey IN (1, 3)
  AND Icon IN ('', char(59332), char(59334));

UPDATE MealCategory
SET Icon = char(127850, 65039)
WHERE FixedCategoryKey IN (2, 4)
  AND Icon IN ('', char(59333), char(59335));

UPDATE MealCategory
SET Icon = char(59336)
WHERE FixedCategoryKey = 5
  AND Icon = '';