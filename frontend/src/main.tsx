import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./index.css";
import "./detailPanel.css";
import { AppDialogProvider } from "./components/AppDialogProvider";
import { initializeMemoryImageCacheBudget } from "./utils/imageCacheSettings";

function start() {
  // The default memory-cache budget is already safe. Do not delay the first
  // React frame on a settings/SQLite round-trip; apply the persisted override
  // asynchronously after the viewer has mounted.
  void initializeMemoryImageCacheBudget();
  ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
    <React.StrictMode>
      <AppDialogProvider>
        <App />
      </AppDialogProvider>
    </React.StrictMode>
  );
}

start();