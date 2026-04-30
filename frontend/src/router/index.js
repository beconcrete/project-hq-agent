import { createRouter, createWebHistory } from "vue-router";
import HQPage from "../pages/HQPage.vue";
import GraphPage from "../pages/GraphPage.vue";
import HomePage from "../pages/HomePage.vue";
import ContractsPage from "../pages/ContractsPage.vue";
import AuthTestPage from "../pages/AuthTestPage.vue";
import HRPage from "../pages/HRPage.vue";
import SalesForecastPage from "../pages/SalesForecastPage.vue";
import { useAuth } from "../composables/useAuth";

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/hq", component: HQPage },
    { path: "/graph", component: GraphPage, meta: { requiresAdmin: true } },
    { path: "/", component: HomePage },
    { path: "/contracts", component: ContractsPage },
    { path: "/auth-test", component: AuthTestPage },
    { path: "/hr", component: HRPage, meta: { requiresAdmin: true } },
    { path: "/sales-forecast", component: SalesForecastPage },
    { path: "/:pathMatch(.*)*", redirect: "/hq" },
  ],
});

router.beforeEach((to) => {
  if (to.meta.requiresAdmin) {
    const auth = useAuth();
    if (!auth.hasRole("admin")) return "/contracts";
  }
});
