import { useState, useCallback, createContext, useContext, type ReactNode } from "react";
import { XIcon, AlertCircleIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

interface Toast {
  id: number;
  message: string;
  type: "error" | "warning" | "info";
}

interface ToastContextType {
  addToast: (message: string, type?: "error" | "warning" | "info") => void;
}

const ToastContext = createContext<ToastContextType | undefined>(undefined);

let nextId = 0;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const addToast = useCallback(
    (message: string, type: "error" | "warning" | "info" = "error") => {
      const id = nextId++;
      setToasts((prev) => [...prev, { id, message, type }]);
      setTimeout(() => {
        setToasts((prev) => prev.filter((t) => t.id !== id));
      }, 5000);
    },
    []
  );

  const removeToast = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  return (
    <ToastContext.Provider value={{ addToast }}>
      {children}
      <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`flex items-start gap-3 p-4 rounded-lg shadow-lg border ${
              toast.type === "error"
                ? "bg-destructive/10 border-destructive/30 text-destructive"
                : toast.type === "warning"
                ? "bg-yellow-50 border-yellow-300 text-yellow-800"
                : "bg-blue-50 border-blue-300 text-blue-800"
            }`}
          >
            <AlertCircleIcon className="w-5 h-5 mt-0.5 shrink-0" />
            <p className="text-sm flex-1">{toast.message}</p>
            <Button
              variant="ghost"
              size="icon"
              className="w-6 h-6 shrink-0"
              onClick={() => removeToast(toast.id)}
            >
              <XIcon className="w-4 h-4" />
            </Button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error("useToast must be used within a ToastProvider");
  }
  return context;
}
