-- DietTracker schema v9
-- Refreshes the default pictograms of the permanent meal cards: breakfast now shows a croissant
-- (U+1F950 + emoji presentation) so it no longer shares the plate glyph with lunch, and dinner a
-- pot of food (U+1F372 + emoji presentation) instead of the abstract round glyph (U+E7C8). Lunch
-- (plate) and the two snack cards (cookie) keep their icons. A row is only rewritten while it
-- still carries a value this application wrote itself - the previous default glyph or an empty
-- icon - so an icon changed by hand is never overwritten.

UPDATE MealCategory
SET Icon = char(129360, 65039)
WHERE FixedCategoryKey = 1
  AND Icon IN ('', char(127869, 65039));

UPDATE MealCategory
SET Icon = char(127858, 65039)
WHERE FixedCategoryKey = 5
  AND Icon IN ('', char(59336));