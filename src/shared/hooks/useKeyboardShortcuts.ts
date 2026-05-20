import { useEffect } from "react";
import { useAppStore } from "./useAppStore";

export function useKeyboardShortcuts() {
  const selectedAsset = useAppStore((s) => s.selectedAsset);
  const setSelectedAsset = useAppStore((s) => s.setSelectedAsset);
  const setDetailPanelOpen = useAppStore((s) => s.setDetailPanelOpen);
  const setSearchQuery = useAppStore((s) => s.setSearchQuery);
  const setActiveView = useAppStore((s) => s.setActiveView);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const isInputFocused =
        e.target instanceof HTMLInputElement ||
        e.target instanceof HTMLTextAreaElement ||
        e.target instanceof HTMLSelectElement;

      if (e.ctrlKey || e.metaKey) {
        if (e.key === "f" || e.key === "F") {
          e.preventDefault();
          const searchInput = document.querySelector(
            'input[placeholder*="Search"], input[placeholder*="search"]'
          ) as HTMLInputElement;
          if (searchInput) {
            searchInput.focus();
          }
          return;
        }
        return;
      }

      if (isInputFocused) return;

      switch (e.key) {
        case "ArrowLeft":
          e.preventDefault();
          break;
        case "ArrowRight":
          e.preventDefault();
          break;
        case "Delete":
          e.preventDefault();
          break;
        case "m":
        case "M":
          e.preventDefault();
          if (selectedAsset) {
            setDetailPanelOpen(true);
            setTimeout(() => {
              const textarea = document.querySelector(
                'textarea[placeholder*="note"], textarea[placeholder*="Note"]'
              ) as HTMLTextAreaElement;
              if (textarea) textarea.focus();
            }, 100);
          }
          break;
        case "t":
        case "T":
          e.preventDefault();
          if (selectedAsset) {
            setDetailPanelOpen(true);
            setTimeout(() => {
              const tagInput = document.querySelector(
                'input[placeholder*="tag"], input[placeholder*="Tag"]'
              ) as HTMLInputElement;
              if (tagInput) tagInput.focus();
            }, 100);
          }
          break;
        case "p":
        case "P":
          e.preventDefault();
          setActiveView("posts");
          break;
        case "Escape":
          e.preventDefault();
          if (selectedAsset) {
            setSelectedAsset(null);
            setDetailPanelOpen(false);
          }
          break;
        case "g":
        case "G":
          e.preventDefault();
          setActiveView("assets");
          break;
        case "s":
        case "S":
          e.preventDefault();
          setActiveView("settings");
          break;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selectedAsset, setSelectedAsset, setDetailPanelOpen, setSearchQuery, setActiveView]);
}
