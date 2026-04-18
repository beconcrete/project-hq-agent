<template>
  <section class="module auth-test-module">
    <div class="module-header">
      <h1>Auth Test</h1>
      <p class="module-subtitle">
        Check role-based authorization for the signed-in user against Be
        Concrete ID.
      </p>
    </div>
    <div class="auth-test-panel">
      <p class="auth-test-description">
        <strong>User</strong> — requires the <code>user</code> or
        <code>admin</code> role.<br />
        <strong>Admin</strong> — requires the <code>admin</code> role.
      </p>
      <div class="auth-test-actions">
        <button
          class="btn btn-ghost"
          :disabled="loading"
          @click="runTest('user')"
        >
          User
        </button>
        <button
          class="btn btn-primary"
          :disabled="loading"
          @click="runTest('admin')"
        >
          Admin
        </button>
      </div>
      <pre
        v-if="result !== null"
        class="auth-test-result"
        :class="resultClass"
        >{{ result }}</pre
      >
    </div>
  </section>
</template>

<script setup>
import { ref, computed } from "vue";
import { useAuth } from "../composables/useAuth";

const auth = useAuth();
const loading = ref(false);
const result = ref(null);
const resultOk = ref(false);

const resultClass = computed(() =>
  resultOk.value ? "auth-test-result--ok" : "auth-test-result--denied",
);

async function runTest(action) {
  loading.value = true;
  result.value = null;
  try {
    const res = await fetch(`/api/auth-test?action=${action}`, {
      headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
    });
    const data = await res.json();
    resultOk.value = res.ok;
    result.value = JSON.stringify(data, null, 2);
  } catch {
    resultOk.value = false;
    result.value = "Error: could not reach the auth service.";
  } finally {
    loading.value = false;
  }
}
</script>
