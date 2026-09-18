import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";

export interface ToolbarSelectOption<T extends string | number> {
  value: T;
  label: string;
}

interface ToolbarSelectProps<T extends string | number> {
  value: T;
  options: readonly ToolbarSelectOption<T>[];
  onChange: (value: T) => void;
  ariaLabel: string;
  className?: string;
}

interface MenuPosition {
  top: number;
  left: number;
  width: number;
  maxHeight: number;
}

const MENU_GAP = 5;
const VIEWPORT_MARGIN = 10;
const MIN_MENU_HEIGHT = 96;

export function ToolbarSelect<T extends string | number>({
  value,
  options,
  onChange,
  ariaLabel,
  className = "",
}: ToolbarSelectProps<T>) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(() => Math.max(0, options.findIndex((option) => option.value === value)));
  const [position, setPosition] = useState<MenuPosition | null>(null);

  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value));
  const selected = options[selectedIndex] ?? options[0];

  const updatePosition = () => {
    const trigger = triggerRef.current;
    if (!trigger) return;
    const rect = trigger.getBoundingClientRect();
    const width = Math.max(rect.width, 132);
    const maxLeft = Math.max(VIEWPORT_MARGIN, window.innerWidth - width - VIEWPORT_MARGIN);
    const left = Math.min(Math.max(rect.left, VIEWPORT_MARGIN), maxLeft);
    const top = rect.bottom + MENU_GAP;
    const availableBelow = window.innerHeight - top - VIEWPORT_MARGIN;

    setPosition({
      top,
      left,
      width,
      maxHeight: Math.max(MIN_MENU_HEIGHT, availableBelow),
    });
  };

  useLayoutEffect(() => {
    if (!open) return;
    setActiveIndex(selectedIndex);
    updatePosition();
  }, [open, selectedIndex]);

  useEffect(() => {
    if (!open) return;

    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target as Node | null;
      if (!target) return;
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) return;
      setOpen(false);
    };
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      setOpen(false);
      triggerRef.current?.focus();
    };
    const handleViewportChange = () => updatePosition();

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleEscape);
    window.addEventListener("resize", handleViewportChange);
    window.addEventListener("scroll", handleViewportChange, true);

    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleEscape);
      window.removeEventListener("resize", handleViewportChange);
      window.removeEventListener("scroll", handleViewportChange, true);
    };
  }, [open]);

  const selectIndex = (index: number) => {
    const option = options[index];
    if (!option) return;
    onChange(option.value);
    setOpen(false);
    triggerRef.current?.focus();
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (!open) {
        setOpen(true);
        setActiveIndex(selectedIndex);
        return;
      }
      const delta = event.key === "ArrowDown" ? 1 : -1;
      setActiveIndex((current) => (current + delta + options.length) % options.length);
      return;
    }

    if ((event.key === "Enter" || event.key === " ") && open) {
      event.preventDefault();
      selectIndex(activeIndex);
      return;
    }

    if (event.key === "Home" && open) {
      event.preventDefault();
      setActiveIndex(0);
      return;
    }

    if (event.key === "End" && open) {
      event.preventDefault();
      setActiveIndex(Math.max(0, options.length - 1));
    }
  };

  const menu = open && position ? createPortal(
    <div
      ref={menuRef}
      className="toolbar-select-menu"
      role="listbox"
      aria-label={ariaLabel}
      style={{
        top: position.top,
        left: position.left,
        width: position.width,
        maxHeight: position.maxHeight,
      }}
    >
      {options.map((option, index) => {
        const selectedOption = option.value === value;
        const active = index === activeIndex;
        return (
          <button
            key={String(option.value)}
            type="button"
            role="option"
            aria-selected={selectedOption}
            className={`toolbar-select-option ${active ? "active" : ""} ${selectedOption ? "selected" : ""}`}
            onPointerMove={() => setActiveIndex(index)}
            onClick={() => selectIndex(index)}
          >
            <span className="min-w-0 flex-1 truncate">{option.label}</span>
            {selectedOption && <span className="toolbar-select-check" aria-hidden="true">✓</span>}
          </button>
        );
      })}
    </div>,
    document.body
  ) : null;

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={`toolbar-select-trigger ${open ? "open" : ""} ${className}`}
        aria-label={ariaLabel}
        aria-haspopup="listbox"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
        onKeyDown={handleKeyDown}
      >
        <span className="min-w-0 flex-1 truncate text-left">{selected?.label ?? ""}</span>
        <svg className="toolbar-select-chevron" viewBox="0 0 20 20" fill="none" aria-hidden="true">
          <path d="m6.5 8 3.5 3.5L13.5 8" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>
      {menu}
    </>
  );
}
