import { useCallback, type ChangeEvent } from "react";
import { SearchIcon } from "lucide-react";
import { Input } from "@/components/ui/input";
import { useAppStore } from "@/shared/hooks/useAppStore";

export function SearchInput() {
  const searchQuery = useAppStore((s) => s.searchQuery);
  const setSearchQuery = useAppStore((s) => s.setSearchQuery);

  const handleSearch = useCallback(
    (e: ChangeEvent<HTMLInputElement>) => {
      setSearchQuery(e.target.value);
    },
    [setSearchQuery]
  );

  return (
    <div className="relative w-64">
      <SearchIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
      <Input
        type="search"
        placeholder="Search assets..."
        value={searchQuery}
        onChange={handleSearch}
        className="pl-9"
      />
    </div>
  );
}