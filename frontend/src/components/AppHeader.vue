<template>
  <header class="topbar">
    <button
      class="topbar-menu-btn"
      aria-label="Open menu"
      @click="sidebar.toggleMobile()"
    >
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <line x1="3" y1="6" x2="21" y2="6" />
        <line x1="3" y1="12" x2="21" y2="12" />
        <line x1="3" y1="18" x2="21" y2="18" />
      </svg>
    </button>

    <div class="topbar-breadcrumb">
      <span class="breadcrumb-root">HQ Agent</span>
      <span class="breadcrumb-sep">›</span>
      <span class="breadcrumb-current">{{ breadcrumb }}</span>
    </div>

    <div class="topbar-user">
      <!-- Search index status — admin only -->
      <div v-if="isAdmin" class="index-status" ref="statusRef">
        <button
          class="index-btn"
          :class="{ 'index-btn--pending': pendingCount > 0 }"
          :title="pendingCount > 0 ? `${pendingCount} entities pending re-index` : 'Search index'"
          @click="togglePanel"
        >
          <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="6.5" cy="6.5" r="4" />
            <path d="M11 11l2.5 2.5" />
            <path d="M6.5 4.5v4M4.5 6.5h4" />
          </svg>
          <span v-if="pendingCount > 0" class="index-dot" />
        </button>

        <div v-if="panelOpen" class="index-panel">
          <p class="index-panel-label">Search Index</p>
          <p class="index-panel-status" :class="pendingCount > 0 ? 'status--warn' : 'status--ok'">
            {{ pendingCount > 0 ? `${pendingCount} entities pending` : 'Up to date' }}
          </p>
          <button
            class="index-panel-btn"
            :disabled="reindexing || reindexQueued"
            @click="triggerReindex"
          >
            <svg v-if="reindexing" class="spin" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M8 2a6 6 0 0 1 6 6" stroke-linecap="round" />
            </svg>
            {{ reindexing ? 'Queuing…' : reindexQueued ? 'Queued ✓' : 'Re-index now' }}
          </button>
        </div>
      </div>

      <span class="topbar-user-name">{{ auth.state.user?.displayName }}</span>
      <button class="btn btn-ghost btn-sm" @click="auth.logout()">Sign out</button>
    </div>
  </header>
</template>

<script setup>
import { computed, ref, onMounted, onBeforeUnmount } from "vue";
import { useRoute } from "vue-router";
import { useAuth } from "../composables/useAuth";
import { useSidebar } from "../composables/useSidebar";

const auth    = useAuth();
const sidebar = useSidebar();
const route   = useRoute();

const breadcrumb = computed(() => {
  const map = {
    "/": "Home",
    "/contracts": "Contracts",
    "/auth-test": "Auth Test",
  };
  return map[route.path] ?? "HQ Agent";
});

const isAdmin = computed(() => auth.hasRole("admin"));

// ── Index status ────────────────────────────────────────────
const statusRef    = ref(null);
const panelOpen    = ref(false);
const pendingCount = ref(0);
const reindexing   = ref(false);
const reindexQueued = ref(false);

function togglePanel() {
  panelOpen.value = !panelOpen.value;
}

function onClickOutside(e) {
  if (statusRef.value && !statusRef.value.contains(e.target)) {
    panelOpen.value = false;
  }
}

async function fetchStatus() {
  if (!isAdmin.value) return;
  try {
    const token = auth.getToken();
    const headers = {};
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;
    const res = await fetch("/api/management-embedding-status", { headers });
    if (res.ok) {
      const data = await res.json();
      pendingCount.value = data.pendingCount ?? 0;
    }
  } catch {
    // silent — status indicator is non-critical
  }
}

async function triggerReindex() {
  if (reindexing.value || reindexQueued.value) return;
  reindexing.value = true;
  try {
    const token = auth.getToken();
    const headers = { "Content-Type": "application/json" };
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;
    const res = await fetch("/api/management-reindex", { method: "POST", headers });
    if (res.ok) {
      reindexQueued.value = true;
      setTimeout(() => { reindexQueued.value = false; }, 4000);
    }
  } catch {
    // silent
  } finally {
    reindexing.value = false;
  }
}

onMounted(() => {
  fetchStatus();
  document.addEventListener("click", onClickOutside);
});

onBeforeUnmount(() => {
  document.removeEventListener("click", onClickOutside);
});
</script>

<style scoped>
/* ── Index status widget ──────────────────────────────────── */
.index-status {
  position: relative;
}

.index-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
  width: 28px;
  height: 28px;
  border: none;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--color-text-muted);
  cursor: pointer;
  transition: color var(--transition-fast), background var(--transition-fast);
}

.index-btn:hover {
  color: var(--color-text-secondary);
  background: var(--color-border-subtle);
}

.index-btn svg {
  width: 15px;
  height: 15px;
}

.index-btn--pending {
  color: #d97706;
}

.index-btn--pending:hover {
  color: #b45309;
  background: #fef3c7;
}

.index-dot {
  position: absolute;
  top: 4px;
  right: 4px;
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #d97706;
  animation: pulse 2s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50%       { opacity: 0.4; }
}

/* ── Dropdown panel ──────────────────────────────────────── */
.index-panel {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  width: 188px;
  padding: 12px 14px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-md);
  z-index: 100;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.index-panel-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--color-text-muted);
}

.index-panel-status {
  font-size: 13px;
}

.status--ok   { color: #059669; }
.status--warn { color: #d97706; }

.index-panel-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin-top: 4px;
  padding: 5px 10px;
  font-size: 12px;
  font-weight: 500;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  color: var(--color-text-primary);
  cursor: pointer;
  transition: background var(--transition-fast), border-color var(--transition-fast);
}

.index-panel-btn:hover:not(:disabled) {
  background: var(--color-bg);
  border-color: var(--color-text-muted);
}

.index-panel-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.index-panel-btn svg {
  width: 12px;
  height: 12px;
}

.spin {
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
