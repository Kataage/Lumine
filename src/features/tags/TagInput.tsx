import { useState, useRef, useEffect, useCallback } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { listTags, createTag, getAssetTags, setAssetTags } from "@/shared/api/client";
import { Input } from "@/components/ui/input";
import { XIcon, PlusIcon, TagIcon } from "lucide-react";
import type { Tag } from "@/entities/types";

interface TagInputProps {
  assetId: number;
}

export function TagInput({ assetId }: TagInputProps) {
  const queryClient = useQueryClient();
  const [inputValue, setInputValue] = useState("");
  const [showDropdown, setShowDropdown] = useState(false);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const { data: allTags = [] } = useQuery<Tag[]>({
    queryKey: ["tags"],
    queryFn: listTags,
  });

  const { data: assetTags = [] } = useQuery<Tag[]>({
    queryKey: ["asset-tags", assetId],
    queryFn: () => getAssetTags(assetId),
    enabled: assetId > 0,
  });

  const createTagMutation = useMutation({
    mutationFn: ({ name, color }: { name: string; color?: string }) =>
      createTag(name, color),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });

  const setTagsMutation = useMutation({
    mutationFn: ({ assetId, tagIds }: { assetId: number; tagIds: number[] }) =>
      setAssetTags(assetId, tagIds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["asset-tags", assetId] });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
    },
  });

  const filteredTags = allTags.filter(
    (tag) =>
      tag.name.toLowerCase().includes(inputValue.toLowerCase()) &&
      !assetTags.some((t) => t.id === tag.id)
  );

  const handleSelectTag = useCallback(
    (tag: Tag) => {
      const newTagIds = [...assetTags.map((t) => t.id), tag.id];
      setTagsMutation.mutate({ assetId, tagIds: newTagIds });
      setInputValue("");
      setShowDropdown(false);
      setSelectedIndex(0);
    },
    [assetTags, assetId, setTagsMutation]
  );

  const handleCreateTag = useCallback(() => {
    if (!inputValue.trim()) return;
    const colors = [
      "#ef4444",
      "#f97316",
      "#eab308",
      "#22c55e",
      "#3b82f6",
      "#8b5cf6",
      "#ec4899",
    ];
    const randomColor = colors[Math.floor(Math.random() * colors.length)];
    createTagMutation.mutate(
      { name: inputValue.trim(), color: randomColor },
      {
        onSuccess: (newTag) => {
          const newTagIds = [...assetTags.map((t) => t.id), newTag.id];
          setTagsMutation.mutate({ assetId, tagIds: newTagIds });
          setInputValue("");
          setShowDropdown(false);
          setSelectedIndex(0);
        },
      }
    );
  }, [inputValue, assetTags, assetId, createTagMutation, setTagsMutation]);

  const handleRemoveTag = useCallback(
    (tagId: number) => {
      const newTagIds = assetTags.filter((t) => t.id !== tagId).map((t) => t.id);
      setTagsMutation.mutate({ assetId, tagIds: newTagIds });
    },
    [assetTags, assetId, setTagsMutation]
  );

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      switch (e.key) {
        case "ArrowDown":
          e.preventDefault();
          setSelectedIndex((i) => Math.min(i + 1, filteredTags.length - 1));
          break;
        case "ArrowUp":
          e.preventDefault();
          setSelectedIndex((i) => Math.max(i - 1, 0));
          break;
        case "Enter":
          e.preventDefault();
          if (filteredTags.length > 0 && selectedIndex >= 0) {
            handleSelectTag(filteredTags[selectedIndex]);
          } else if (inputValue.trim()) {
            handleCreateTag();
          }
          break;
        case "Escape":
          setShowDropdown(false);
          break;
        case "Backspace":
          if (!inputValue && assetTags.length > 0) {
            handleRemoveTag(assetTags[assetTags.length - 1].id);
          }
          break;
      }
    },
    [
      filteredTags,
      selectedIndex,
      inputValue,
      assetTags,
      handleSelectTag,
      handleCreateTag,
      handleRemoveTag,
    ]
  );

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (
        containerRef.current &&
        !containerRef.current.contains(e.target as Node)
      ) {
        setShowDropdown(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  return (
    <div ref={containerRef} className="space-y-2">
      <div className="flex flex-wrap gap-1.5">
        {assetTags.map((tag) => (
          <span
            key={tag.id}
            className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full text-white"
            style={{ backgroundColor: tag.color || "#6b7280" }}
          >
            {tag.name}
            <button
              onClick={() => handleRemoveTag(tag.id)}
              className="hover:bg-black/20 rounded-full p-0.5"
            >
              <XIcon className="w-3 h-3" />
            </button>
          </span>
        ))}
        <div className="relative flex-1 min-w-[120px]">
          <div className="flex items-center gap-1">
            <TagIcon className="w-3.5 h-3.5 text-muted-foreground flex-shrink-0" />
            <Input
              ref={inputRef}
              value={inputValue}
              onChange={(e) => {
                setInputValue(e.target.value);
                setShowDropdown(true);
                setSelectedIndex(0);
              }}
              onFocus={() => setShowDropdown(true)}
              onKeyDown={handleKeyDown}
              placeholder="Add tag..."
              className="h-6 text-xs border-0 bg-transparent focus-visible:ring-0 focus-visible:ring-offset-0 px-1"
            />
          </div>
          {showDropdown && inputValue && (
            <div className="absolute top-full left-0 right-0 mt-1 bg-popover border border-border rounded-md shadow-lg z-50 max-h-48 overflow-auto">
              {filteredTags.length > 0 ? (
                filteredTags.map((tag, index) => (
                  <button
                    key={tag.id}
                    onClick={() => handleSelectTag(tag)}
                    className={`w-full px-3 py-1.5 text-left text-sm hover:bg-accent flex items-center gap-2 ${
                      index === selectedIndex ? "bg-accent" : ""
                    }`}
                  >
                    <span
                      className="w-3 h-3 rounded-full flex-shrink-0"
                      style={{ backgroundColor: tag.color || "#6b7280" }}
                    />
                    {tag.name}
                  </button>
                ))
              ) : (
                <button
                  onClick={handleCreateTag}
                  className="w-full px-3 py-1.5 text-left text-sm hover:bg-accent flex items-center gap-2 text-muted-foreground"
                >
                  <PlusIcon className="w-3.5 h-3.5" />
                  Create "{inputValue}"
                </button>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
