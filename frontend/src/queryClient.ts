import { QueryClient } from "@tanstack/react-query";

export const QUERY_CACHE_GC_MS = 15 * 60 * 1000;

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: Infinity,
      gcTime: QUERY_CACHE_GC_MS,
      refetchOnWindowFocus: false,
      refetchOnMount: false,
      retry: 1,
    },
  },
});
