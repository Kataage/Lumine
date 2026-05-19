import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { moveAssets } from "@/shared/api/client";
import { useAppStore } from "@/shared/hooks/useAppStore";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from "@/components/ui/dialog";
import { FolderIcon, AlertCircleIcon, CheckCircleIcon } from "lucide-react";

interface MoveAssetsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function MoveAssetsDialog({ open, onOpenChange }: MoveAssetsDialogProps) {
  const [destination, setDestination] = useState("");
  const [policy, setPolicy] = useState<"skip" | "rename" | "fail">("skip");
  const [result, setResult] = useState<{
    succeeded: number;
    skipped: number;
    errors: number;
    errorMessages: string[];
  } | null>(null);

  const selectedAssetIds = useAppStore((s) => s.selectedAssetIds);
  const setSelectedAssetIds = useAppStore((s) => s.setSelectedAssetIds);
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () =>
      moveAssets(selectedAssetIds, destination, policy),
    onSuccess: (res) => {
      setResult({
        succeeded: res.succeeded,
        skipped: res.skipped,
        errors: res.errors,
        errorMessages: res.error_messages,
      });
      queryClient.invalidateQueries({ queryKey: ["assets"] });
      setSelectedAssetIds([]);
    },
  });

  const handleSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (destination && selectedAssetIds.length > 0) {
      mutation.mutate();
    }
  };

  const handleClose = () => {
    setDestination("");
    setPolicy("skip");
    setResult(null);
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Move Assets</DialogTitle>
          <DialogDescription>
            Move {selectedAssetIds.length} asset(s) to the destination folder.
          </DialogDescription>
        </DialogHeader>

        {result ? (
          <div className="space-y-4">
            <div className="flex items-center gap-2 text-sm">
              <CheckCircleIcon className="w-4 h-4 text-green-600" />
              <span>{result.succeeded} moved</span>
              {result.skipped > 0 && (
                <span className="text-muted-foreground">{result.skipped} skipped</span>
              )}
              {result.errors > 0 && (
                <span className="text-destructive">{result.errors} errors</span>
              )}
            </div>
            {result.errorMessages.length > 0 && (
              <div className="space-y-1">
                {result.errorMessages.slice(0, 5).map((msg, i) => (
                  <div key={i} className="flex items-start gap-2 text-sm text-destructive">
                    <AlertCircleIcon className="w-4 h-4 mt-0.5" />
                    <span className="truncate">{msg}</span>
                  </div>
                ))}
                {result.errorMessages.length > 5 && (
                  <span className="text-sm text-muted-foreground">
                    +{result.errorMessages.length - 5} more errors
                  </span>
                )}
              </div>
            )}
            <DialogFooter>
              <Button onClick={handleClose}>Close</Button>
            </DialogFooter>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label className="text-sm font-medium">Destination</label>
              <div className="flex gap-2 mt-1">
                <Input
                  value={destination}
                  onChange={(e) => setDestination(e.target.value)}
                  placeholder="C:\Images\NewFolder"
                  className="flex-1"
                />
                <Button type="button" variant="outline">
                  <FolderIcon className="w-4 h-4" />
                </Button>
              </div>
            </div>

            <div>
              <label className="text-sm font-medium">Conflict Policy</label>
              <div className="flex gap-2 mt-1">
                <Button
                  type="button"
                  variant={policy === "skip" ? "default" : "outline"}
                  size="sm"
                  onClick={() => setPolicy("skip")}
                >
                  Skip
                </Button>
                <Button
                  type="button"
                  variant={policy === "rename" ? "default" : "outline"}
                  size="sm"
                  onClick={() => setPolicy("rename")}
                >
                  Rename
                </Button>
                <Button
                  type="button"
                  variant={policy === "fail" ? "default" : "outline"}
                  size="sm"
                  onClick={() => setPolicy("fail")}
                >
                  Fail
                </Button>
              </div>
            </div>

            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={handleClose}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={!destination || mutation.isPending}
              >
                {mutation.isPending ? "Moving..." : "Move"}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
