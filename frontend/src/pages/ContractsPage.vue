<template>
  <section class="module contracts-module">
    <div class="module-header">
      <h1>Contracts</h1>
      <p class="module-subtitle">
        Upload, extract, and query your contracts with AI assistance.
      </p>
    </div>

    <!-- Upload area — admin only -->
    <div v-if="auth.hasRole('admin')">
      <div
        class="dropzone-full"
        :class="{ 'drag-active': isDragging }"
        @click="fileInput?.click()"
        @dragover.prevent="isDragging = true"
        @dragleave.prevent="isDragging = false"
        @drop.prevent="onDrop"
      >
        <svg
          class="dropzone-icon"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
        >
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
          <polyline points="17 8 12 3 7 8" />
          <line x1="12" y1="3" x2="12" y2="15" />
        </svg>
        <div class="dropzone-heading">Drop your contract here</div>
        <p class="dropzone-subtext">or click to browse your files</p>
        <div class="dropzone-types">
          <span class="dropzone-badge">PDF</span>
          <span class="dropzone-badge">DOCX</span>
          <span class="dropzone-badge">Max 20 MB</span>
        </div>
      </div>

      <div v-if="uploadState === 'uploading'" class="upload-progress">
        <div class="upload-progress-bar">
          <div class="upload-progress-fill" />
        </div>
        <span class="upload-progress-label">Uploading…</span>
      </div>

      <div v-if="uploadState === 'error'" class="upload-error">
        <span class="upload-error-text">{{ uploadError }}</span>
        <button class="btn btn-ghost btn-sm" @click="uploadState = 'idle'">
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

    <!-- Contract list -->
    <div v-if="contracts.length > 0" class="contracts-grid">
      <div
        v-for="contract in contracts"
        :key="contract.correlationId"
        class="contract-card"
        :class="{
          'contract-card--clickable': isClickable(contract),
          'contract-card--selected': selectedId === contract.correlationId,
        }"
        @click="onCardClick(contract)"
      >
        <div class="contract-card-icon">
          <svg
            v-if="contract.status === 'processing'"
            class="spin"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path
              d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"
            />
          </svg>
          <svg
            v-else-if="contract.status === 'failed'"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
          <svg
            v-else
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path
              d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
            />
            <polyline points="14 2 14 8 20 8" />
          </svg>
        </div>

        <div class="contract-card-body">
          <div class="contract-card-name">
            {{ contract.fileName || "Contract" }}
          </div>
          <div class="contract-card-meta">
            <template v-if="contract.status === 'processing'"
              >Extracting fields…</template
            >
            <template v-else-if="contract.status === 'failed'"
              >Extraction failed</template
            >
            <template v-else
              >{{ contract.documentType || "Document" }} ·
              {{ formatDate(contract.uploadedAt) }}</template
            >
          </div>
        </div>

        <div class="contract-card-actions">
          <span
            v-if="contract.status === 'processing'"
            class="badge badge-processing"
            >Processing</span
          >
          <span
            v-else-if="contract.status === 'pending_review'"
            class="badge badge-warning"
            >Pending review</span
          >
          <span
            v-else-if="contract.status === 'failed'"
            class="badge badge-error"
            >Failed</span
          >
          <button
            v-if="contract.status === 'failed'"
            class="btn btn-ghost btn-sm"
            @click.stop="dismissFailed(contract.correlationId)"
          >
            Dismiss
          </button>
        </div>
      </div>
    </div>

    <!-- Empty state -->
    <div v-else-if="!listLoading" class="contracts-empty">
      <svg
        class="contracts-empty-icon"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
      >
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
      </svg>
      <p class="contracts-empty-title">No contracts yet</p>
      <p class="contracts-empty-text">
        {{
          auth.hasRole("admin")
            ? "Upload a contract above to get started."
            : "No contracts have been uploaded yet."
        }}
      </p>
    </div>

    <!-- Detail panel -->
    <div
      v-if="selectedContract"
      class="contract-detail-overlay"
      @click.self="selectedId = null"
    >
      <aside class="contract-detail-panel">
        <div class="contract-detail-header">
          <div>
            <h2 class="contract-detail-title">
              {{ selectedContract.documentType || selectedContract.fileName }}
            </h2>
            <p class="contract-detail-sub">
              {{ selectedContract.fileName }} ·
              {{ formatDate(selectedContract.uploadedAt) }}
            </p>
          </div>
          <button
            class="btn btn-ghost btn-icon"
            @click="selectedId = null"
            aria-label="Close"
          >
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <line x1="18" y1="6" x2="6" y2="18" />
              <line x1="6" y1="6" x2="18" y2="18" />
            </svg>
          </button>
        </div>

        <div v-if="detailLoading" class="contract-detail-loading">Loading…</div>

        <div v-else-if="detailError" class="upload-error">
          <span class="upload-error-text">{{ detailError }}</span>
        </div>

        <div v-else-if="detailData" class="contract-detail-fields">
          <div
            v-if="detailData.status === 'pending_review'"
            class="contract-detail-notice"
          >
            Flagged for human review — confidence below threshold.
          </div>

          <table class="fields-table">
            <tbody>
              <template
                v-for="(value, key) in flatFields(detailData.fields)"
                :key="key"
              >
                <tr>
                  <th>{{ formatKey(key) }}</th>
                  <td>
                    <!-- Array of objects → cards (e.g. parties) -->
                    <template
                      v-if="
                        Array.isArray(value) &&
                        value.length &&
                        isObject(value[0])
                      "
                    >
                      <div class="object-cards">
                        <div
                          v-for="(item, i) in value"
                          :key="i"
                          class="object-card"
                        >
                          <div v-if="item.name" class="object-card-name">
                            {{ item.name }}
                          </div>
                          <dl class="object-card-props">
                            <template v-for="(v, k) in item" :key="k">
                              <div v-if="k !== 'name'" class="object-card-prop">
                                <dt>{{ formatKey(k) }}</dt>
                                <dd>{{ v ?? "—" }}</dd>
                              </div>
                            </template>
                          </dl>
                        </div>
                      </div>
                    </template>
                    <!-- Array of primitives → plain list -->
                    <template v-else-if="Array.isArray(value) && value.length">
                      <ul class="field-list">
                        <li v-for="(item, i) in value" :key="i">{{ item }}</li>
                      </ul>
                    </template>
                    <!-- Empty array -->
                    <template v-else-if="Array.isArray(value)">
                      <span class="field-not-found">None</span>
                    </template>
                    <!-- Null / empty -->
                    <template
                      v-else-if="
                        value === null || value === undefined || value === ''
                      "
                    >
                      <span class="field-not-found">Not found</span>
                    </template>
                    <!-- Boolean -->
                    <template v-else-if="typeof value === 'boolean'">
                      <span :class="value ? 'field-bool-yes' : 'field-bool-no'">
                        {{ value ? "Yes" : "No" }}
                      </span>
                    </template>
                    <!-- Scalar -->
                    <template v-else>{{ value }}</template>
                  </td>
                </tr>
              </template>
              <tr
                v-if="
                  !detailData.fields ||
                  Object.keys(detailData.fields).length === 0
                "
              >
                <td colspan="2" class="field-not-found">
                  No fields extracted.
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Chat section -->
        <div v-if="detailData && !detailLoading" class="contract-chat">
          <div class="contract-chat-header">
            <span class="contract-chat-title">Ask a question</span>
            <button
              v-if="chatMessages.length"
              class="btn btn-ghost btn-sm"
              @click="clearChat"
            >
              New conversation
            </button>
          </div>

          <div ref="chatScroll" class="contract-chat-messages">
            <p v-if="!chatMessages.length && !chatPending" class="chat-empty">
              Ask anything about this contract — parties, notice periods,
              obligations…
            </p>

            <div
              v-for="msg in chatMessages"
              :key="msg.id"
              :class="[
                'chat-message',
                msg.role === 'user'
                  ? 'chat-message--user'
                  : 'chat-message--assistant',
              ]"
            >
              <div :class="['chat-bubble', msg.error && 'chat-bubble--error']">
                {{ msg.content }}
              </div>
              <div
                v-if="msg.role === 'assistant' && !msg.error"
                class="chat-badges"
              >
                <span
                  v-if="msg.sources?.includes('original_document')"
                  class="chat-badge"
                  >From contract document</span
                >
                <span
                  v-else-if="msg.sources?.includes('extracted_fields')"
                  class="chat-badge"
                  >From extracted fields</span
                >
                <span
                  v-if="msg.model?.includes('opus')"
                  class="chat-badge chat-badge--opus"
                  >Deep analysis</span
                >
              </div>
            </div>

            <div
              v-if="chatPending"
              class="chat-message chat-message--assistant"
            >
              <div class="chat-bubble chat-typing">
                <span /><span /><span />
              </div>
            </div>
          </div>

          <form class="contract-chat-input" @submit.prevent="sendMessage">
            <input
              v-model="chatInput"
              type="text"
              placeholder="Ask about this contract…"
              :disabled="chatPending"
              class="chat-input"
              @keydown.enter.prevent="sendMessage"
            />
            <button
              type="submit"
              :disabled="!chatInput.trim() || chatPending"
              class="btn btn-primary btn-sm"
            >
              Send
            </button>
          </form>
        </div>
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
const uploadState = ref("idle"); // 'idle' | 'uploading' | 'error'
const uploadError = ref("");
const contracts = ref([]);
const listLoading = ref(true);
const selectedId = ref(null);
const detailData = ref(null);
const detailLoading = ref(false);
const detailError = ref("");

const polls = new Map(); // correlationId → intervalId

// Chat state
const sessionId = ref(crypto.randomUUID());
const chatMessages = ref([]); // { id, role, content, sources?, model?, error? }
const chatInput = ref("");
const chatPending = ref(false);
const chatScroll = ref(null);

const selectedContract = computed(
  () =>
    contracts.value.find((c) => c.correlationId === selectedId.value) ?? null,
);

const MAX_SIZE = 20 * 1024 * 1024;
const ALLOWED_TYPES = [
  "application/pdf",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
];

onMounted(loadContracts);
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
    /* silent — empty list shown */
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
    const { status } = await res.json();
    if (status === "processing") return;

    clearInterval(polls.get(correlationId));
    polls.delete(correlationId);

    const idx = contracts.value.findIndex(
      (c) => c.correlationId === correlationId,
    );
    if (idx !== -1) {
      if (status === "completed" || status === "pending_review") {
        const detail = await fetchDetail(correlationId);
        if (detail) {
          contracts.value[idx] = { ...contracts.value[idx], ...detail, status };
        } else {
          contracts.value[idx] = { ...contracts.value[idx], status };
        }
      } else {
        contracts.value[idx] = { ...contracts.value[idx], status };
      }
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
  if (!isClickable(contract)) return;
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
  // Reset chat for each new contract
  sessionId.value = crypto.randomUUID();
  chatMessages.value = [];
  chatInput.value = "";
  chatPending.value = false;
});

async function sendMessage() {
  const msg = chatInput.value.trim();
  if (!msg || chatPending.value) return;
  chatInput.value = "";
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
        correlationId: selectedId.value,
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
      sources: data.sources,
      model: data.modelUsed,
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
  }
}

function clearChat() {
  chatMessages.value = [];
  sessionId.value = crypto.randomUUID();
}

function scrollChatToBottom() {
  nextTick(() => {
    if (chatScroll.value)
      chatScroll.value.scrollTop = chatScroll.value.scrollHeight;
  });
}

function dismissFailed(correlationId) {
  contracts.value = contracts.value.filter(
    (c) => c.correlationId !== correlationId,
  );
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
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || `Upload failed (${res.status})`);
      }
      const data = await res.json();
      contracts.value.unshift({
        correlationId: data.correlationId,
        fileName: data.fileName,
        uploadedAt: new Date().toISOString(),
        status: "processing",
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

function formatKey(key) {
  return key
    .replace(/([A-Z])/g, " $1")
    .replace(/^./, (s) => s.toUpperCase())
    .trim();
}

function flatFields(fields) {
  if (!fields || typeof fields !== "object") return {};
  return fields;
}

function isObject(val) {
  return val !== null && typeof val === "object" && !Array.isArray(val);
}
</script>

<style scoped>
.contract-card--clickable {
  cursor: pointer;
}
.contract-card--clickable:hover {
  border-color: var(--color-accent, #6366f1);
}
.contract-card--selected {
  border-color: var(--color-accent, #6366f1);
}
.contract-card-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
.spin {
  animation: spin 1.2s linear infinite;
}

.badge-warning {
  background: #fef3c7;
  color: #92400e;
}
.badge-error {
  background: #fee2e2;
  color: #991b1b;
}

/* Detail panel */
.contract-detail-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 100;
  display: flex;
  justify-content: flex-end;
}
.contract-detail-panel {
  width: min(480px, 100vw);
  height: 100%;
  background: var(--color-surface, #fff);
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.15);
}
.contract-detail-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  padding: 1.5rem;
  border-bottom: 1px solid var(--color-border, #e5e7eb);
}
.contract-detail-title {
  font-size: 1.1rem;
  font-weight: 600;
  margin: 0 0 0.25rem;
}
.contract-detail-sub {
  font-size: 0.8rem;
  color: var(--color-muted, #6b7280);
  margin: 0;
}
.contract-detail-loading {
  padding: 2rem;
  text-align: center;
  color: var(--color-muted, #6b7280);
}
.contract-detail-fields {
  flex: 1;
  overflow-y: auto;
  padding: 1.25rem 1.5rem;
}
.contract-detail-notice {
  background: #fef3c7;
  color: #92400e;
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
  font-size: 0.85rem;
}
.contract-detail-footer {
  padding: 1rem 1.5rem;
  border-top: 1px solid var(--color-border, #e5e7eb);
}

.fields-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}
.fields-table th {
  text-align: left;
  font-weight: 500;
  color: var(--color-muted, #6b7280);
  padding: 0.5rem 0;
  width: 40%;
  vertical-align: top;
  border-bottom: 1px solid var(--color-border, #e5e7eb);
}
.fields-table td {
  padding: 0.5rem 0 0.5rem 0.75rem;
  vertical-align: top;
  border-bottom: 1px solid var(--color-border, #e5e7eb);
}
.field-not-found {
  color: var(--color-muted, #9ca3af);
  font-style: italic;
}
.field-bool-yes {
  color: #15803d;
  font-weight: 500;
}
.field-bool-no {
  color: var(--color-muted, #6b7280);
}
.field-list {
  margin: 0;
  padding-left: 1.25rem;
}
.field-list li {
  margin-bottom: 0.25rem;
}
.object-cards {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding: 0.25rem 0;
}
.object-card {
  background: var(--color-bg, #f9fafb);
  border: 1px solid var(--color-border, #e5e7eb);
  border-radius: 8px;
  padding: 0.625rem 0.75rem;
}
.object-card-name {
  font-weight: 600;
  font-size: 0.875rem;
  margin-bottom: 0.35rem;
  color: var(--color-text, #111827);
}
.object-card-props {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem 1rem;
  margin: 0;
}
.object-card-prop {
  display: flex;
  align-items: baseline;
  gap: 0.3rem;
  font-size: 0.8rem;
}
.object-card-prop dt {
  color: var(--color-muted, #6b7280);
  font-weight: 500;
  white-space: nowrap;
}
.object-card-prop dt::after {
  content: ":";
}
.object-card-prop dd {
  margin: 0;
  color: var(--color-text, #374151);
}

.btn-icon {
  width: 2rem;
  height: 2rem;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}
.btn-icon svg {
  width: 1.1rem;
  height: 1.1rem;
}

/* ── Chat ─────────────────────────────────────────────────── */
.contract-chat {
  display: flex;
  flex-direction: column;
  border-top: 1px solid var(--color-border, #e5e7eb);
  flex: 1;
  min-height: 0;
}
.contract-chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1.5rem;
  border-bottom: 1px solid var(--color-border, #e5e7eb);
}
.contract-chat-title {
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--color-text, #111827);
}
.contract-chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 1rem 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  min-height: 160px;
  max-height: 340px;
}
.chat-empty {
  color: var(--color-muted, #9ca3af);
  font-size: 0.85rem;
  text-align: center;
  margin: auto;
}
.chat-message {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  max-width: 85%;
}
.chat-message--user {
  align-self: flex-end;
  align-items: flex-end;
}
.chat-message--assistant {
  align-self: flex-start;
  align-items: flex-start;
}
.chat-bubble {
  padding: 0.55rem 0.85rem;
  border-radius: 12px;
  font-size: 0.875rem;
  line-height: 1.5;
  white-space: pre-wrap;
}
.chat-message--user .chat-bubble {
  background: var(--color-accent, #6366f1);
  color: #fff;
  border-bottom-right-radius: 3px;
}
.chat-message--assistant .chat-bubble {
  background: var(--color-bg, #f3f4f6);
  color: var(--color-text, #111827);
  border-bottom-left-radius: 3px;
}
.chat-bubble--error {
  background: #fee2e2 !important;
  color: #991b1b !important;
}
.chat-badges {
  display: flex;
  gap: 0.35rem;
  flex-wrap: wrap;
}
.chat-badge {
  font-size: 0.7rem;
  padding: 0.15rem 0.45rem;
  border-radius: 99px;
  background: var(--color-border, #e5e7eb);
  color: var(--color-muted, #6b7280);
}
.chat-badge--opus {
  background: #ede9fe;
  color: #5b21b6;
}

/* Typing indicator */
.chat-typing {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 0.65rem 1rem;
}
.chat-typing span {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--color-muted, #9ca3af);
  animation: typing-bounce 1.2s infinite ease-in-out;
}
.chat-typing span:nth-child(2) {
  animation-delay: 0.2s;
}
.chat-typing span:nth-child(3) {
  animation-delay: 0.4s;
}
@keyframes typing-bounce {
  0%,
  60%,
  100% {
    transform: translateY(0);
  }
  30% {
    transform: translateY(-5px);
  }
}

.contract-chat-input {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem 1.5rem 1rem;
  border-top: 1px solid var(--color-border, #e5e7eb);
}
.chat-input {
  flex: 1;
  padding: 0.45rem 0.75rem;
  border: 1px solid var(--color-border, #d1d5db);
  border-radius: 8px;
  font-size: 0.875rem;
  outline: none;
  background: var(--color-surface, #fff);
  color: var(--color-text, #111827);
}
.chat-input:focus {
  border-color: var(--color-accent, #6366f1);
}
.chat-input:disabled {
  opacity: 0.6;
}
</style>
