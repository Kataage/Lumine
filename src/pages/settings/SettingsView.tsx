import { useAppStore } from "@/shared/hooks/useAppStore";

export function SettingsView() {
  const thumbnailSize = useAppStore((s) => s.thumbnailSize);
  const setThumbnailSize = useAppStore((s) => s.setThumbnailSize);

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <h1 className="text-2xl font-bold mb-6">Settings</h1>

      <div className="space-y-6">
        <div>
          <h2 className="text-lg font-medium mb-2">Display</h2>
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">Default thumbnail size</label>
              <div className="flex items-center gap-2 mt-1">
                <input
                  type="range"
                  min="80"
                  max="300"
                  step="20"
                  value={thumbnailSize}
                  onChange={(e) => setThumbnailSize(Number(e.target.value))}
                  className="flex-1"
                />
                <span className="text-sm text-muted-foreground w-12">{thumbnailSize}px</span>
              </div>
            </div>
          </div>
        </div>

        <div>
          <h2 className="text-lg font-medium mb-2">Scan</h2>
          <p className="text-sm text-muted-foreground">
            Supported image formats: JPG, JPEG, PNG, GIF, BMP, WebP, TIFF, TIF, ICO, SVG, AVIF, APNG
          </p>
        </div>

        <div>
          <h2 className="text-lg font-medium mb-2">Storage</h2>
          <p className="text-sm text-muted-foreground">
            Database and thumbnails are stored in the app data directory.
          </p>
        </div>
      </div>
    </div>
  );
}
