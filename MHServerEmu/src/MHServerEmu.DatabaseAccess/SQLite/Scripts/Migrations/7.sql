-- Add CustomCostumes column to the Player table (per-avatar custom-costume overrides)
ALTER TABLE Player ADD COLUMN CustomCostumes TEXT NOT NULL DEFAULT '';
