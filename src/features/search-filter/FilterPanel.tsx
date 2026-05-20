import { useState } from "react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
  DropdownMenuSeparator,
  DropdownMenuCheckboxItem,
} from "@/components/ui/dropdown-menu";
import { SlidersHorizontalIcon, ArrowUpDown, Check } from "lucide-react";

export interface FilterState {
  sortField: "modified_at" | "created_at" | "name" | "size" | "rating" | "status";
  sortOrder: "asc" | "desc";
  extension?: string;
  ratingMin?: number;
  statusLabel?: string;
  hasNote?: boolean;
  isFavorite?: boolean;
}

interface FilterPanelProps {
  filters: FilterState;
  onFilterChange: (filters: FilterState) => void;
  extensions: string[];
}

export function FilterPanel({ filters, onFilterChange, extensions }: FilterPanelProps) {
  const [isOpen, setIsOpen] = useState(false);

  const sortFields: { value: FilterState["sortField"]; label: string }[] = [
    { value: "modified_at", label: "Modified Date" },
    { value: "created_at", label: "Created Date" },
    { value: "name", label: "Name" },
    { value: "size", label: "Size" },
    { value: "rating", label: "Rating" },
    { value: "status", label: "Status" },
  ];

  return (
    <DropdownMenu open={isOpen} onOpenChange={setIsOpen}>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Filters">
          <SlidersHorizontalIcon className="w-4 h-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        <div className="px-2 py-1.5 text-sm font-medium">Sort By</div>
        {sortFields.map((field) => (
          <DropdownMenuItem
            key={field.value}
            onClick={() =>
              onFilterChange({ ...filters, sortField: field.value })
            }
            className="flex items-center justify-between"
          >
            {field.label}
            {filters.sortField === field.value && (
              <Check className="w-4 h-4 ml-2" />
            )}
          </DropdownMenuItem>
        ))}
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={() =>
            onFilterChange({
              ...filters,
              sortOrder: filters.sortOrder === "asc" ? "desc" : "asc",
            })
          }
          className="flex items-center justify-between"
        >
          <div className="flex items-center gap-2">
            <ArrowUpDown className="w-4 h-4" />
            {filters.sortOrder === "asc" ? "Ascending" : "Descending"}
          </div>
        </DropdownMenuItem>
        {extensions.length > 0 && (
          <>
            <DropdownMenuSeparator />
            <div className="px-2 py-1.5 text-sm font-medium">Extension</div>
            <DropdownMenuItem
              onClick={() =>
                onFilterChange({ ...filters, extension: undefined })
              }
              className="flex items-center justify-between"
            >
              All
              {!filters.extension && <Check className="w-4 h-4 ml-2" />}
            </DropdownMenuItem>
            {extensions.map((ext) => (
              <DropdownMenuItem
                key={ext}
                onClick={() =>
                  onFilterChange({
                    ...filters,
                    extension: filters.extension === ext ? undefined : ext,
                  })
                }
                className="flex items-center justify-between"
              >
                {ext.toUpperCase()}
                {filters.extension === ext && <Check className="w-4 h-4 ml-2" />}
              </DropdownMenuItem>
            ))}
          </>
        )}
        <DropdownMenuSeparator />
        <div className="px-2 py-1.5 text-sm font-medium">Quick Filters</div>
        <DropdownMenuCheckboxItem
          checked={filters.isFavorite === true}
          onCheckedChange={(checked: boolean) =>
            onFilterChange({
              ...filters,
              isFavorite: checked ? true : undefined,
            })
          }
        >
          Favorites only
        </DropdownMenuCheckboxItem>
        <DropdownMenuCheckboxItem
          checked={filters.hasNote === true}
          onCheckedChange={(checked: boolean) =>
            onFilterChange({
              ...filters,
              hasNote: checked ? true : undefined,
            })
          }
        >
          Has notes
        </DropdownMenuCheckboxItem>
        {[1, 2, 3, 4, 5].map((rating) => (
          <DropdownMenuCheckboxItem
            key={rating}
            checked={filters.ratingMin === rating}
            onCheckedChange={(checked: boolean) =>
              onFilterChange({
                ...filters,
                ratingMin: checked ? rating : undefined,
              })
            }
          >
            {"★".repeat(rating)} & up
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
