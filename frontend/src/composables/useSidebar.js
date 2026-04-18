import { ref } from "vue";

const isCollapsed = ref(localStorage.getItem("sidebarCollapsed") === "true");
const isMobileOpen = ref(false);

export function useSidebar() {
  return {
    isCollapsed,
    isMobileOpen,

    toggleDesktop() {
      isCollapsed.value = !isCollapsed.value;
      localStorage.setItem("sidebarCollapsed", isCollapsed.value);
    },

    toggleMobile() {
      isMobileOpen.value = !isMobileOpen.value;
    },

    closeMobile() {
      isMobileOpen.value = false;
    },
  };
}
