import { createRouter, createWebHistory } from "vue-router";
import HomePage from "../pages/HomePage.vue";
import ContractsPage from "../pages/ContractsPage.vue";
import AuthTestPage from "../pages/AuthTestPage.vue";

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", component: HomePage },
    { path: "/contracts", component: ContractsPage },
    { path: "/auth-test", component: AuthTestPage },
    { path: "/:pathMatch(.*)*", redirect: "/contracts" },
  ],
});
