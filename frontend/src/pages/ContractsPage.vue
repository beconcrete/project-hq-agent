<template>
  <section class="contracts-page">
    <!-- ── Chat Zone (full width, center stage) ─────────────────────────── -->
    <div class="chat-zone">
      <div class="chat-header">
        <div class="chat-header-text">
          <h2 class="chat-heading">Contract Assistant</h2>
          <span class="chat-subheading"
            >Ask questions across all your contracts</span
          >
        </div>
        <button
          v-if="chatMessages.length"
          class="btn-new-chat"
          @click="clearChat"
        >
          New conversation
        </button>
      </div>

      <div v-if="selectedContract" class="chat-context">
        <div class="chat-context-inner">
          <svg
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
          <span class="chat-context-name">{{
            selectedContract.documentType || selectedContract.fileName
          }}</span>
          <span class="chat-context-label">selected</span>
        </div>
        <button
          class="chat-context-dismiss"
          @click="selectedId = null"
          aria-label="Clear context"
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

      <div ref="chatScroll" class="chat-messages">
        <div v-if="!chatMessages.length && !chatPending" class="chat-empty">
          <div class="chat-suggestions">
            <span class="suggestion-pill"
              >Which contracts expire next month?</span
            >
            <span class="suggestion-pill">Summarize the NDA with Acme</span>
            <span class="suggestion-pill">What are the payment terms?</span>
          </div>
        </div>

        <div
          v-for="msg in chatMessages"
          :key="msg.id"
          :class="[
            'chat-msg',
            msg.role === 'user' ? 'chat-msg--user' : 'chat-msg--ai',
          ]"
        >
          <div :class="['chat-bubble', msg.error && 'chat-bubble--error']">
            {{ msg.content }}
          </div>
          <span
            v-if="msg.role === 'assistant' && !msg.error && msg.model"
            class="msg-model"
            >{{ msg.model }}</span
          >
        </div>

        <div v-if="chatPending" class="chat-msg chat-msg--ai">
          <div class="chat-bubble typing-dots"><span /><span /><span /></div>
        </div>
      </div>

      <form class="chat-compose" @submit.prevent="sendMessage">
        <input
          v-model="chatInput"
          type="text"
          :placeholder="
            selectedContract
              ? 'Ask about this contract or all contracts…'
              : 'Ask about your contracts…'
          "
          :disabled="chatPending"
          class="compose-input"
          @keydown.enter.prevent="sendMessage"
        />
        <button
          type="submit"
          :disabled="!chatInput.trim() || chatPending"
          class="compose-send"
        >
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.5"
          >
            <line x1="22" y1="2" x2="11" y2="13" />
            <polygon points="22 2 15 22 11 13 2 9 22 2" />
          </svg>
        </button>
      </form>
    </div>

    <!-- ── Workspace: Accordions left + Detail right ─────────────────────── -->
    <div class="workspace">
      <!-- Left column: accordions -->
      <div class="workspace-left">
        <!-- Existing Contracts accordion -->
        <div class="accordion" :class="{ open: contractsOpen }">
          <button
            class="accordion-trigger"
            @click="contractsOpen = !contractsOpen"
          >
            <span class="accordion-label">
              <svg
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
              Existing Contracts
              <span v-if="contracts.length" class="badge-count">{{
                contracts.length
              }}</span>
            </span>
            <svg
              class="chevron"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <polyline points="6 9 12 15 18 9" />
            </svg>
          </button>
          <div class="accordion-panel">
            <div class="accordion-panel-inner">
              <div v-if="listLoading" class="panel-state">Loading…</div>
              <div
                v-else-if="contracts.length === 0"
                class="panel-state panel-state--empty"
              >
                <svg
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
                No contracts yet
              </div>
              <div v-else class="contract-list">
                <div
                  v-for="contract in contracts"
                  :key="contract.correlationId"
                  class="contract-row"
                  :class="{
                    'contract-row--clickable': isClickable(contract),
                    'contract-row--selected':
                      selectedId === contract.correlationId,
                  }"
                  @click="onCardClick(contract)"
                >
                  <div class="contract-row-icon">
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
                  <div class="contract-row-body">
                    <div class="contract-row-name">
                      {{ contract.fileName || "Contract" }}
                    </div>
                    <div class="contract-row-meta">
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
                      @click.stop="dismissFailed(contract.correlationId)"
                    >
                      Dismiss
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Upload New accordion (admin only) -->
        <div
          v-if="auth.hasRole('admin')"
          class="accordion"
          :class="{ open: uploadOpen }"
        >
          <button class="accordion-trigger" @click="uploadOpen = !uploadOpen">
            <span class="accordion-label">
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.5"
              >
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                <polyline points="17 8 12 3 7 8" />
                <line x1="12" y1="3" x2="12" y2="15" />
              </svg>
              Upload New
            </span>
            <svg
              class="chevron"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
            >
              <polyline points="6 9 12 15 18 9" />
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
                <svg
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.5"
                >
                  <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                  <polyline points="17 8 12 3 7 8" />
                  <line x1="12" y1="3" x2="12" y2="15" />
                </svg>
                <p class="dropzone-title">Drop contract here</p>
                <p class="dropzone-hint">or click to browse</p>
                <div class="dropzone-tags">
                  <span>PDF</span><span>DOCX</span><span>Max 20 MB</span>
                </div>
              </div>
              <div v-if="uploadState === 'uploading'" class="upload-status">
                <div class="upload-bar"><div class="upload-fill" /></div>
                <span>Uploading…</span>
              </div>
              <div v-if="uploadState === 'error'" class="upload-err">
                <span>{{ uploadError }}</span>
                <button class="btn-ghost-sm" @click="uploadState = 'idle'">
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
      </div>

      <!-- Right column: detail panel -->
      <aside class="workspace-right">
        <div v-if="!selectedId" class="detail-empty">
          <svg
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
          >
            <path
              d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"
            />
            <polyline points="14 2 14 8 20 8" />
            <line x1="16" y1="13" x2="8" y2="13" />
            <line x1="16" y1="17" x2="8" y2="17" />
            <line x1="10" y1="9" x2="8" y2="9" />
          </svg>
          <p>Select a contract to view its extracted fields</p>
        </div>
        <template v-else>
          <div class="detail-top">
            <div class="detail-top-text">
              <h3 class="detail-doc-type">
                {{ detailData?.documentType || detailData?.fileName || "—" }}
              </h3>
              <p class="detail-doc-meta">
                {{ detailData?.fileName }} ·
                {{ formatDate(detailData?.uploadedAt) }}
              </p>
            </div>
            <button
              class="btn-icon"
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
          <div class="detail-scroll">
            <div v-if="detailLoading" class="detail-loading">Loading…</div>
            <div v-else-if="detailError" class="detail-error-msg">
              {{ detailError }}
            </div>
            <div v-else-if="detailData">
              <div
                v-if="detailData.status === 'pending_review'"
                class="detail-notice"
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
                        <template
                          v-if="
                            Array.isArray(value) &&
                            value.length &&
                            isObject(value[0])
                          "
                        >
                          <div class="obj-cards">
                            <div
                              v-for="(item, i) in value"
                              :key="i"
                              class="obj-card"
                            >
                              <div v-if="item.name" class="obj-card-name">
                                {{ item.name }}
                              </div>
                              <dl class="obj-card-dl">
                                <template v-for="(v, k) in item" :key="k">
                                  <div v-if="k !== 'name'" class="obj-card-row">
                                    <dt>{{ formatKey(k) }}</dt>
                                    <dd>{{ v ?? "—" }}</dd>
                                  </div>
                                </template>
                              </dl>
                            </div>
                          </div>
                        </template>
                        <template
                          v-else-if="Array.isArray(value) && value.length"
                        >
                          <ul class="field-list">
                            <li v-for="(item, i) in value" :key="i">
                              {{ item }}
                            </li>
                          </ul>
                        </template>
                        <template v-else-if="Array.isArray(value)"
                          ><span class="val-nil">None</span></template
                        >
                        <template
                          v-else-if="
                            value === null ||
                            value === undefined ||
                            value === ''
                          "
                          ><span class="val-nil">Not found</span></template
                        >
                        <template v-else-if="typeof value === 'boolean'">
                          <span :class="value ? 'val-yes' : 'val-no'">{{
                            value ? "Yes" : "No"
                          }}</span>
                        </template>
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
                    <td colspan="2" class="val-nil">No fields extracted.</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </template>
      </aside>
    </div>
  </section>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, watch, nextTick } from "vue";
import { useAuth } from "../composables/useAuth";

const auth = useAuth();

// ── Upload ────────────────────────────────────────────────────────────────────
const fileInput = ref(null);
const isDragging = ref(false);
const uploadState = ref("idle");
const uploadError = ref("");

// ── Contracts ─────────────────────────────────────────────────────────────────
const contracts = ref([]);
const listLoading = ref(true);
const selectedId = ref(null);
const detailData = ref(null);
const detailLoading = ref(false);
const detailError = ref("");
const polls = new Map();

// ── Accordion state ────────────────────────────────────────────────────────────
const contractsOpen = ref(true);
const uploadOpen = ref(true);

// ── Chat ──────────────────────────────────────────────────────────────────────
const sessionId = ref(crypto.randomUUID());
const chatMessages = ref([]);
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
  // Chat session intentionally persists — user may continue cross-contract questions
});

// ── Chat ──────────────────────────────────────────────────────────────────────
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

// ── File upload ───────────────────────────────────────────────────────────────
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

// ── Formatters ────────────────────────────────────────────────────────────────
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
  return !fields || typeof fields !== "object" ? {} : fields;
}

function isObject(val) {
  return val !== null && typeof val === "object" && !Array.isArray(val);
}
</script>

<style scoped>
/* ── Page ─────────────────────────────────────────────────────────────────── */
.contracts-page {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
  padding-bottom: 2rem;
}

/* ── Chat Zone ────────────────────────────────────────────────────────────── */
.chat-zone {
  display: flex;
  flex-direction: column;
  height: 420px;
  background: linear-gradient(150deg, #f8f9ff 0%, #f2f4ff 100%);
  border: 1px solid rgba(99, 102, 241, 0.18);
  border-radius: 12px;
  overflow: hidden;
  box-shadow:
    0 2px 12px rgba(79, 70, 229, 0.07),
    0 1px 3px rgba(0, 0, 0, 0.04);
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.875rem 1.25rem;
  border-bottom: 1px solid rgba(99, 102, 241, 0.1);
  background: rgba(255, 255, 255, 0.75);
  backdrop-filter: blur(8px);
  flex-shrink: 0;
}
.chat-header-text {
  display: flex;
  align-items: baseline;
  gap: 0.625rem;
}
.chat-heading {
  font-size: 0.9375rem;
  font-weight: 600;
  color: #1e1b4b;
  margin: 0;
  letter-spacing: -0.01em;
}
.chat-subheading {
  font-size: 0.775rem;
  color: #9ca3af;
}

.btn-new-chat {
  font-size: 0.775rem;
  font-weight: 500;
  color: #6366f1;
  background: none;
  border: 1px solid rgba(99, 102, 241, 0.28);
  border-radius: 6px;
  padding: 0.3rem 0.7rem;
  cursor: pointer;
  transition:
    background 0.15s,
    border-color 0.15s;
  white-space: nowrap;
}
.btn-new-chat:hover {
  background: rgba(99, 102, 241, 0.07);
  border-color: rgba(99, 102, 241, 0.45);
}

.chat-context {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.4rem 1.25rem;
  background: rgba(99, 102, 241, 0.06);
  border-bottom: 1px solid rgba(99, 102, 241, 0.1);
  flex-shrink: 0;
}
.chat-context-inner {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.8rem;
}
.chat-context-inner svg {
  width: 0.8rem;
  height: 0.8rem;
  color: #6366f1;
  flex-shrink: 0;
}
.chat-context-name {
  font-weight: 500;
  color: #4338ca;
  max-width: 280px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.chat-context-label {
  color: #9ca3af;
}
.chat-context-dismiss {
  background: none;
  border: none;
  cursor: pointer;
  color: #9ca3af;
  padding: 0.15rem;
  display: flex;
  align-items: center;
  transition: color 0.15s;
}
.chat-context-dismiss:hover {
  color: #374151;
}
.chat-context-dismiss svg {
  width: 0.8rem;
  height: 0.8rem;
}

.chat-messages {
  flex: 1;
  overflow-y: auto;
  padding: 1rem 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  min-height: 0;
}
.chat-empty {
  margin: auto;
  text-align: center;
}
.chat-suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
}
.suggestion-pill {
  font-size: 0.775rem;
  color: #6366f1;
  background: rgba(99, 102, 241, 0.07);
  border: 1px solid rgba(99, 102, 241, 0.15);
  border-radius: 99px;
  padding: 0.3rem 0.8rem;
  cursor: default;
}

.chat-msg {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  max-width: 80%;
}
.chat-msg--user {
  align-self: flex-end;
  align-items: flex-end;
}
.chat-msg--ai {
  align-self: flex-start;
  align-items: flex-start;
}

.chat-bubble {
  padding: 0.55rem 0.9rem;
  border-radius: 14px;
  font-size: 0.875rem;
  line-height: 1.55;
  white-space: pre-wrap;
}
.chat-msg--user .chat-bubble {
  background: #4f46e5;
  color: #fff;
  border-bottom-right-radius: 4px;
}
.chat-msg--ai .chat-bubble {
  background: rgba(255, 255, 255, 0.9);
  color: #111827;
  border: 1px solid rgba(0, 0, 0, 0.07);
  border-bottom-left-radius: 4px;
}
.chat-bubble--error {
  background: #fee2e2 !important;
  color: #991b1b !important;
  border-color: transparent !important;
}

.msg-model {
  font-size: 0.68rem;
  color: #9ca3af;
  padding: 0.1rem 0.4rem;
  background: rgba(0, 0, 0, 0.04);
  border-radius: 99px;
}

.typing-dots {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 0.65rem 1rem;
}
.typing-dots span {
  width: 5px;
  height: 5px;
  border-radius: 50%;
  background: #9ca3af;
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
    transform: translateY(-5px);
  }
}

.chat-compose {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem;
  border-top: 1px solid rgba(99, 102, 241, 0.1);
  background: rgba(255, 255, 255, 0.75);
  backdrop-filter: blur(8px);
  flex-shrink: 0;
}
.compose-input {
  flex: 1;
  padding: 0.55rem 0.9rem;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  font-size: 0.875rem;
  outline: none;
  background: #fff;
  color: #111827;
  transition:
    border-color 0.15s,
    box-shadow 0.15s;
}
.compose-input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}
.compose-input:disabled {
  opacity: 0.6;
}
.compose-send {
  width: 2.25rem;
  height: 2.25rem;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #4f46e5;
  color: #fff;
  border: none;
  border-radius: 10px;
  cursor: pointer;
  flex-shrink: 0;
  transition:
    background 0.15s,
    opacity 0.15s;
}
.compose-send:hover:not(:disabled) {
  background: #4338ca;
}
.compose-send:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
.compose-send svg {
  width: 0.875rem;
  height: 0.875rem;
}

/* ── Workspace ────────────────────────────────────────────────────────────── */
.workspace {
  display: grid;
  grid-template-columns: 380px 1fr;
  gap: 1.25rem;
  align-items: start;
}

.workspace-left {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

/* ── Accordion ────────────────────────────────────────────────────────────── */
.accordion {
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  overflow: hidden;
  background: #fff;
}

.accordion-trigger {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.875rem 1rem;
  background: #f9fafb;
  border: none;
  cursor: pointer;
  text-align: left;
  transition: background 0.15s;
}
.accordion-trigger:hover {
  background: #f3f4f6;
}

.accordion-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.875rem;
  font-weight: 600;
  color: #111827;
}
.accordion-label svg {
  width: 1rem;
  height: 1rem;
  color: #6b7280;
  flex-shrink: 0;
}

.badge-count {
  font-size: 0.7rem;
  font-weight: 600;
  background: #e0e7ff;
  color: #4338ca;
  border-radius: 99px;
  padding: 0.1rem 0.45rem;
}

.chevron {
  width: 1rem;
  height: 1rem;
  color: #9ca3af;
  flex-shrink: 0;
  transition: transform 0.2s ease;
}
.accordion.open .chevron {
  transform: rotate(180deg);
}

.accordion-panel {
  display: grid;
  grid-template-rows: 0fr;
  transition: grid-template-rows 0.25s ease;
}
.accordion.open .accordion-panel {
  grid-template-rows: 1fr;
}

.accordion-panel-inner {
  overflow: hidden;
}

/* ── Contract rows ────────────────────────────────────────────────────────── */
.contract-list {
  padding: 0.375rem;
}

.contract-row {
  display: flex;
  align-items: center;
  gap: 0.625rem;
  padding: 0.625rem 0.75rem;
  border-radius: 8px;
  transition: background 0.12s;
}
.contract-row--clickable {
  cursor: pointer;
}
.contract-row--clickable:hover {
  background: #f9fafb;
}
.contract-row--selected {
  background: rgba(99, 102, 241, 0.07);
}
.contract-row--selected:hover {
  background: rgba(99, 102, 241, 0.1);
}

.contract-row-icon {
  width: 1.5rem;
  height: 1.5rem;
  flex-shrink: 0;
  color: #d1d5db;
}
.contract-row-icon svg {
  width: 100%;
  height: 100%;
}
.contract-row--selected .contract-row-icon {
  color: #818cf8;
}

.contract-row-body {
  flex: 1;
  min-width: 0;
}
.contract-row-name {
  font-size: 0.84rem;
  font-weight: 500;
  color: #111827;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.contract-row-meta {
  font-size: 0.74rem;
  color: #9ca3af;
  margin-top: 0.1rem;
}

.contract-row-end {
  display: flex;
  align-items: center;
  gap: 0.375rem;
  flex-shrink: 0;
}

/* ── Panel empty/loading states ───────────────────────────────────────────── */
.panel-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 1.5rem;
  font-size: 0.84rem;
  color: #9ca3af;
}
.panel-state--empty svg {
  width: 1.1rem;
  height: 1.1rem;
  opacity: 0.4;
}

/* ── Badges ───────────────────────────────────────────────────────────────── */
.badge {
  font-size: 0.7rem;
  font-weight: 500;
  padding: 0.18rem 0.5rem;
  border-radius: 99px;
  white-space: nowrap;
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

/* ── Dropzone ─────────────────────────────────────────────────────────────── */
.dropzone {
  margin: 0.75rem;
  border: 2px dashed #e5e7eb;
  border-radius: 8px;
  padding: 1.75rem 1.5rem;
  text-align: center;
  cursor: pointer;
  transition:
    border-color 0.15s,
    background 0.15s;
}
.dropzone:hover,
.dropzone--over {
  border-color: #6366f1;
  background: rgba(99, 102, 241, 0.03);
}
.dropzone svg {
  width: 1.75rem;
  height: 1.75rem;
  color: #9ca3af;
  margin-bottom: 0.5rem;
}
.dropzone-title {
  font-size: 0.875rem;
  font-weight: 500;
  color: #374151;
  margin: 0 0 0.2rem;
}
.dropzone-hint {
  font-size: 0.775rem;
  color: #9ca3af;
  margin: 0 0 0.75rem;
}
.dropzone-tags {
  display: flex;
  justify-content: center;
  gap: 0.375rem;
}
.dropzone-tags span {
  font-size: 0.7rem;
  background: #f3f4f6;
  color: #6b7280;
  padding: 0.15rem 0.5rem;
  border-radius: 4px;
}

.upload-status {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.6rem 1rem;
  font-size: 0.8rem;
  color: #6b7280;
}
.upload-bar {
  flex: 1;
  height: 3px;
  background: #e5e7eb;
  border-radius: 99px;
  overflow: hidden;
}
.upload-fill {
  height: 100%;
  background: #6366f1;
  width: 60%;
  animation: progress-shimmer 1.5s ease-in-out infinite;
}
@keyframes progress-shimmer {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.45;
  }
}

.upload-err {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  margin: 0 0.75rem 0.75rem;
  padding: 0.6rem 0.75rem;
  background: #fee2e2;
  border-radius: 6px;
  font-size: 0.8rem;
  color: #991b1b;
}

/* ── Right panel (detail) ─────────────────────────────────────────────────── */
.workspace-right {
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: #fff;
  min-height: 180px;
  position: sticky;
  top: 1rem;
  max-height: calc(100vh - 180px);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.detail-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 180px;
  gap: 0.75rem;
  color: #9ca3af;
  font-size: 0.875rem;
  padding: 2rem;
  text-align: center;
}
.detail-empty svg {
  width: 2.25rem;
  height: 2.25rem;
  opacity: 0.28;
}

.detail-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid #f3f4f6;
  flex-shrink: 0;
}
.detail-doc-type {
  font-size: 0.9375rem;
  font-weight: 600;
  color: #111827;
  margin: 0 0 0.2rem;
  letter-spacing: -0.01em;
}
.detail-doc-meta {
  font-size: 0.74rem;
  color: #9ca3af;
  margin: 0;
}

.detail-scroll {
  overflow-y: auto;
  flex: 1;
  min-height: 0;
}
.detail-loading {
  padding: 2rem;
  text-align: center;
  color: #9ca3af;
  font-size: 0.875rem;
}
.detail-error-msg {
  padding: 1rem 1.25rem;
  color: #991b1b;
  font-size: 0.875rem;
}
.detail-notice {
  margin: 0.75rem 1.25rem;
  padding: 0.6rem 0.85rem;
  background: #fef3c7;
  color: #92400e;
  border-radius: 6px;
  font-size: 0.8rem;
}

/* ── Fields table ─────────────────────────────────────────────────────────── */
.fields-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.84rem;
}
.fields-table th {
  text-align: left;
  font-weight: 500;
  color: #9ca3af;
  padding: 0.5rem 0 0.5rem 1.25rem;
  width: 38%;
  vertical-align: top;
  border-bottom: 1px solid #f3f4f6;
}
.fields-table td {
  padding: 0.5rem 1.25rem 0.5rem 0.625rem;
  vertical-align: top;
  border-bottom: 1px solid #f3f4f6;
  color: #111827;
}
.val-nil {
  color: #d1d5db;
  font-style: italic;
}
.val-yes {
  color: #15803d;
  font-weight: 500;
}
.val-no {
  color: #9ca3af;
}

.field-list {
  margin: 0;
  padding-left: 1.1rem;
}
.field-list li {
  margin-bottom: 0.2rem;
}

.obj-cards {
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
  padding: 0.2rem 0;
}
.obj-card {
  background: #f9fafb;
  border: 1px solid #e5e7eb;
  border-radius: 6px;
  padding: 0.5rem 0.65rem;
}
.obj-card-name {
  font-weight: 600;
  font-size: 0.8rem;
  margin-bottom: 0.25rem;
}
.obj-card-dl {
  display: flex;
  flex-wrap: wrap;
  gap: 0.2rem 0.65rem;
  margin: 0;
}
.obj-card-row {
  display: flex;
  align-items: baseline;
  gap: 0.25rem;
  font-size: 0.75rem;
}
.obj-card-row dt {
  color: #9ca3af;
  font-weight: 500;
  white-space: nowrap;
}
.obj-card-row dt::after {
  content: ":";
}
.obj-card-row dd {
  margin: 0;
  color: #374151;
}

/* ── Shared buttons ───────────────────────────────────────────────────────── */
.btn-ghost-sm {
  font-size: 0.74rem;
  font-weight: 500;
  color: #6b7280;
  background: none;
  border: 1px solid #e5e7eb;
  border-radius: 5px;
  padding: 0.18rem 0.5rem;
  cursor: pointer;
  transition:
    background 0.12s,
    color 0.12s;
  white-space: nowrap;
}
.btn-ghost-sm:hover {
  background: #f3f4f6;
  color: #374151;
}

.btn-icon {
  width: 1.75rem;
  height: 1.75rem;
  display: flex;
  align-items: center;
  justify-content: center;
  background: none;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  color: #9ca3af;
  flex-shrink: 0;
  transition:
    background 0.12s,
    color 0.12s;
}
.btn-icon:hover {
  background: #f3f4f6;
  color: #374151;
}
.btn-icon svg {
  width: 1rem;
  height: 1rem;
}

/* ── Spin ─────────────────────────────────────────────────────────────────── */
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
.spin {
  animation: spin 1.2s linear infinite;
}

/* ── Mobile ───────────────────────────────────────────────────────────────── */
@media (max-width: 767px) {
  .chat-zone {
    height: 340px;
  }
  .chat-subheading {
    display: none;
  }

  .workspace {
    grid-template-columns: 1fr;
    gap: 0.75rem;
  }
  .workspace-right {
    position: static;
    max-height: none;
  }
}
</style>
