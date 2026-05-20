import { useQuery } from "@tanstack/react-query";
import { listPosts } from "@/shared/api/client";
import { useToast } from "@/components/ui/toast";
import { useEffect } from "react";

export function PostsView() {
  const { data: posts = [], isLoading, isError, error } = useQuery({
    queryKey: ["posts"],
    queryFn: () => listPosts(),
  });
  const { addToast } = useToast();

  useEffect(() => {
    if (isError && error) {
      addToast(`Failed to load posts: ${error.message}`, "error");
    }
  }, [isError, error, addToast]);

  if (isLoading) {
    return <div className="flex items-center justify-center h-full text-muted-foreground">Loading posts...</div>;
  }

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <h1 className="text-2xl font-bold mb-6">Posts</h1>

      {posts.length === 0 ? (
        <p className="text-muted-foreground">No posts yet.</p>
      ) : (
        <div className="space-y-2">
          {posts.map((post) => (
            <div
              key={post.id}
              className="p-4 rounded-md border border-border"
            >
              <div className="flex items-center justify-between">
                <h3 className="font-medium">{post.title || "Untitled"}</h3>
                <span className="text-xs px-2 py-1 rounded bg-secondary">
                  {post.status}
                </span>
              </div>
              {post.body && (
                <p className="text-sm text-muted-foreground mt-1 truncate">
                  {post.body}
                </p>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
