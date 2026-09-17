import { createContext, useCallback, useContext, useEffect, useRef, useState } from "react";
import type { ReactNode } from "react";
import { createPortal } from "react-dom";

type DialogTone = "default" | "danger";

interface ConfirmOptions {
  title: string;
  description: string;
  detail?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  tone?: DialogTone;
}

interface MessageOptions {
  title: string;
  description: string;
  detail?: string;
  buttonLabel?: string;
  tone?: DialogTone;
}

type DialogRequest =
  | { kind: "confirm"; options: ConfirmOptions; resolve: (value: boolean) => void }
  | { kind: "message"; options: MessageOptions; resolve: () => void };

interface DialogAPI {
  confirm: (options: ConfirmOptions) => Promise<boolean>;
  notify: (options: MessageOptions) => Promise<void>;
}

const DialogContext = createContext<DialogAPI | null>(null);

export function useAppDialog(): DialogAPI {
  const value = useContext(DialogContext);
  if (!value) throw new Error("useAppDialog must be used inside AppDialogProvider");
  return value;
}

export function AppDialogProvider({ children }: { children: ReactNode }) {
  const [active, setActive] = useState<DialogRequest | null>(null);
  const queueRef = useRef<DialogRequest[]>([]);

  const presentNext = useCallback(() => {
    setActive((current) => current ?? queueRef.current.shift() ?? null);
  }, []);

  const enqueue = useCallback((request: DialogRequest) => {
    queueRef.current.push(request);
    presentNext();
  }, [presentNext]);

  const confirm = useCallback((options: ConfirmOptions) => new Promise<boolean>((resolve) => {
    enqueue({ kind: "confirm", options, resolve });
  }), [enqueue]);

  const notify = useCallback((options: MessageOptions) => new Promise<void>((resolve) => {
    enqueue({ kind: "message", options, resolve });
  }), [enqueue]);

  const close = useCallback((confirmed = false) => {
    setActive((current) => {
      if (!current) return null;
      if (current.kind === "confirm") current.resolve(confirmed);
      else current.resolve();
      return null;
    });
    window.setTimeout(presentNext, 0);
  }, [presentNext]);

  useEffect(() => {
    if (!active) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        close(false);
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [active, close]);

  return (
    <DialogContext.Provider value={{ confirm, notify }}>
      {children}
      {active && createPortal(
        <div className="fixed inset-0 z-[200] flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm" onMouseDown={(event) => { if (event.currentTarget === event.target) close(false); }}>
          <div className="w-full max-w-md overflow-hidden rounded-2xl border border-border bg-card text-card-foreground shadow-2xl" role="dialog" aria-modal="true" aria-labelledby="lumine-dialog-title">
            <div className="p-5">
              <div className={`mb-4 flex h-10 w-10 items-center justify-center rounded-xl ${active.options.tone === "danger" ? "bg-destructive/12 text-destructive" : "bg-primary/12 text-primary"}`} aria-hidden="true">
                {active.options.tone === "danger" ? "!" : "i"}
              </div>
              <h2 id="lumine-dialog-title" className="text-base font-semibold">{active.options.title}</h2>
              <p className="mt-2 whitespace-pre-line text-sm leading-relaxed text-muted-foreground">{active.options.description}</p>
              {active.options.detail && <pre className="mt-3 max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-xl border border-border bg-muted/30 p-3 text-[11px] leading-relaxed text-muted-foreground">{active.options.detail}</pre>}
            </div>
            <div className="flex justify-end gap-2 border-t border-border bg-muted/15 px-5 py-3">
              {active.kind === "confirm" && (
                <button type="button" className="ui-secondary-button" onClick={() => close(false)} autoFocus>
                  {active.options.cancelLabel ?? "キャンセル"}
                </button>
              )}
              <button
                type="button"
                className={active.options.tone === "danger" && active.kind === "confirm" ? "h-9 rounded-lg bg-destructive px-4 text-xs font-semibold text-destructive-foreground hover:opacity-90" : "ui-primary-button"}
                onClick={() => close(true)}
                autoFocus={active.kind === "message"}
              >
                {active.kind === "confirm" ? (active.options.confirmLabel ?? "実行") : (active.options.buttonLabel ?? "OK")}
              </button>
            </div>
          </div>
        </div>,
        document.body
      )}
    </DialogContext.Provider>
  );
}
