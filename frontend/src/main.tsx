import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./index.css";
import "./detailPanel.css";
import { AppDialogProvider } from "./components/AppDialogProvider";
import { initializeMemoryImageCacheBudget } from "./utils/imageCacheSettings";

async function start() {
  await initializeMemoryImageCacheBudget();
  ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
    <React.StrictMode>
      <AppDialogProvider>
        <App />
      </AppDialogProvider>
    </React.StrictMode>
  );
}

void start();