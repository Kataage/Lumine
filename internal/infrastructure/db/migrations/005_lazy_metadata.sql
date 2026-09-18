-- Heavy EXIF parsing is intentionally deferred until an asset is opened in
-- the detail panel. This flag distinguishes "not parsed yet" from "parsed and
-- no EXIF was present" so non-EXIF images are not reparsed on every click.
ALTER TABLE assets ADD COLUMN metadata_loaded INTEGER NOT NULL DEFAULT 0;
