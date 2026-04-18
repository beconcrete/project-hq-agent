<template>
  <header class="topbar">
    <button
      class="topbar-menu-btn"
      aria-label="Open menu"
      @click="sidebar.toggleMobile()"
    >
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
      >
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
      <span class="topbar-user-name">{{ auth.state.user?.displayName }}</span>
      <button class="btn btn-ghost btn-sm" @click="auth.logout()">
        Sign out
      </button>
    </div>
  </header>
</template>

<script setup>
import { computed } from "vue";
import { useRoute } from "vue-router";
import { useAuth } from "../composables/useAuth";
import { useSidebar } from "../composables/useSidebar";

const auth = useAuth();
const sidebar = useSidebar();
const route = useRoute();

const breadcrumb = computed(() => {
  const map = {
    "/": "Home",
    "/contracts": "Contracts",
    "/auth-test": "Auth Test",
  };
  return map[route.path] ?? "HQ Agent";
});
</script>
