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
      <!-- Search index widget — admin only -->
      <div v-if="isAdmin" class="index-status" ref="statusRef">
        <button
          class="index-btn"
          :class="iconClass"
          :title="iconTitle"
          @click="togglePanel"
        >
          <!-- Spinner when indexing in progress -->
          <svg v-if="state === 'indexing'" class="spin" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5">
            <path d="M8 2a6 6 0 1 1-4.2 1.8" stroke-linecap="round" />
          </svg>
          <!-- Search icon otherwise -->
          <svg v-else viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round">
            <circle cx="6.5" cy="6.5" r="4" />
            <path d="M11 11l2.5 2.5" />
          </svg>
          <!-- Amber dot when not indexed -->
          <span v-if="state === 'empty'" class="index-dot" />
        </button>

        <div v-if="panelOpen" class="index-panel">
          <!-- Not indexed -->
          <template v-if="state === 'empty'">
            <p class="index-panel-label">Search Index</p>
            <p class="index-panel-status status--warn">Not indexed</p>
            <p class="index-panel-hint">Run indexing to enable semantic search across all entities.</p>
            <button class="index-panel-btn index-panel-btn--primary" :disabled="triggering" @click="triggerReindex">
              {{ triggering ? 'Starting…' : 'Index now' }}
            </button>
          </template>

          <!-- Indexing in progress -->
          <template v-else-if="state === 'indexing'">
            <p class="index-panel-label">Search Index</p>
            <p class="index-panel-status status--info">
              <svg class="spin inline-spin" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M6 1.5a4.5 4.5 0 1 1-3.2 1.3" stroke-linecap="round" />
              </svg>
              Indexing in progress
            </p>
            <p class="index-panel-hint">{{ pendingCount }} entities queued — completes within the hour.</p>
          </template>

          <!-- Up to date -->
          <template v-else>
            <p class="index-panel-label">Search Index</p>
            <p class="index-panel-status status--ok">Up to date</p>
            <p class="index-panel-hint">{{ okCount }} entities indexed.</p>
            <button class="index-panel-btn" :disabled="triggering" @click="triggerReindex">
              {{ triggering ? 'Starting…' : 'Re-index' }}
            </button>
          </template>
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
const okCount      = ref(0);
const pendingCount = ref(0);
const triggering   = ref(false);

// Derived state: "empty" | "indexing" | "ok"
const state = computed(() => {
  if (pendingCount.value > 0) return "indexing";
  if (okCount.value === 0)    return "empty";
  return "ok";
});

const iconClass = computed(() => ({
  "index-btn--warn":    state.value === "empty",
  "index-btn--muted":   state.value === "ok",
}));

const iconTitle = computed(() => ({
  empty:   "Search index not yet initialized",
  indexing: `Indexing in progress — ${pendingCount.value} entities queued`,
  ok:      `Search index up to date — ${okCount.value} entities indexed`,
}[state.value]));

function togglePanel() {
  panelOpen.value = !panelOpen.value;
}

function onClickOutside(e) {
  if (statusRef.value && !statusRef.value.contains(e.target))
    panelOpen.value = false;
}

async function fetchStatus() {
  if (!isAdmin.value) return;
  try {
    const token = auth.getToken();
    const headers = {};
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;
    const res = await fetch("/api/management-embedding-status", { headers });
    if (res.ok) {
      const data     = await res.json();
      okCount.value  = data.okCount      ?? 0;
      pendingCount.value = data.pendingCount ?? 0;
    }
  } catch {
    // non-critical
  }
}

async function triggerReindex() {
  if (triggering.value) return;
  triggering.value = true;
  try {
    const token = auth.getToken();
    const headers = { "Content-Type": "application/json" };
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;
    const res = await fetch("/api/management-reindex", { method: "POST", headers });
    if (res.ok) {
      // Refresh status after a brief delay so the panel updates to "Indexing in progress"
      setTimeout(fetchStatus, 1500);
    }
  } catch {
    // silent
  } finally {
    triggering.value = false;
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
/* ── Icon button ─────────────────────────────────────────── */
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

.index-btn--warn  { color: #d97706; }
.index-btn--warn:hover { color: #b45309; background: #fef3c7; }
.index-btn--muted { color: var(--color-text-muted); }

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

/* ── Panel ───────────────────────────────────────────────── */
.index-panel {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  width: 200px;
  padding: 12px 14px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-md);
  z-index: 100;
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.index-panel-label {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  color: var(--color-text-muted);
  margin-bottom: 1px;
}

.index-panel-status {
  font-size: 13px;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 5px;
}

.index-panel-hint {
  font-size: 12px;
  color: var(--color-text-muted);
  line-height: 1.4;
}

.status--ok   { color: #059669; }
.status--warn { color: #d97706; }
.status--info { color: var(--color-text-secondary); }

.index-panel-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  margin-top: 6px;
  padding: 5px 10px;
  font-size: 12px;
  font-weight: 500;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm);
  background: var(--color-surface);
  color: var(--color-text-secondary);
  cursor: pointer;
  transition: background var(--transition-fast), border-color var(--transition-fast);
}

.index-panel-btn--primary {
  background: var(--color-primary);
  border-color: var(--color-primary);
  color: #fff;
}

.index-panel-btn--primary:hover:not(:disabled) {
  background: var(--color-primary-hover);
  border-color: var(--color-primary-hover);
}

.index-panel-btn:hover:not(:disabled) {
  background: var(--color-bg);
  border-color: var(--color-text-muted);
}

.index-panel-btn:disabled {
  opacity: 0.55;
  cursor: default;
}

/* ── Spinners ────────────────────────────────────────────── */
.spin {
  animation: spin 0.9s linear infinite;
}

.inline-spin {
  width: 11px;
  height: 11px;
  flex-shrink: 0;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
