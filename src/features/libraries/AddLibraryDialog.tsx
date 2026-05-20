import { useState, type FormEvent, type ChangeEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { addLibrary } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { FolderIcon } from "lucide-react";
import { useToast } from "@/components/ui/toast";
import { open as openDialog } from "@tauri-apps/plugin-dialog";

interface AddLibraryDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function AddLibraryDialog({ open, onOpenChange }: AddLibraryDialogProps) {
  const [name, setName] = useState("");
  const [path, setPath] = useState("");
  const addLibraryToStore = useAppStore((s) => s.addLibrary);
  const queryClient = useQueryClient();
  const { addToast } = useToast();

  const mutation = useMutation({
    mutationFn: () => addLibrary(name, path),
    onSuccess: (library) => {
      addLibraryToStore(library);
      queryClient.invalidateQueries({ queryKey: ["libraries"] });
      setName("");
      setPath("");
      onOpenChange(false);
    },
    onError: (error) => {
      addToast(`Failed to add library: ${error.message}`, "error");
    },
  });

  const handleSelectFolder = async () => {
    const selected = await openDialog({
      directory: true,
      multiple: false,
      title: "Select Library Folder",
    });
    if (selected && typeof selected === "string") {
      setPath(selected);
    }
  };

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (name && path) {
      mutation.mutate();
    }
  };

  const handleNameChange = (e: ChangeEvent<HTMLInputElement>) => {
    setName(e.target.value);
  };

  const handlePathChange = (e: ChangeEvent<HTMLInputElement>) => {
    setPath(e.target.value);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Library</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="text-sm font-medium">Name</label>
            <Input
              value={name}
              onChange={handleNameChange}
              placeholder="My Image Library"
              className="mt-1"
            />
          </div>
          <div>
            <label className="text-sm font-medium">Path</label>
            <div className="flex gap-2 mt-1">
              <Input
                value={path}
                onChange={handlePathChange}
                placeholder="C:\Images"
                className="flex-1"
              />
              <Button type="button" variant="outline" onClick={handleSelectFolder}>
                <FolderIcon className="w-4 h-4" />
              </Button>
            </div>
          </div>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={!name || !path || mutation.isPending}>
              {mutation.isPending ? "Adding..." : "Add Library"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}