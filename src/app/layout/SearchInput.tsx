import { useState, useCallback, type ChangeEvent } from "react";
import { SearchIcon } from "lucide-react";
import { Input } from "@/components/ui/input";

export function SearchInput() {
  const [value, setValue] = useState("");

  const handleSearch = useCallback(
    (e: ChangeEvent<HTMLInputElement>) => {
      setValue(e.target.value);
    },
    []
  );

  return (
    <div className="relative w-64">
      <SearchIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
      <Input
        type="search"
        placeholder="Search assets..."
        value={value}
        onChange={handleSearch}
        className="pl-9"
      />
    </div>
  );
}