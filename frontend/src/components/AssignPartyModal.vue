<template>
  <Teleport to="body">
    <div class="modal-backdrop" @click.self="$emit('cancel')">
      <div class="modal-card">
        <h3 class="modal-title">Assign to party</h3>

        <div class="modal-field">
          <label class="modal-label" for="party-select">Party</label>
          <select id="party-select" v-model="selected" class="modal-select">
            <option value="" disabled>Select a party…</option>
            <option v-for="party in knownParties" :key="party" :value="party">
              {{ party }}
            </option>
            <option disabled value="__divider__">──────────</option>
            <option value="__new__">New party…</option>
            <option value="__remove__">Remove override</option>
          </select>
        </div>

        <div v-if="selected === '__new__'" class="modal-field">
          <label class="modal-label" for="new-party-input">Party name</label>
          <input
            id="new-party-input"
            v-model="newPartyName"
            class="modal-input"
            placeholder="e.g. Euroclear Sweden AB"
            autocomplete="off"
          />
        </div>

        <p v-if="errorMsg" class="modal-err">{{ errorMsg }}</p>

        <div class="modal-actions">
          <button
            class="modal-btn modal-btn--primary"
            type="button"
            :disabled="!canConfirm || saving"
            @click="confirm"
          >
            {{ saving ? "Saving…" : "Confirm" }}
          </button>
          <button
            class="modal-btn modal-btn--ghost"
            type="button"
            @click="$emit('cancel')"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useAuth } from "../composables/useAuth";

const props = defineProps({
  contract: { type: Object, required: true },
  knownParties: { type: Array, default: () => [] },
});
const emit = defineEmits(["assigned", "cancel"]);

const auth = useAuth();
const selected = ref("");
const newPartyName = ref("");
const saving = ref(false);
const errorMsg = ref("");

onMounted(() => {
  if (props.contract.manualPartyOverride) {
    const existing = props.knownParties.find(
      (p) => p === props.contract.manualPartyOverride,
    );
    selected.value = existing ?? "__new__";
    if (!existing) newPartyName.value = props.contract.manualPartyOverride;
  }
});

const resolvedParty = computed(() => {
  if (selected.value === "__new__") return newPartyName.value.trim();
  if (selected.value === "__remove__") return "";
  return selected.value;
});

const canConfirm = computed(() => {
  if (!selected.value || selected.value === "__divider__") return false;
  if (selected.value === "__new__") return newPartyName.value.trim().length > 0;
  return true;
});

async function confirm() {
  saving.value = true;
  errorMsg.value = "";
  try {
    const res = await fetch("/api/contract-assign", {
      method: "PATCH",
      headers: {
        "Content-Type": "application/json",
        "X-Auth-Token": `Bearer ${auth.getToken()}`,
      },
      body: JSON.stringify({
        rowKey: props.contract.rowKey,
        party: resolvedParty.value,
      }),
    });
    if (!res.ok) throw new Error(`Request failed (${res.status})`);
    emit("assigned", props.contract.rowKey, resolvedParty.value);
  } catch (e) {
    errorMsg.value = e.message || "Failed to assign party";
    saving.value = false;
  }
}
</script>

<style scoped>
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(24, 21, 17, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-card {
  background: #fff;
  border-radius: 18px;
  padding: 1.75rem;
  width: min(420px, calc(100vw - 2rem));
  display: flex;
  flex-direction: column;
  gap: 1rem;
  box-shadow: 0 24px 64px rgba(24, 21, 17, 0.22);
}

.modal-title {
  font-size: 1rem;
  font-weight: 700;
  color: #181511;
  margin: 0;
}

.modal-field {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.modal-label {
  font-size: 0.72rem;
  font-weight: 700;
  color: #9a9388;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.modal-select,
.modal-input {
  width: 100%;
  padding: 0.6rem 0.75rem;
  border: 1px solid #ddd7cd;
  border-radius: 8px;
  background: #fbfaf7;
  color: #181511;
  font: inherit;
  font-size: 0.88rem;
}
.modal-select:focus,
.modal-input:focus {
  outline: 2px solid #126b5f;
  outline-offset: 1px;
}

.modal-err {
  font-size: 0.78rem;
  color: #991b1b;
  margin: 0;
}

.modal-actions {
  display: flex;
  gap: 0.6rem;
  margin-top: 0.25rem;
}

.modal-btn {
  flex: 1;
  padding: 0.6rem 0.75rem;
  border: 0;
  border-radius: 9px;
  font: inherit;
  font-size: 0.86rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.12s;
}
.modal-btn:disabled {
  opacity: 0.45;
  cursor: default;
}

.modal-btn--primary {
  background: #126b5f;
  color: #fff;
}
.modal-btn--primary:hover:not(:disabled) {
  opacity: 0.88;
}

.modal-btn--ghost {
  background: #f0ede6;
  color: #746f67;
}
.modal-btn--ghost:hover {
  background: #e5e0d8;
}
</style>
