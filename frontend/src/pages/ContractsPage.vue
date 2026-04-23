<template>
  <section class="contracts-page">
    <div class="chat-shell">
      <header class="chat-topbar">
        <div class="identity">
          <div class="mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <path
                d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
              />
              <path d="M14 2v6h6" />
            </svg>
          </div>
          <div>
            <h2 class="chat-heading">Contract Assistant</h2>
            <span class="chat-subheading"
              >Be Concrete AB contract intelligence</span
            >
          </div>
        </div>

        <button
          v-if="chatMessages.length"
          class="icon-btn"
          type="button"
          @click="clearChat"
          aria-label="New conversation"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
            <path d="M12 5v14M5 12h14" />
          </svg>
        </button>
      </header>

      <div v-if="selectedContract" class="chat-context">
        <div class="chat-context-inner">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
            <path
              d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
            />
            <path d="M14 2v6h6" />
          </svg>
          <span class="chat-context-name">{{
            selectedContract.documentType || selectedContract.fileName
          }}</span>
          <span class="chat-context-label">selected</span>
        </div>
        <button
          class="chat-context-dismiss"
          type="button"
          @click="selectedId = null"
          aria-label="Clear selected contract"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
        </button>
      </div>

      <div ref="chatScroll" class="conversation">
        <div class="thread">
          <section v-if="!chatMessages.length && !chatPending" class="welcome">
            <h1>What should we know about the contracts?</h1>
            <p>
              Ask about renewals, NDAs, consulting assignments, people, payment
              terms, risks, or anything tied to the uploaded agreements.
            </p>
            <div class="chat-suggestions">
              <button
                v-for="suggestion in suggestions"
                :key="suggestion"
                type="button"
                @click="useSuggestion(suggestion)"
              >
                {{ suggestion }}
              </button>
            </div>
          </section>

          <article
            v-for="msg in chatMessages"
            :key="msg.id"
            :class="['message', msg.role === 'user' ? 'user' : 'assistant']"
          >
            <div v-if="msg.role === 'assistant'" class="avatar">CA</div>
            <div class="message-body">
              <div :class="['bubble', msg.error && 'bubble--error']">
                {{ msg.content }}
              </div>
              <div
                v-if="msg.role === 'assistant' && msg.references?.length"
                class="refs"
              >
                <button
                  v-for="ref in msg.references"
                  :key="referenceCorrelationId(ref)"
                  type="button"
                  class="ref-chip"
                  @click="openContractReference(ref)"
                >
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
                    <path
                      d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
                    />
                    <path d="M14 2v6h6" />
                  </svg>
                  <span>{{ referenceLabel(ref) }}</span>
                </button>
              </div>
              <span
                v-if="msg.role === 'assistant' && !msg.error && msg.model"
                class="model-tag"
                >{{ msg.model }}</span
              >
            </div>
            <div v-if="msg.role === 'user'" class="avatar">BE</div>
          </article>

          <article v-if="chatPending" class="message assistant">
            <div class="avatar">CA</div>
            <div class="typing-dots"><span /><span /><span /></div>
          </article>
        </div>
      </div>

      <footer class="composer-wrap">
        <form class="composer" @submit.prevent="sendMessage">
          <textarea
            ref="chatInputEl"
            v-model="chatInput"
            rows="1"
            :placeholder="
              selectedContract
                ? 'Ask about this contract or all contracts...'
                : 'Ask about your contracts...'
            "
            :disabled="chatPending"
            @input="resizeComposer"
            @keydown="onComposerKeydown"
          />
          <button
            class="send-btn"
            type="submit"
            :disabled="!chatInput.trim() || chatPending"
            aria-label="Send"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <path d="M22 2 11 13" />
              <path d="m22 2-7 20-4-9-9-4 20-7Z" />
            </svg>
          </button>
        </form>
        <div class="compose-hint">
          Enter sends. Shift Enter adds a new line. Arrow Up recalls earlier
          questions.
        </div>
      </footer>
    </div>

    <section v-if="attentionContracts.length" class="attention-strip">
      <div>
        <span class="attention-kicker">Needs attention</span>
        <strong>{{ attentionContracts.length }} contract{{ attentionContracts.length === 1 ? "" : "s" }}</strong>
      </div>
      <div class="attention-items">
        <button
          v-for="contract in attentionContracts.slice(0, 4)"
          :key="contract.correlationId"
          type="button"
          @click="selectContract(contract)"
        >
          <span>{{ contract.fileName || contract.documentType || "Contract" }}</span>
          <small>{{ lifecycleLabel(contract) }}</small>
        </button>
      </div>
    </section>

    <div class="workspace">
      <div class="accordion" :class="{ open: contractsOpen }">
        <button
          class="accordion-trigger"
          type="button"
          @click="contractsOpen = !contractsOpen"
        >
          <span class="accordion-label">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <path d="M4 7h16M4 12h16M4 17h10" />
            </svg>
            Existing Contracts
            <span v-if="contracts.length" class="badge-count">{{
              contracts.length
            }}</span>
          </span>
          <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor">
            <path d="m6 9 6 6 6-6" />
          </svg>
        </button>
        <div class="accordion-panel">
          <div class="accordion-panel-inner">
            <div v-if="listLoading" class="panel-state">Loading...</div>
            <div
              v-else-if="contracts.length === 0"
              class="panel-state panel-state--empty"
            >
              No contracts yet
            </div>
            <div v-else class="contract-list">
              <div
                v-for="contract in contracts"
                :key="contract.correlationId"
                class="contract-item"
                :class="{ 'contract-item--review': auth.hasRole('admin') && isPendingReview(contract) }"
              >
                <div
                  class="contract-row"
                  :class="{
                    'contract-row--clickable': isClickable(contract),
                    'contract-row--selected':
                      selectedId === contract.correlationId,
                    'contract-row--processing': contract.status === 'processing',
                  }"
                  @click="onCardClick(contract)"
                >
                  <div class="contract-row-icon">
                    <svg
                      :class="{ 'spin-icon': contract.status === 'processing' }"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                    >
                      <path
                        v-if="contract.status === 'processing'"
                        d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"
                      />
                      <template v-else-if="contract.status === 'failed'">
                        <circle cx="12" cy="12" r="10" />
                        <path d="M12 8v4M12 16h.01" />
                      </template>
                      <template v-else>
                        <path
                          d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
                        />
                        <path d="M14 2v6h6" />
                      </template>
                    </svg>
                  </div>
                  <div class="contract-row-body">
                    <div class="contract-row-name">
                      {{ contract.fileName || "Contract" }}
                    </div>
                    <div class="contract-row-meta">
                      <template v-if="contract.status === 'processing'"
                        >{{ contract.statusMessage || "Extracting fields..." }}</template
                      >
                      <template v-else-if="contract.status === 'failed'"
                        >{{ contract.lastError || contract.statusMessage || "Extraction failed" }}</template
                      >
                      <template v-else>
                        {{ contract.documentType || "Document" }} ·
                        {{ lifecycleLabel(contract) || formatDate(contract.uploadedAt) }}
                      </template>
                    </div>
                    <div v-if="paymentLabel(contract)" class="contract-row-payment">
                      {{ paymentLabel(contract) }}
                    </div>
                    <div v-if="relationshipLabel(contract)" class="contract-row-relation">
                      {{ relationshipLabel(contract) }}
                    </div>
                  </div>
                  <div class="contract-row-end">
                    <span
                      v-if="contract.status === 'processing'"
                      class="badge badge--blue"
                      >Processing</span
                    >
                    <span
                      v-else-if="contract.status === 'pending_review'"
                      class="badge badge--amber"
                      >Review</span
                    >
                    <span
                      v-else-if="contract.status === 'failed'"
                      class="badge badge--red"
                      >Failed</span
                    >
                    <button
                      v-if="contract.status === 'failed'"
                      class="btn-ghost-sm"
                      type="button"
                      @click.stop="dismissFailed(contract.correlationId)"
                    >
                      Dismiss
                    </button>
                  </div>
                </div>
                <section
                  v-if="auth.hasRole('admin') && isPendingReview(contract)"
                  class="review-panel"
                >
                  <div class="review-panel-head">
                    <div>
                      <strong>Review this upload</strong>
                      <p>{{ reviewSummary(contract) }}</p>
                    </div>
                    <span class="review-panel-state">Admin action</span>
                  </div>
                  <div
                    v-if="relationshipCandidates(contract).length"
                    class="candidate-list"
                  >
                    <div class="candidate-kicker">Possible related contract</div>
                    <button
                      v-for="candidate in relationshipCandidates(contract)"
                      :key="candidate.correlationId"
                      type="button"
                      :class="[
                        'candidate',
                        selectedRelatedId(contract) === candidate.correlationId && 'candidate--selected',
                      ]"
                      @click.stop="setSelectedRelatedId(contract, candidate.correlationId)"
                    >
                      <span>{{ candidate.fileName || candidate.documentType || "Related contract" }}</span>
                      <small>{{ candidateLabel(candidate) }}</small>
                    </button>
                  </div>
                  <div class="review-actions">
                    <button
                      class="btn-ghost-sm"
                      type="button"
                      :disabled="reviewPending === contract.correlationId"
                      @click.stop="reviewContract(contract, 'approve_as_new')"
                    >
                      Approve new
                    </button>
                    <button
                      v-if="relationshipCandidates(contract).length"
                      class="btn-ghost-sm"
                      type="button"
                      :disabled="reviewPending === contract.correlationId"
                      @click.stop="reviewContract(contract, 'mark_extension')"
                    >
                      Mark extension
                    </button>
                    <button
                      v-if="relationshipCandidates(contract).length"
                      class="btn-ghost-sm"
                      type="button"
                      :disabled="reviewPending === contract.correlationId"
                      @click.stop="reviewContract(contract, 'mark_replacement')"
                    >
                      Mark replacement
                    </button>
                    <button
                      v-if="relationshipCandidates(contract).length"
                      class="btn-ghost-sm btn-ghost-sm--danger"
                      type="button"
                      :disabled="reviewPending === contract.correlationId"
                      @click.stop="reviewContract(contract, 'mark_duplicate_delete')"
                    >
                      Duplicate, delete
                    </button>
                    <button
                      class="btn-ghost-sm btn-ghost-sm--danger"
                      type="button"
                      :disabled="reviewPending === contract.correlationId"
                      @click.stop="reviewContract(contract, 'reject_delete')"
                    >
                      Reject, delete
                    </button>
                  </div>
                  <p v-if="relationshipCandidates(contract).length" class="review-action-hint">
                    Use replacement only when this upload should supersede the selected contract.
                    Use extension when it is a related new period. Duplicate and reject hide this upload.
                  </p>
                </section>
              </div>
            </div>
          </div>
        </div>
      </div>

      <div
        v-if="auth.hasRole('admin')"
        class="accordion"
        :class="{ open: uploadOpen }"
      >
        <button
          class="accordion-trigger"
          type="button"
          @click="uploadOpen = !uploadOpen"
        >
          <span class="accordion-label">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <path d="m17 8-5-5-5 5M12 3v12" />
            </svg>
            Upload New
          </span>
          <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor">
            <path d="m6 9 6 6 6-6" />
          </svg>
        </button>
        <div class="accordion-panel">
          <div class="accordion-panel-inner">
            <div
              class="dropzone"
              :class="{ 'dropzone--over': isDragging }"
              @click="fileInput?.click()"
              @dragover.prevent="isDragging = true"
              @dragleave.prevent="isDragging = false"
              @drop.prevent="onDrop"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                <path d="m17 8-5-5-5 5M12 3v12" />
              </svg>
              <p class="dropzone-title">Drop contract here</p>
              <p class="dropzone-hint">or click to browse</p>
              <div class="dropzone-tags">
                <span>PDF</span><span>DOCX</span><span>Max 20 MB</span>
              </div>
            </div>
            <div v-if="uploadState === 'uploading'" class="upload-status">
              <div class="upload-bar"><div class="upload-fill" /></div>
              <span>Uploading...</span>
            </div>
            <div v-if="uploadState === 'error'" class="upload-err">
              <span>{{ uploadError }}</span>
              <button class="btn-ghost-sm" type="button" @click="uploadState = 'idle'">
                Try again
              </button>
            </div>
            <input
              ref="fileInput"
              type="file"
              accept=".pdf,.docx"
              hidden
              @change="onFileChange"
            />
          </div>
        </div>
      </div>

      <aside class="workspace-right" aria-hidden="true">
        <div v-if="detailData">{{ flatFields(detailData.fields) }}</div>
      </aside>
    </div>
  </section>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from "vue";
import { useAuth } from "../composables/useAuth";

const auth = useAuth();

const fileInput = ref(null);
const isDragging = ref(false);
const uploadState = ref("idle");
const uploadError = ref("");

const contracts = ref([]);
const listLoading = ref(true);
const selectedId = ref(null);
const detailData = ref(null);
const detailLoading = ref(false);
const detailError = ref("");
const polls = new Map();
const reviewPending = ref("");

const contractsOpen = ref(false);
const uploadOpen = ref(false);

const sessionId = ref(crypto.randomUUID());
const chatMessages = ref([]);
const chatInput = ref("");
const chatPending = ref(false);
const chatScroll = ref(null);
const chatInputEl = ref(null);
const promptHistory = ref([]);
const historyIndex = ref(0);
const selectedRelatedIds = ref({});

const suggestions = [
  "Which contracts expire next?",
  "Do we have an NDA with Microsoft Sweden?",
  "Which contracts affect Björn Eriksen?",
];

// Keep a stable front-end touchpoint here for small deploy-only nudges when needed.

const selectedContract = computed(
  () =>
    contracts.value.find((c) => c.correlationId === selectedId.value) ?? null,
);

const attentionContracts = computed(() => {
  const now = new Date();
  const horizon = new Date(now);
  horizon.setDate(horizon.getDate() + 120);

  return contracts.value
    .filter((contract) => isClickable(contract))
    .filter((contract) => {
      if (isPendingReview(contract)) return true;
      const actionDate = lifecycleDate(contract);
      return actionDate && actionDate >= startOfDay(now) && actionDate <= horizon;
    })
    .sort((a, b) => {
      if (isPendingReview(a) !== isPendingReview(b)) return isPendingReview(a) ? -1 : 1;
      return (lifecycleDate(a)?.getTime() ?? Number.MAX_SAFE_INTEGER) -
        (lifecycleDate(b)?.getTime() ?? Number.MAX_SAFE_INTEGER);
    });
});

const MAX_SIZE = 20 * 1024 * 1024;
const ALLOWED_TYPES = [
  "application/pdf",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
];

onMounted(() => {
  loadContracts();
  nextTick(() => chatInputEl.value?.focus());
});
onUnmounted(() => polls.forEach((id) => clearInterval(id)));

async function loadContracts() {
  listLoading.value = true;
  try {
    const res = await fetch("/api/list-contracts", {
      headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
    });
    if (res.ok) {
      const data = await res.json();
      contracts.value = data;
      data
        .filter((c) => c.status === "processing")
        .forEach((c) => startPolling(c.correlationId));
    }
  } catch {
    /* empty list shown */
  } finally {
    listLoading.value = false;
  }
}

function startPolling(correlationId) {
  if (polls.has(correlationId)) return;
  const id = setInterval(() => pollStatus(correlationId), 3000);
  polls.set(correlationId, id);
}

async function pollStatus(correlationId) {
  try {
    const res = await fetch(
      `/api/check-status?correlationId=${correlationId}`,
      {
        headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
      },
    );
    if (!res.ok) return;
    const { status, statusMessage, lastError, retryCount } = await res.json();

    const idx = contracts.value.findIndex(
      (c) => c.correlationId === correlationId,
    );
    if (idx !== -1) {
      contracts.value[idx] = {
        ...contracts.value[idx],
        status,
        statusMessage: statusMessage ?? contracts.value[idx].statusMessage,
        lastError: lastError ?? contracts.value[idx].lastError,
        retryCount: retryCount ?? contracts.value[idx].retryCount,
      };
    }

    if (status === "processing") return;

    clearInterval(polls.get(correlationId));
    polls.delete(correlationId);
    if (idx === -1) return;
    if (status === "completed" || status === "pending_review") {
      const detail = await fetchDetail(correlationId);
      contracts.value[idx] = detail
        ? { ...contracts.value[idx], ...detail, status }
        : { ...contracts.value[idx], status };
    } else {
      contracts.value[idx] = { ...contracts.value[idx], status };
    }
  } catch {
    /* retry next tick */
  }
}

async function fetchDetail(correlationId) {
  try {
    const res = await fetch(
      `/api/get-contract?correlationId=${correlationId}`,
      {
        headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
      },
    );
    return res.ok ? await res.json() : null;
  } catch {
    return null;
  }
}

function isClickable(contract) {
  return (
    contract.status === "completed" || contract.status === "pending_review"
  );
}

async function onCardClick(contract) {
  await selectContract(contract);
}

async function selectContract(contract) {
  if (!isClickable(contract)) return;
  if (selectedId.value === contract.correlationId) {
    selectedId.value = null;
    return;
  }
  selectedId.value = contract.correlationId;
  detailData.value = null;
  detailError.value = "";
  detailLoading.value = true;
  try {
    const data = await fetchDetail(contract.correlationId);
    if (data) detailData.value = data;
    else detailError.value = "Failed to load contract details.";
  } catch {
    detailError.value = "Failed to load contract details.";
  } finally {
    detailLoading.value = false;
  }
}

watch(selectedId, (id) => {
  if (!id) {
    detailData.value = null;
    detailError.value = "";
  }
});

async function sendMessage() {
  const msg = chatInput.value.trim();
  if (!msg || chatPending.value) return;

  promptHistory.value.push(msg);
  historyIndex.value = promptHistory.value.length;
  chatInput.value = "";
  resizeComposer();

  chatMessages.value.push({
    id: crypto.randomUUID(),
    role: "user",
    content: msg,
  });
  chatPending.value = true;
  scrollChatToBottom();

  try {
    const res = await fetch("/api/contract-chat", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Auth-Token": `Bearer ${auth.getToken()}`,
      },
      body: JSON.stringify({
        correlationId: selectedId.value ?? undefined,
        sessionId: sessionId.value,
        message: msg,
      }),
    });
    if (!res.ok) throw new Error(`Chat failed (${res.status})`);
    const data = await res.json();
    chatMessages.value.push({
      id: crypto.randomUUID(),
      role: "assistant",
      content: data.answer,
      model: data.modelUsed,
      references: data.references ?? [],
    });
  } catch {
    chatMessages.value.push({
      id: crypto.randomUUID(),
      role: "assistant",
      content: "Something went wrong. Please try again.",
      error: true,
    });
  } finally {
    chatPending.value = false;
    scrollChatToBottom();
    nextTick(() => chatInputEl.value?.focus());
  }
}

function onComposerKeydown(event) {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    sendMessage();
    nextTick(() => chatInputEl.value?.focus());
    return;
  }

  if (
    event.key === "ArrowUp" &&
    promptHistory.value.length &&
    chatInputEl.value?.selectionStart === 0 &&
    chatInputEl.value?.selectionEnd === 0
  ) {
    event.preventDefault();
    historyIndex.value = Math.max(0, historyIndex.value - 1);
    chatInput.value = promptHistory.value[historyIndex.value] ?? "";
    resizeComposer();
    nextTick(() => {
      const input = chatInputEl.value;
      input?.setSelectionRange(input.value.length, input.value.length);
    });
  }

  if (event.key === "ArrowDown" && promptHistory.value.length) {
    event.preventDefault();
    historyIndex.value = Math.min(
      promptHistory.value.length,
      historyIndex.value + 1,
    );
    chatInput.value = promptHistory.value[historyIndex.value] ?? "";
    resizeComposer();
  }
}

function useSuggestion(suggestion) {
  chatInput.value = suggestion;
  resizeComposer();
  nextTick(() => chatInputEl.value?.focus());
}

function clearChat() {
  chatMessages.value = [];
  sessionId.value = crypto.randomUUID();
  chatInput.value = "";
  nextTick(() => chatInputEl.value?.focus());
}

async function openContractReference(ref) {
  const correlationId = referenceCorrelationId(ref);
  if (!correlationId) return;
  const tab = window.open("", "_blank");
  try {
    const res = await fetch(
      `/api/get-contract-download-url?correlationId=${encodeURIComponent(
        correlationId,
      )}`,
      {
        headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
      },
    );
    if (!res.ok) throw new Error(`Download URL failed (${res.status})`);
    const data = await res.json();
    if (!data.url) throw new Error("Missing download URL");
    if (tab) tab.location.href = data.url;
    else window.location.href = data.url;
  } catch {
    if (tab) tab.close();
    chatMessages.value.push({
      id: crypto.randomUUID(),
      role: "assistant",
      content: "I could not open that contract link. Please try again.",
      error: true,
    });
  }
}

function referenceCorrelationId(ref) {
  return ref?.correlationId ?? ref?.CorrelationId ?? "";
}

function referenceLabel(ref) {
  return (
    ref?.fileName ??
    ref?.FileName ??
    ref?.documentType ??
    ref?.DocumentType ??
    "Contract"
  );
}

function scrollChatToBottom() {
  nextTick(() => {
    if (chatScroll.value)
      chatScroll.value.scrollTop = chatScroll.value.scrollHeight;
  });
}

function resizeComposer() {
  nextTick(() => {
    const input = chatInputEl.value;
    if (!input) return;
    input.style.height = "auto";
    input.style.height = `${Math.min(input.scrollHeight, 148)}px`;
  });
}

function dismissFailed(correlationId) {
  contracts.value = contracts.value.filter(
    (c) => c.correlationId !== correlationId,
  );
}

async function reviewContract(contract, action) {
  reviewPending.value = contract.correlationId;
  try {
    const relatedCorrelationId = selectedRelatedId(contract) || undefined;
    const res = await fetch("/api/review-contract", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Auth-Token": `Bearer ${auth.getToken()}`,
      },
      body: JSON.stringify({
        correlationId: contract.correlationId,
        action,
        relatedCorrelationId,
      }),
    });
    if (!res.ok) throw new Error(`Review failed (${res.status})`);
    const data = await res.json();
    const idx = contracts.value.findIndex(
      (c) => c.correlationId === contract.correlationId,
    );
    if (idx >= 0 && data.status === "deleted") {
      contracts.value.splice(idx, 1);
      if (selectedId.value === contract.correlationId) selectedId.value = null;
    } else if (idx >= 0) {
      contracts.value[idx] = {
        ...contracts.value[idx],
        status: data.status,
        reviewState: data.reviewState,
        relationshipType: data.relationshipType,
        duplicateOfCorrelationId: data.duplicateOfCorrelationId,
        supersedesCorrelationId: data.supersedesCorrelationId,
        relatedContractIds: data.relatedContractIds,
        reviewedAt: data.reviewedAt,
        reviewedBy: data.reviewedBy,
      };
    }
  } catch {
    chatMessages.value.push({
      id: crypto.randomUUID(),
      role: "assistant",
      content: "I could not update that contract review. Please try again.",
      error: true,
    });
  } finally {
    reviewPending.value = "";
  }
}

function onDrop(e) {
  isDragging.value = false;
  uploadFiles(Array.from(e.dataTransfer.files));
}

function onFileChange(e) {
  uploadFiles(Array.from(e.target.files));
  e.target.value = "";
}

async function uploadFiles(files) {
  for (const file of files) {
    if (!ALLOWED_TYPES.includes(file.type)) {
      uploadError.value = `${file.name} is not a supported file type. Use PDF or DOCX.`;
      uploadState.value = "error";
      return;
    }
    if (file.size > MAX_SIZE) {
      uploadError.value = `${file.name} exceeds the 20 MB limit.`;
      uploadState.value = "error";
      return;
    }
  }
  uploadState.value = "uploading";
  try {
    for (const file of files) {
      const form = new FormData();
      form.append("file", file);
      const res = await fetch("/api/upload-contract", {
        method: "POST",
        headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` },
        body: form,
      });
      if (!res.ok) {
        const text = await res.text().catch(() => "");
        let message = text.trim();
        if (!message) {
          const body = await res.json().catch(() => ({}));
          message = body.error || body.message || "";
        }
        throw new Error(message || `Upload failed (${res.status})`);
      }
      const data = await res.json();
      contracts.value.unshift({
        correlationId: data.correlationId,
        fileName: data.fileName,
        uploadedAt: new Date().toISOString(),
        status: "processing",
        statusMessage: "Uploading complete. Queued for extraction.",
        documentType: "",
      });
      startPolling(data.correlationId);
    }
    uploadState.value = "idle";
  } catch (err) {
    uploadError.value = err.message;
    uploadState.value = "error";
  }
}

function formatDate(iso) {
  if (!iso) return "";
  return new Date(iso).toLocaleDateString(undefined, {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function flatFields(fields) {
  return !fields || typeof fields !== "object" ? {} : fields;
}

function isPendingReview(contract) {
  return (
    contract.status === "pending_review" ||
    contract.reviewState === "pending_review"
  );
}

function relationshipCandidates(contract) {
  return Array.isArray(contract.relationshipCandidates)
    ? contract.relationshipCandidates
    : [];
}

function selectedRelatedId(contract) {
  return (
    selectedRelatedIds.value[contract.correlationId] ??
    relationshipCandidates(contract)[0]?.correlationId ??
    ""
  );
}

function setSelectedRelatedId(contract, correlationId) {
  selectedRelatedIds.value = {
    ...selectedRelatedIds.value,
    [contract.correlationId]: correlationId,
  };
}

function reviewSummary(contract) {
  const candidates = relationshipCandidates(contract);
  if (candidates.length === 0)
    return "No close match was found. Approve as a new contract or reject and delete the upload.";

  return "A possible related contract was found. Select it, then decide whether this upload is new, an extension, a replacement, or a duplicate.";
}

function candidateLabel(candidate) {
  const reasons = Array.isArray(candidate.reasons) ? candidate.reasons : [];
  const relationship = relationshipDisplay(candidate.relationshipType);
  return [relationship, reasons.slice(0, 2).join(" ")].filter(Boolean).join(" · ");
}

function relationshipLabel(contract) {
  if (isPendingReview(contract) && relationshipCandidates(contract).length)
    return "Potential match found";

  const type = relationshipDisplay(contract.relationshipType);
  if (!type || contract.relationshipType === "new") return "";
  return type;
}

function relationshipDisplay(type) {
  switch (type) {
    case "duplicate":
      return "Duplicate candidate";
    case "replacement":
      return "Replacement candidate";
    case "extension":
      return "Extension candidate";
    case "unknown":
      return "";
    case "new":
      return "New contract";
    default:
      return "";
  }
}

function lifecycleDate(contract) {
  const date =
    contract.noticeDeadline ??
    contract.expiryDate ??
    contract.assignmentEndDate;
  return date ? startOfDay(new Date(date)) : null;
}

function lifecycleLabel(contract) {
  if (isPendingReview(contract)) return "Pending review";
  if (contract.noticeDeadline) return `Notice by ${formatDate(contract.noticeDeadline)}`;
  if (contract.expiryDate) return `Expires ${formatDate(contract.expiryDate)}`;
  if (contract.assignmentEndDate) return `Assignment ends ${formatDate(contract.assignmentEndDate)}`;
  return "";
}

function paymentLabel(contract) {
  if (!contract.paymentAmount) return "";
  const amount = new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 2,
  }).format(contract.paymentAmount);
  const currency = contract.paymentCurrency || "";
  const unit = contract.paymentUnit && contract.paymentUnit !== "one_time"
    ? `/${contract.paymentUnit}`
    : "";
  return `${amount} ${currency}${unit}`.trim();
}

function startOfDay(date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}
</script>

<style scoped>
.contracts-page {
  --ink: #181511;
  --muted: #746f67;
  --line: #ddd7cd;
  --paper: #fbfaf7;
  --panel: #ffffff;
  --accent: #126b5f;
  --accent-soft: #e7f2ef;
  --danger: #991b1b;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding-bottom: 2rem;
  color: var(--ink);
}

.chat-shell {
  min-height: 72vh;
  display: grid;
  grid-template-rows: auto auto 1fr auto;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid var(--line);
  border-radius: 18px;
  box-shadow: 0 20px 55px rgba(31, 28, 22, 0.11);
  overflow: hidden;
}

.chat-topbar,
.chat-context,
.composer-wrap {
  background: rgba(251, 250, 247, 0.9);
}

.chat-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--line);
}

.identity,
.chat-context-inner,
.accordion-label,
.contract-row,
.ref-chip,
.icon-btn,
.send-btn {
  display: flex;
  align-items: center;
}

.identity {
  gap: 0.75rem;
  min-width: 0;
}

.mark {
  width: 2.15rem;
  height: 2.15rem;
  display: grid;
  place-items: center;
  border: 1px solid #b7d2cc;
  border-radius: 8px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

svg {
  width: 1rem;
  height: 1rem;
  stroke-width: 1.8;
}

.chat-heading {
  margin: 0;
  font-family: Georgia, "Times New Roman", serif;
  font-size: 1.25rem;
  font-weight: 600;
  line-height: 1.1;
  letter-spacing: 0;
}

.chat-subheading {
  display: block;
  margin-top: 0.18rem;
  color: var(--muted);
  font-size: 0.82rem;
}

.icon-btn {
  width: 2.25rem;
  height: 2.25rem;
  justify-content: center;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--panel);
  color: var(--muted);
  cursor: pointer;
}

.icon-btn:hover {
  color: var(--ink);
  border-color: #c9c0b3;
}

.chat-context {
  justify-content: space-between;
  gap: 0.8rem;
  padding: 0.45rem 1.25rem;
  border-bottom: 1px solid var(--line);
}

.chat-context-inner {
  gap: 0.45rem;
  min-width: 0;
  color: var(--accent);
  font-size: 0.82rem;
  font-weight: 600;
}

.chat-context-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chat-context-label {
  color: var(--muted);
  font-weight: 500;
}

.chat-context-dismiss {
  display: grid;
  place-items: center;
  width: 1.65rem;
  height: 1.65rem;
  border: 0;
  background: transparent;
  color: var(--muted);
  cursor: pointer;
}

.conversation {
  overflow: auto;
  padding: 2rem 1.5rem 1.5rem;
  scroll-behavior: smooth;
}

.thread {
  width: min(860px, 100%);
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.welcome {
  width: min(720px, 100%);
  margin: 0 auto 0.5rem;
  text-align: center;
}

.welcome h1 {
  margin: 0;
  font-family: Georgia, "Times New Roman", serif;
  font-size: clamp(2rem, 4vw, 3.35rem);
  line-height: 1.04;
  font-weight: 500;
  letter-spacing: 0;
}

.welcome p {
  margin: 0.85rem auto 0;
  max-width: 34rem;
  color: var(--muted);
  line-height: 1.55;
}

.chat-suggestions {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.65rem;
  margin-top: 1.35rem;
}

.chat-suggestions button {
  min-height: 3.6rem;
  padding: 0.75rem 0.85rem;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: #fff;
  color: var(--ink);
  font: inherit;
  font-size: 0.82rem;
  line-height: 1.35;
  text-align: left;
  cursor: pointer;
}

.chat-suggestions button:hover {
  border-color: #b8d2ca;
  background: #f6fbf9;
}

.message {
  display: grid;
  grid-template-columns: 2.15rem minmax(0, 1fr);
  gap: 0.8rem;
}

.message.user {
  grid-template-columns: minmax(0, 1fr) 2.15rem;
}

.avatar {
  width: 2.15rem;
  height: 2.15rem;
  display: grid;
  place-items: center;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: #f0ede6;
  color: var(--muted);
  font-size: 0.72rem;
  font-weight: 700;
}

.assistant .avatar {
  background: var(--accent-soft);
  border-color: #b7d2cc;
  color: var(--accent);
}

.bubble {
  max-width: 45rem;
  line-height: 1.62;
  color: #2a251f;
  white-space: pre-wrap;
}

.user .message-body {
  justify-self: end;
}

.user .bubble {
  padding: 0.75rem 0.95rem;
  border: 1px solid var(--line);
  border-radius: 12px;
  background: #f0ede6;
}

.bubble--error {
  padding: 0.75rem 0.95rem;
  border: 1px solid #fecaca;
  border-radius: 12px;
  background: #fee2e2;
  color: var(--danger);
}

.refs {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.ref-chip {
  gap: 0.42rem;
  max-width: 17.5rem;
  padding: 0.42rem 0.62rem;
  border: 1px solid #b7d2cc;
  border-radius: 7px;
  background: #f6fbf9;
  color: var(--accent);
  font: inherit;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
}

.ref-chip span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.model-tag {
  display: inline-block;
  margin-top: 0.5rem;
  color: #9a9388;
  font-size: 0.75rem;
}

.typing-dots {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  padding-top: 0.55rem;
}

.typing-dots span {
  width: 0.35rem;
  height: 0.35rem;
  border-radius: 999px;
  background: #9a9388;
  animation: dot-bounce 1.2s infinite ease-in-out;
}

.typing-dots span:nth-child(2) {
  animation-delay: 0.2s;
}

.typing-dots span:nth-child(3) {
  animation-delay: 0.4s;
}

@keyframes dot-bounce {
  0%,
  60%,
  100% {
    transform: translateY(0);
  }
  30% {
    transform: translateY(-0.32rem);
  }
}

.composer-wrap {
  padding: 1rem 1.25rem 1.1rem;
  border-top: 1px solid var(--line);
}

.composer {
  width: min(860px, 100%);
  margin: 0 auto;
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 0.65rem;
  align-items: end;
  padding: 0.62rem;
  border: 1px solid #cfc7bb;
  border-radius: 14px;
  background: #fff;
  box-shadow: 0 8px 24px rgba(31, 28, 22, 0.08);
}

textarea {
  min-height: 3rem;
  max-height: 9.25rem;
  resize: none;
  border: 0;
  outline: 0;
  padding: 0.72rem 0.62rem;
  color: var(--ink);
  font: inherit;
  font-size: 0.95rem;
  line-height: 1.45;
}

textarea::placeholder {
  color: #a19a90;
}

.send-btn {
  width: 2.65rem;
  height: 2.65rem;
  justify-content: center;
  border: 0;
  border-radius: 10px;
  background: var(--accent);
  color: #fff;
  cursor: pointer;
}

.send-btn:disabled {
  background: #c8c1b7;
  cursor: default;
}

.compose-hint {
  width: min(860px, 100%);
  margin: 0.5rem auto 0;
  color: #9a9388;
  font-size: 0.75rem;
}

.workspace {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.9rem;
}

.attention-strip {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: center;
  gap: 1rem;
  padding: 0.85rem 1rem;
  border: 1px solid #d7cdbf;
  border-radius: 12px;
  background: #fffaf0;
}

.attention-strip strong {
  display: block;
  margin-top: 0.1rem;
  font-size: 0.95rem;
}

.attention-kicker {
  color: #8a5f11;
  font-size: 0.72rem;
  font-weight: 800;
  text-transform: uppercase;
}

.attention-items {
  display: flex;
  gap: 0.5rem;
  overflow: auto;
}

.attention-items button {
  min-width: 11rem;
  max-width: 16rem;
  padding: 0.55rem 0.65rem;
  border: 1px solid #e8d6aa;
  border-radius: 8px;
  background: #fff;
  color: var(--ink);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.attention-items span,
.attention-items small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.attention-items span {
  font-size: 0.8rem;
  font-weight: 700;
}

.attention-items small {
  margin-top: 0.12rem;
  color: #8a5f11;
  font-size: 0.72rem;
}

.workspace-right {
  display: none;
}

.accordion {
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid var(--line);
  border-radius: 12px;
  overflow: hidden;
}

.accordion-trigger {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.8rem;
  padding: 1rem 1.05rem;
  border: 0;
  background: transparent;
  color: var(--ink);
  font: inherit;
  cursor: pointer;
}

.accordion-label {
  gap: 0.62rem;
  min-width: 0;
  font-weight: 700;
}

.accordion-label svg {
  color: var(--accent);
  flex-shrink: 0;
}

.badge-count {
  padding: 0.18rem 0.5rem;
  border-radius: 999px;
  background: #f0ede6;
  color: var(--muted);
  font-size: 0.74rem;
}

.chevron {
  width: 1rem;
  height: 1rem;
  color: #9a9388;
  transition: transform 0.15s ease;
}

.accordion.open .chevron {
  transform: rotate(180deg);
}

.accordion-panel {
  display: none;
  border-top: 1px solid var(--line);
}

.accordion.open .accordion-panel {
  display: block;
}

.accordion-panel-inner {
  padding: 0.85rem;
}

.panel-state {
  display: grid;
  min-height: 7rem;
  place-items: center;
  color: var(--muted);
}

.contract-list {
  display: grid;
  gap: 0.5rem;
  max-height: 22rem;
  overflow: auto;
}

.contract-item {
  display: grid;
  gap: 0;
}

.contract-row {
  gap: 0.7rem;
  padding: 0.62rem;
  border: 1px solid #ebe5dc;
  border-radius: 8px;
  background: var(--paper);
}

.contract-item--review .contract-row {
  border-color: #ead8aa;
  border-bottom-color: transparent;
  border-radius: 8px 8px 0 0;
  background: #fffdf6;
}

.contract-row--processing {
  background: linear-gradient(90deg, #fbfaf7, #f2faf7, #fbfaf7);
  background-size: 220% 100%;
  animation: row-progress 1.8s infinite linear;
}

.contract-row--clickable {
  cursor: pointer;
}

.contract-row--clickable:hover,
.contract-row--selected {
  border-color: #b7d2cc;
  background: #f6fbf9;
}

.contract-row-icon {
  width: 2rem;
  height: 2rem;
  display: grid;
  place-items: center;
  color: var(--accent);
  flex-shrink: 0;
}

.contract-row-icon svg {
  width: 1.15rem;
  height: 1.15rem;
}

.contract-row-body {
  min-width: 0;
  flex: 1;
}

.contract-row-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-weight: 700;
  font-size: 0.86rem;
}

.contract-row-meta {
  margin-top: 0.16rem;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--muted);
  font-size: 0.76rem;
}

.contract-row-payment {
  margin-top: 0.18rem;
  color: var(--accent);
  font-size: 0.74rem;
  font-weight: 700;
}

.contract-row-relation {
  margin-top: 0.16rem;
  color: #8a5f11;
  font-size: 0.72rem;
  font-weight: 700;
}

.contract-row-end {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  flex-shrink: 0;
}

.badge,
.btn-ghost-sm {
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 700;
}

.badge {
  padding: 0.2rem 0.48rem;
}

.badge--blue {
  background: #dbeafe;
  color: #1d4ed8;
}

.badge--amber {
  background: #fef3c7;
  color: #92400e;
}

.badge--red {
  background: #fee2e2;
  color: #991b1b;
}

.btn-ghost-sm {
  border: 1px solid var(--line);
  background: #fff;
  color: var(--muted);
  padding: 0.25rem 0.55rem;
  cursor: pointer;
}

.btn-ghost-sm--danger {
  border-color: #fecaca;
  color: var(--danger);
}

.btn-ghost-sm:disabled {
  opacity: 0.55;
  cursor: default;
}

.spin-icon {
  animation: spin 0.9s linear infinite;
}

.review-panel {
  padding: 0.8rem 0.85rem 0.85rem;
  border: 1px solid #ead8aa;
  border-top: 0;
  border-radius: 0 0 8px 8px;
  background: #fffaf0;
}

.review-panel-head {
  display: flex;
  justify-content: space-between;
  gap: 0.8rem;
  align-items: flex-start;
}

.review-panel-head strong {
  display: block;
  font-size: 0.84rem;
}

.review-panel-head p {
  margin: 0.15rem 0 0;
  color: #746f67;
  font-size: 0.76rem;
  line-height: 1.35;
}

.review-panel-head a {
  display: none;
}

.review-panel-state,
.candidate-kicker {
  color: #8a5f11;
  font-size: 0.68rem;
  font-weight: 900;
  text-transform: uppercase;
}

.review-panel-state {
  flex-shrink: 0;
  padding-top: 0.08rem;
}

.candidate-list {
  display: grid;
  gap: 0.4rem;
  margin-top: 0.65rem;
}

.candidate-kicker {
  margin-bottom: 0.05rem;
}

.candidate {
  padding: 0.52rem 0.6rem;
  border: 1px solid #ead8aa;
  border-radius: 8px;
  background: #fff;
  color: var(--ink);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.candidate--selected {
  border-color: var(--accent);
  background: #f6fbf9;
}

.candidate span,
.candidate small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.candidate span {
  font-size: 0.78rem;
  font-weight: 800;
}

.candidate small {
  margin-top: 0.12rem;
  color: #746f67;
  font-size: 0.7rem;
}

.review-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-top: 0.65rem;
}

.review-action-hint {
  margin: 0.55rem 0 0;
  color: #746f67;
  font-size: 0.72rem;
  line-height: 1.35;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

@keyframes row-progress {
  to {
    background-position: -220% 0;
  }
}

.dropzone {
  min-height: 9rem;
  display: grid;
  place-items: center;
  border: 1px dashed #c5baad;
  border-radius: 10px;
  background: var(--paper);
  color: var(--muted);
  text-align: center;
  cursor: pointer;
}

.dropzone--over {
  border-color: var(--accent);
  background: var(--accent-soft);
}

.dropzone svg {
  width: 1.5rem;
  height: 1.5rem;
  color: var(--accent);
}

.dropzone-title {
  margin: 0.45rem 0 0;
  color: var(--ink);
  font-weight: 700;
}

.dropzone-hint {
  margin: 0.15rem 0 0;
  font-size: 0.82rem;
}

.dropzone-tags {
  display: flex;
  justify-content: center;
  gap: 0.35rem;
  margin-top: 0.55rem;
}

.dropzone-tags span {
  padding: 0.18rem 0.45rem;
  border-radius: 999px;
  background: #f0ede6;
  color: var(--muted);
  font-size: 0.72rem;
  font-weight: 700;
}

.upload-status,
.upload-err {
  margin-top: 0.7rem;
}

.upload-status {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  color: var(--muted);
  font-size: 0.82rem;
}

.upload-bar {
  flex: 1;
  height: 0.35rem;
  overflow: hidden;
  border-radius: 999px;
  background: #f0ede6;
}

.upload-fill {
  width: 45%;
  height: 100%;
  border-radius: inherit;
  background: var(--accent);
  animation: upload-pulse 1.2s infinite ease-in-out;
}

@keyframes upload-pulse {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(230%);
  }
}

.upload-err {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.8rem;
  color: var(--danger);
  font-size: 0.82rem;
}

@media (max-width: 780px) {
  .chat-shell {
    min-height: 68vh;
    border-radius: 13px;
  }

  .chat-subheading,
  .compose-hint {
    display: none;
  }

  .conversation {
    padding: 1.5rem 0.9rem 1.1rem;
  }

  .chat-suggestions,
  .workspace,
  .attention-strip {
    grid-template-columns: 1fr;
  }

  .attention-items {
    flex-direction: column;
  }

  .attention-items button {
    max-width: none;
    width: 100%;
  }

  .message,
  .message.user {
    grid-template-columns: 2rem minmax(0, 1fr);
  }

  .message.user .avatar {
    grid-column: 1;
    grid-row: 1;
  }

  .message.user .message-body {
    grid-column: 2;
    justify-self: start;
  }
}
</style>
