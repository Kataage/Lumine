import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { listTags, createTag } from "@/shared/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PlusIcon } from "lucide-react";
import { useToast } from "@/components/ui/toast";

export function TagsView() {
  const [newTagName, setNewTagName] = useState("");
  const [newTagColor, setNewTagColor] = useState("");
  const queryClient = useQueryClient();
  const { addToast } = useToast();

  const { data: tags = [], isLoading } = useQuery({
    queryKey: ["tags"],
    queryFn: listTags,
  });

  const createMutation = useMutation({
    mutationFn: () => createTag(newTagName, newTagColor || undefined),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
      setNewTagName("");
      setNewTagColor("");
      addToast("Tag created", "info");
    },
    onError: (error) => {
      const msg = error instanceof Error ? error.message : String(error);
      addToast(`Failed to create tag: ${msg}`, "error");
    },
  });

  const handleCreate = () => {
    if (newTagName.trim()) {
      createMutation.mutate();
    }
  };

  if (isLoading) {
    return <div className="flex items-center justify-center h-full text-muted-foreground">Loading tags...</div>;
  }

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <h1 className="text-2xl font-bold mb-6">Tags</h1>

      <div className="flex gap-2 mb-6">
        <Input
          value={newTagName}
          onChange={(e) => setNewTagName(e.target.value)}
          placeholder="Tag name"
          className="flex-1"
        />
        <Input
          value={newTagColor}
          onChange={(e) => setNewTagColor(e.target.value)}
          placeholder="Color (optional)"
          className="w-32"
        />
        <Button onClick={handleCreate} disabled={!newTagName.trim() || createMutation.isPending}>
          <PlusIcon className="w-4 h-4 mr-1" />
          Add
        </Button>
      </div>

      <div className="space-y-2">
        {tags.length === 0 ? (
          <p className="text-muted-foreground">No tags yet. Create one above.</p>
        ) : (
          tags.map((tag) => (
            <div
              key={tag.id}
              className="flex items-center gap-3 p-3 rounded-md border border-border"
            >
              {tag.color && (
                <div
                  className="w-4 h-4 rounded-full"
                  style={{ backgroundColor: tag.color }}
                />
              )}
              <span className="font-medium">{tag.name}</span>
              <span className="text-xs text-muted-foreground ml-auto">
                {new Date(tag.created_at).toLocaleDateString()}
              </span>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
