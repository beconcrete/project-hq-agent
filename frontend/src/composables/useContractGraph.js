import { ref } from "vue";
import { useAuth } from "./useAuth";

export function useContractGraph() {
  const graph = ref({ nodes: [], edges: [] });
  const loading = ref(false);
  const error = ref(null);
  const auth = useAuth();

  async function fetch() {
    loading.value = true;
    error.value = null;
    try {
      const res = await globalThis.fetch("/api/contract-graph", {
        headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
      });
      if (!res.ok) throw new Error(`Request failed (${res.status})`);
      graph.value = await res.json();
    } catch (e) {
      error.value = e.message || "Failed to load graph";
    } finally {
      loading.value = false;
    }
  }

  return { graph, loading, error, fetch };
}
