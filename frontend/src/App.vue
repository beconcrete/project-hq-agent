<template>
  <!-- Loading -->
  <div v-if="auth.state.isLoading" class="auth-gate">
    <img src="/logo.jpg" alt="HQ Agent" class="signin-logo" />
  </div>

  <!-- Unauthenticated -->
  <div v-else-if="!auth.state.isAuthenticated" class="auth-gate">
    <img src="/logo.jpg" alt="HQ Agent" class="signin-logo" />
    <p v-if="auth.state.error === 'access_denied'" class="auth-error">
      Your account doesn't have access to this application.
    </p>
    <p v-else-if="auth.state.error === 'error'" class="auth-error">
      Something went wrong. Please try again.
    </p>
    <button
      v-if="auth.state.error === 'access_denied'"
      class="btn btn-secondary login-btn"
      @click="auth.logout()"
    >
      Sign out
    </button>
    <button v-else class="btn btn-primary login-btn" @click="auth.login()">
      Sign in
    </button>
  </div>

  <!-- Authenticated app shell -->
  <div v-else class="app-layout">
    <AppNav />
    <main
      class="main-content"
      :class="{ 'sidebar-collapsed': sidebar.isCollapsed.value }"
    >
      <AppHeader />
      <div class="page-content">
        <router-view />
      </div>
    </main>
    <div
      class="sidebar-overlay"
      :class="{ open: sidebar.isMobileOpen.value }"
      @click="sidebar.closeMobile()"
    />
  </div>
</template>

<script setup>
import { onMounted } from "vue";
import { initAuth, useAuth } from "./composables/useAuth";
import { useSidebar } from "./composables/useSidebar";
import AppNav from "./components/AppNav.vue";
import AppHeader from "./components/AppHeader.vue";

const auth = useAuth();
const sidebar = useSidebar();

onMounted(initAuth);
</script>
