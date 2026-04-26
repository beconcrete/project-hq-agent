<template>
  <aside
    class="sidebar"
    :class="{
      'sidebar-collapsed': sidebar.isCollapsed.value,
      'sidebar-open': sidebar.isMobileOpen.value,
    }"
  >
    <div class="sidebar-header">
      <router-link
        to="/"
        class="sidebar-logo-btn"
        aria-label="Go to home"
        @click="sidebar.closeMobile()"
      >
        <div class="sidebar-logo">
          <span class="logo-mark">HQ</span>
          <span class="logo-text">Agent</span>
        </div>
      </router-link>
      <button
        class="sidebar-toggle"
        aria-label="Toggle navigation"
        @click="sidebar.toggleDesktop()"
      >
        <span></span><span></span><span></span>
      </button>
    </div>

    <nav class="sidebar-nav" aria-label="Main navigation">
      <ul class="nav-list">
        <li class="nav-item" :class="{ active: $route.path === '/contracts' }">
          <router-link
            to="/contracts"
            class="nav-link"
            @click="sidebar.closeMobile()"
          >
            <svg
              class="nav-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <path
                d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
              />
              <polyline points="14 2 14 8 20 8" />
              <line x1="16" y1="13" x2="8" y2="13" />
              <line x1="16" y1="17" x2="8" y2="17" />
              <polyline points="10 9 9 9 8 9" />
            </svg>
            <span class="nav-label">Contracts</span>
          </router-link>
        </li>
        <li class="nav-item" :class="{ active: $route.path === '/auth-test' }">
          <router-link
            to="/auth-test"
            class="nav-link"
            @click="sidebar.closeMobile()"
          >
            <svg
              class="nav-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
              <polyline points="9 12 11 14 15 10" />
            </svg>
            <span class="nav-label">Auth Test</span>
          </router-link>
        </li>

        <!-- HR — admin only -->
        <li
          v-if="auth.hasRole('admin')"
          class="nav-item"
          :class="{ active: $route.path === '/hr' }"
        >
          <router-link to="/hr" class="nav-link" @click="sidebar.closeMobile()">
            <svg
              class="nav-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
              <path d="M16 3.13a4 4 0 0 1 0 7.75" />
            </svg>
            <span class="nav-label">HR</span>
          </router-link>
        </li>

        <li
          class="nav-item"
          :class="{ active: $route.path === '/sales-forecast' }"
        >
          <router-link
            to="/sales-forecast"
            class="nav-link"
            @click="sidebar.closeMobile()"
          >
            <svg
              class="nav-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <line x1="4" y1="20" x2="20" y2="20" />
              <line x1="7" y1="16" x2="7" y2="10" />
              <line x1="12" y1="16" x2="12" y2="6" />
              <line x1="17" y1="16" x2="17" y2="12" />
            </svg>
            <span class="nav-label">Sales Forecast</span>
          </router-link>
        </li>

        <!-- Coming soon modules -->
        <li
          v-for="item in comingSoon"
          :key="item.label"
          class="nav-item coming-soon"
        >
          <a href="#" class="nav-link" aria-disabled="true" @click.prevent>
            <svg
              class="nav-icon"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              v-html="item.icon"
            />
            <span class="nav-label">{{ item.label }}</span>
            <span class="nav-badge">Soon</span>
          </a>
        </li>
      </ul>
    </nav>

    <div class="sidebar-footer">
      <span class="sidebar-version">v0.1.0</span>
    </div>
  </aside>
</template>

<script setup>
import { useSidebar } from "../composables/useSidebar";
import { useAuth } from "../composables/useAuth";

const sidebar = useSidebar();
const auth = useAuth();

const comingSoon = [
  {
    label: "Finance",
    icon: '<line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>',
  },
  {
    label: "Procurement",
    icon: '<circle cx="9" cy="21" r="1"/><circle cx="20" cy="21" r="1"/><path d="M1 1h4l2.68 13.39a2 2 0 0 0 2 1.61h9.72a2 2 0 0 0 2-1.61L23 6H6"/>',
  },
  {
    label: "Legal",
    icon: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>',
  },
  {
    label: "Projects",
    icon: '<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>',
  },
  {
    label: "Compliance",
    icon: '<path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>',
  },
  {
    label: "Analytics",
    icon: '<line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>',
  },
  {
    label: "Vendors",
    icon: '<path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>',
  },
  {
    label: "Settings",
    icon: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
  },
];
</script>
