import { QueryProvider } from "./app/providers";
import { MainLayout } from "./app/layout/MainLayout";

function App() {
  return (
    <QueryProvider>
      <MainLayout />
    </QueryProvider>
  );
}

export default App;