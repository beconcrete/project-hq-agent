<template>
  <Transition name="detail-panel">
    <aside
      v-if="node"
      class="contract-node-detail"
      :style="{ '--node-accent': accentColor }"
    >
      <button class="detail-close" type="button" @click="$emit('close')">
        ×
      </button>

      <!-- Contract mode -->
      <template v-if="node.type === 'contract'">
        <div class="detail-badge">
          {{ node.data.documentType || "Contract" }}
        </div>
        <h3 class="detail-title">
          {{ node.data.fileName || node.data.documentType || "Contract" }}
        </h3>

        <dl class="detail-fields">
          <div class="detail-row">
            <dt>Counterparty</dt>
            <dd>
              {{ node.data.counterparty || "—" }}
              <span v-if="node.data.manualPartyOverride" class="detail-tag"
                >Manually assigned</span
              >
            </dd>
          </div>
          <div class="detail-row">
            <dt>Expires</dt>
            <dd>{{ node.data.expiryDate || "—" }}</dd>
          </div>
          <div v-if="node.data.renewalStatus" class="detail-row">
            <dt>Renewal</dt>
            <dd>{{ node.data.renewalStatus }}</dd>
          </div>
          <div class="detail-row">
            <dt>Review</dt>
            <dd>
              <span
                class="review-chip"
                :class="`review-chip--${node.data.reviewState}`"
              >
                {{ formatReviewState(node.data.reviewState) }}
              </span>
            </dd>
          </div>
          <div v-if="paymentSummary" class="detail-row">
            <dt>Payment</dt>
            <dd>{{ paymentSummary }}</dd>
          </div>
        </dl>

        <div v-if="riskFlags.length" class="detail-risks">
          <p class="detail-section-label">Risk flags</p>
          <ul class="detail-risk-list">
            <li v-for="flag in riskFlags" :key="flag">{{ flag }}</li>
          </ul>
        </div>

        <div class="detail-actions">
          <button
            class="detail-btn detail-btn--primary"
            type="button"
            @click="$emit('assign-party', node)"
          >
            Assign to party
          </button>
          <template v-if="!deleteConfirm">
            <button
              class="detail-btn detail-btn--danger"
              type="button"
              @click="deleteConfirm = true"
            >
              Delete
            </button>
          </template>
          <template v-else>
            <div class="detail-confirm">
              <span class="detail-confirm-label">Delete permanently?</span>
              <button
                class="detail-btn detail-btn--danger"
                type="button"
                :disabled="deleting"
                @click="doDelete"
              >
                {{ deleting ? "Deleting…" : "Yes, delete" }}
              </button>
              <button
                class="detail-btn detail-btn--ghost"
                type="button"
                @click="deleteConfirm = false"
              >
                Cancel
              </button>
            </div>
          </template>
          <p v-if="deleteError" class="detail-err">{{ deleteError }}</p>
        </div>
      </template>

      <!-- Party mode -->
      <template v-else-if="node.type === 'party'">
        <div class="detail-badge">Party</div>
        <h3 class="detail-title">{{ node.data.label }}</h3>
        <p class="detail-sub">
          {{ node.data.contractCount }}
          {{ node.data.contractCount === 1 ? "contract" : "contracts" }}
        </p>

        <section class="detail-people">
          <p class="detail-section-label">People</p>
          <ul
            v-if="node.data.people && node.data.people.length"
            class="detail-people-list"
          >
            <li v-for="person in node.data.people" :key="person">
              {{ person }}
            </li>
          </ul>
          <p v-else class="detail-empty">
            No people identified in these contracts
          </p>
        </section>
      </template>
    </aside>
  </Transition>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useAuth } from "../composables/useAuth";

const props = defineProps({
  node: { type: Object, default: null },
  accentColor: { type: String, default: "#126b5f" },
});
const emit = defineEmits(["close", "assign-party", "contract-deleted"]);

const auth = useAuth();
const deleteConfirm = ref(false);
const deleting = ref(false);
const deleteError = ref("");

const riskFlags = computed(() => {
  const flags = props.node?.data?.riskFlags;
  if (!flags || !Array.isArray(flags)) return [];
  return flags;
});

const paymentSummary = computed(() => {
  const d = props.node?.data;
  if (!d) return null;
  const parts = [];
  if (d.paymentAmount != null) {
    const formatted = Number(d.paymentAmount).toLocaleString("sv-SE");
    const currency = d.paymentCurrency || "";
    parts.push(currency ? `${formatted} ${currency}` : formatted);
  }
  if (d.paymentUnit) parts.push(d.paymentUnit);
  if (d.paymentType) parts.push(d.paymentType);
  return parts.length ? parts.join(" · ") : null;
});

const REVIEW_LABELS = {
  approved: "Approved",
  approved_by_extraction: "Approved by extraction",
  pending_review: "Pending review",
  rejected: "Rejected",
  duplicate_deleted: "Duplicate",
  failed: "Failed",
};

function formatReviewState(state) {
  if (!state) return "—";
  return (
    REVIEW_LABELS[state] ??
    state.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase())
  );
}

watch(
  () => props.node,
  () => {
    deleteConfirm.value = false;
    deleting.value = false;
    deleteError.value = "";
  },
);

async function doDelete() {
  deleting.value = true;
  deleteError.value = "";
  try {
    const res = await fetch("/api/contract-delete", {
      method: "DELETE",
      headers: {
        "Content-Type": "application/json",
        "X-Auth-Token": `Bearer ${auth.getToken()}`,
      },
      body: JSON.stringify({ rowKey: props.node.data.rowKey }),
    });
    if (!res.ok) throw new Error(`Delete failed (${res.status})`);
    emit("contract-deleted", props.node.data.rowKey);
  } catch (e) {
    deleteError.value = e.message || "Delete failed";
    deleting.value = false;
  }
}
</script>

<style scoped>
.contract-node-detail {
  --node-accent: #126b5f;
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  width: 300px;
  background: #fff;
  border-left: 1px solid #ddd7cd;
  padding: 1.25rem 1rem;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  z-index: 10;
}

.detail-close {
  position: absolute;
  top: 0.75rem;
  right: 0.75rem;
  width: 1.75rem;
  height: 1.75rem;
  border: 0;
  border-radius: 6px;
  background: #f0ede6;
  color: #746f67;
  font-size: 1.1rem;
  line-height: 1;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
}
.detail-close:hover {
  background: #e5e0d8;
}

.detail-badge {
  display: inline-block;
  padding: 0.2rem 0.6rem;
  border-radius: 999px;
  background: var(--node-accent);
  color: #fff;
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  width: fit-content;
}

.detail-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: #181511;
  margin: 0;
  padding-right: 1.5rem;
  line-height: 1.3;
}

.detail-sub {
  font-size: 0.8rem;
  color: #746f67;
  margin: 0;
}

.detail-fields {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin: 0;
}

.detail-row {
  display: flex;
  flex-direction: column;
  gap: 0.18rem;
}

.detail-row dt {
  font-size: 0.68rem;
  font-weight: 700;
  color: #9a9388;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.detail-row dd {
  font-size: 0.84rem;
  color: #181511;
  margin: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem;
  line-height: 1.35;
}

.detail-risks {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  padding-top: 0.5rem;
  border-top: 1px solid #f0ede6;
}

.detail-risk-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.detail-risk-list li {
  font-size: 0.78rem;
  color: #92400e;
  background: #fef3c7;
  border-radius: 5px;
  padding: 0.2rem 0.5rem;
}

.detail-tag {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
  background: #e7f2ef;
  color: #126b5f;
}

.review-chip {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 0.15rem 0.5rem;
  border-radius: 6px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.review-chip--approved {
  background: #d1fae5;
  color: #065f46;
}
.review-chip--pending_review {
  background: #fef3c7;
  color: #92400e;
}
.review-chip--approved_by_extraction {
  background: #dcfce7;
  color: #166534;
}
.review-chip--rejected {
  background: #fee2e2;
  color: #991b1b;
}
.review-chip--duplicate_deleted {
  background: #f3f4f6;
  color: #374151;
}
.review-chip--failed {
  background: #fee2e2;
  color: #991b1b;
}

.detail-actions {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-top: auto;
  padding-top: 0.75rem;
  border-top: 1px solid #f0ede6;
}

.detail-btn {
  width: 100%;
  padding: 0.55rem 0.75rem;
  border: 0;
  border-radius: 8px;
  font: inherit;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.12s;
}
.detail-btn:disabled {
  opacity: 0.5;
  cursor: default;
}

.detail-btn--primary {
  background: var(--node-accent);
  color: #fff;
}
.detail-btn--primary:hover:not(:disabled) {
  opacity: 0.88;
}

.detail-btn--danger {
  background: #fee2e2;
  color: #991b1b;
}
.detail-btn--danger:hover:not(:disabled) {
  background: #fecaca;
}

.detail-btn--ghost {
  background: #f0ede6;
  color: #746f67;
}
.detail-btn--ghost:hover {
  background: #e5e0d8;
}

.detail-confirm {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.detail-confirm-label {
  font-size: 0.78rem;
  color: #991b1b;
  font-weight: 600;
}

.detail-err {
  font-size: 0.75rem;
  color: #991b1b;
  margin: 0;
}

.detail-section-label {
  font-size: 0.7rem;
  font-weight: 700;
  color: #9a9388;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  margin: 0 0 0.4rem;
}

.detail-people-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}
.detail-people-list li {
  font-size: 0.82rem;
  color: #181511;
  padding: 0.3rem 0.5rem;
  background: #fbfaf7;
  border: 1px solid #ddd7cd;
  border-radius: 6px;
}

.detail-empty {
  font-size: 0.8rem;
  color: #9a9388;
  margin: 0;
  font-style: italic;
}

/* Slide-in transition */
.detail-panel-enter-active,
.detail-panel-leave-active {
  transition:
    transform 0.22s ease,
    opacity 0.22s ease;
}
.detail-panel-enter-from,
.detail-panel-leave-to {
  transform: translateX(100%);
  opacity: 0;
}
</style>
