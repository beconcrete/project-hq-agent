<template>
  <section class="hq-page">
    <div class="chat-shell">
      <header class="chat-topbar">
        <div class="identity">
          <div class="mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <rect x="3" y="3" width="7" height="7" rx="1" />
              <rect x="14" y="3" width="7" height="7" rx="1" />
              <rect x="3" y="14" width="7" height="7" rx="1" />
              <rect x="14" y="14" width="7" height="7" rx="1" />
            </svg>
          </div>
          <div>
            <h2 class="chat-heading">HQ</h2>
            <span class="chat-subheading"
              >Contracts, employees, projects &amp; time</span
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

      <div ref="chatScroll" class="conversation">
        <div class="thread">
          <section v-if="!chatMessages.length && !chatPending" class="welcome">
            <h1>What do you need today?</h1>
            <p>
              Ask about contracts, employees, customers, or projects — or log
              time directly in this conversation.
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
            <div v-if="msg.role === 'assistant'" class="avatar">HQ</div>
            <div class="message-body">
              <div :class="['bubble', msg.error && 'bubble--error']">
                <template v-if="msg.isUpload">
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    class="upload-icon"
                  >
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
                    <polyline points="17 8 12 3 7 8" />
                    <line x1="12" y1="3" x2="12" y2="15" />
                  </svg>
                  {{ msg.content }}
                </template>
                <template v-else>{{ msg.content }}</template>
              </div>
            </div>
            <div v-if="msg.role === 'user'" class="avatar">ME</div>
          </article>

          <article v-if="chatPending" class="message assistant">
            <div class="avatar">HQ</div>
            <div class="typing-dots"><span /><span /><span /></div>
          </article>
        </div>
      </div>

      <footer class="composer-wrap">
        <form class="composer" @submit.prevent="sendMessage">
          <button
            type="button"
            class="attach-btn"
            :disabled="chatPending"
            aria-label="Attach contract"
            @click="triggerFilePicker"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <line x1="12" y1="5" x2="12" y2="19" />
              <line x1="5" y1="12" x2="19" y2="12" />
            </svg>
          </button>

          <input
            ref="fileInputEl"
            type="file"
            accept=".pdf,.docx"
            style="display: none"
            @change="onFileSelected"
          />

          <textarea
            ref="chatInputEl"
            v-model="chatInput"
            rows="1"
            placeholder="Ask anything or report time..."
            :disabled="chatPending"
            @keydown.enter.exact.prevent="sendMessage"
            @keydown.enter.shift.exact="chatInput += '\n'"
            @input="autoGrow"
          />

          <button
            type="submit"
            class="send-btn"
            :disabled="chatPending || !chatInput.trim()"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <line x1="22" y1="2" x2="11" y2="13" />
              <polygon points="22 2 15 22 11 13 2 9 22 2" />
            </svg>
          </button>
        </form>
      </footer>
    </div>
  </section>
</template>

<script setup>
import { ref, nextTick } from "vue";
import { useAuth } from "../composables/useAuth";

const auth = useAuth();
const sessionId = crypto.randomUUID();

const chatMessages = ref([]);
const chatInput = ref("");
const chatPending = ref(false);
const chatScroll = ref(null);
const chatInputEl = ref(null);
const fileInputEl = ref(null);
let messageCounter = 0;

const suggestions = [
  "Which contracts expire in the next 90 days?",
  "Report 2 hours on a project",
  "List all active projects",
  "Who are the employees on project X?",
];

function addMessage(role, content, extra = {}) {
  chatMessages.value.push({ id: ++messageCounter, role, content, ...extra });
  scrollToBottom();
}

async function scrollToBottom() {
  await nextTick();
  if (chatScroll.value) {
    chatScroll.value.scrollTop = chatScroll.value.scrollHeight;
  }
}

function autoGrow(e) {
  const el = e.target;
  el.style.height = "auto";
  el.style.height = Math.min(el.scrollHeight, 200) + "px";
}

function useSuggestion(text) {
  chatInput.value = text;
  chatInputEl.value?.focus();
}

function clearChat() {
  chatMessages.value = [];
  chatInput.value = "";
}

function getToken() {
  return auth.getToken();
}

async function sendMessage() {
  const text = chatInput.value.trim();
  if (!text || chatPending.value) return;

  chatInput.value = "";
  if (chatInputEl.value) {
    chatInputEl.value.style.height = "auto";
  }

  addMessage("user", text);
  chatPending.value = true;

  try {
    const token = await getToken();
    const headers = { "Content-Type": "application/json" };
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;

    const res = await fetch("/api/hq-chat", {
      method: "POST",
      headers,
      body: JSON.stringify({ sessionId, message: text }),
    });

    if (!res.ok) {
      const errText = await res.text().catch(() => "");
      addMessage(
        "assistant",
        `Error ${res.status}: ${errText || "Something went wrong. Please try again."}`,
        { error: true },
      );
      return;
    }

    const data = await res.json();
    addMessage("assistant", data.answer);
  } catch {
    addMessage("assistant", "Connection error. Please try again.", {
      error: true,
    });
  } finally {
    chatPending.value = false;
  }
}

function triggerFilePicker() {
  fileInputEl.value?.click();
}

async function onFileSelected(e) {
  const file = e.target.files?.[0];
  if (!file) return;
  e.target.value = "";

  addMessage("user", file.name, { isUpload: true });
  chatPending.value = true;

  try {
    const token = getToken();
    const formData = new FormData();
    formData.append("file", file);

    const headers = {};
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;

    const res = await fetch("/api/upload-contract", {
      method: "POST",
      headers,
      body: formData,
    });

    if (!res.ok) {
      addMessage(
        "assistant",
        `Failed to upload "${file.name}". Make sure you have admin access.`,
        { error: true },
      );
      return;
    }

    const { correlationId } = await res.json();
    addMessage("assistant", `"${file.name}" is queued for processing…`);
    chatPending.value = false;

    pollContractStatus(correlationId, file.name, token);
  } catch {
    addMessage("assistant", "Upload failed — connection error.", {
      error: true,
    });
  } finally {
    chatPending.value = false;
  }
}

async function pollContractStatus(correlationId, fileName, token) {
  const INTERVAL = 3000;
  const MAX_ATTEMPTS = 40; // ~2 minutes

  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    await new Promise((r) => setTimeout(r, INTERVAL));

    try {
      const headers = {};
      if (token) headers["X-Auth-Token"] = `Bearer ${token}`;
      const res = await fetch(
        `/api/check-status?correlationId=${correlationId}`,
        { headers },
      );
      if (!res.ok) continue;

      const { status, statusMessage, lastError } = await res.json();

      if (
        status === "completed" ||
        status === "pending_review" ||
        status === "approved"
      ) {
        addMessage(
          "assistant",
          `"${fileName}" has been processed and is ready. You can now ask me about it.`,
        );
        return;
      }

      if (status === "failed") {
        addMessage(
          "assistant",
          `Processing "${fileName}" failed.${lastError ? ` Error: ${lastError}` : ""} You can try uploading it again.`,
          { error: true },
        );
        return;
      }
    } catch {
      // transient error — keep polling
    }
  }

  addMessage(
    "assistant",
    `Processing "${fileName}" is taking longer than expected. It should still complete in the background.`,
  );
}
</script>

<style scoped>
.hq-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.chat-shell {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

.chat-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem 1.5rem;
  border-bottom: 1px solid var(--border, #e5e7eb);
  flex-shrink: 0;
}

.identity {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.mark {
  width: 2rem;
  height: 2rem;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--accent, #4f46e5);
}

.mark svg {
  width: 1.5rem;
  height: 1.5rem;
  stroke-width: 2;
}

.chat-heading {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
  line-height: 1.2;
}

.chat-subheading {
  font-size: 0.75rem;
  color: var(--text-muted, #6b7280);
}

.icon-btn {
  background: none;
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 0.375rem;
  padding: 0.375rem;
  cursor: pointer;
  color: var(--text-muted, #6b7280);
  display: flex;
  align-items: center;
}

.icon-btn svg {
  width: 1rem;
  height: 1rem;
  stroke-width: 2;
}

.icon-btn:hover {
  color: var(--text, #111827);
  border-color: var(--text-muted, #6b7280);
}

.conversation {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem;
}

.thread {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 48rem;
  margin: 0 auto;
}

.welcome {
  text-align: center;
  padding: 3rem 1rem;
}

.welcome h1 {
  font-size: 1.5rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
}

.welcome p {
  color: var(--text-muted, #6b7280);
  margin-bottom: 1.5rem;
  font-size: 0.9rem;
}

.chat-suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
}

.chat-suggestions button {
  background: var(--surface, #f9fafb);
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 1rem;
  padding: 0.375rem 0.875rem;
  font-size: 0.8rem;
  cursor: pointer;
  color: var(--text, #111827);
  transition: background 0.15s;
}

.chat-suggestions button:hover {
  background: var(--border, #e5e7eb);
}

.message {
  display: flex;
  gap: 0.625rem;
  align-items: flex-start;
}

.message.user {
  flex-direction: row-reverse;
}

.avatar {
  flex-shrink: 0;
  width: 1.75rem;
  height: 1.75rem;
  border-radius: 50%;
  background: var(--accent, #4f46e5);
  color: #fff;
  font-size: 0.6rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  letter-spacing: 0.03em;
}

.message.user .avatar {
  background: var(--surface, #f3f4f6);
  color: var(--text, #111827);
  border: 1px solid var(--border, #e5e7eb);
}

.message-body {
  max-width: 75%;
}

.bubble {
  background: var(--surface, #f3f4f6);
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 1rem;
  padding: 0.625rem 1rem;
  font-size: 0.875rem;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

.message.user .bubble {
  background: var(--accent, #4f46e5);
  color: #fff;
  border-color: transparent;
}

.bubble--error {
  background: #fef2f2;
  border-color: #fecaca;
  color: #b91c1c;
}

.message.user .bubble--error {
  background: #fef2f2;
  color: #b91c1c;
  border-color: #fecaca;
}

.upload-icon {
  width: 0.9rem;
  height: 0.9rem;
  vertical-align: middle;
  margin-right: 0.375rem;
  stroke-width: 2;
}

.typing-dots {
  display: flex;
  gap: 0.3rem;
  padding: 0.75rem 1rem;
  background: var(--surface, #f3f4f6);
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 1rem;
  align-items: center;
}

.typing-dots span {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: var(--text-muted, #9ca3af);
  animation: dot-bounce 1.2s infinite;
}

.typing-dots span:nth-child(2) {
  animation-delay: 0.2s;
}
.typing-dots span:nth-child(3) {
  animation-delay: 0.4s;
}

@keyframes dot-bounce {
  0%,
  80%,
  100% {
    transform: translateY(0);
  }
  40% {
    transform: translateY(-0.3rem);
  }
}

.composer-wrap {
  border-top: 1px solid var(--border, #e5e7eb);
  padding: 0.875rem 1.5rem;
  flex-shrink: 0;
}

.composer {
  display: flex;
  align-items: flex-end;
  gap: 0.5rem;
  max-width: 48rem;
  margin: 0 auto;
  background: var(--surface, #f9fafb);
  border: 1px solid var(--border, #e5e7eb);
  border-radius: 0.75rem;
  padding: 0.375rem 0.375rem 0.375rem 0.625rem;
}

.attach-btn,
.send-btn {
  flex-shrink: 0;
  width: 2rem;
  height: 2rem;
  border-radius: 0.5rem;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  background: none;
  color: var(--text-muted, #6b7280);
  transition:
    color 0.15s,
    background 0.15s;
}

.attach-btn svg,
.send-btn svg {
  width: 1rem;
  height: 1rem;
  stroke-width: 2;
}

.attach-btn:hover:not(:disabled) {
  color: var(--text, #111827);
  background: var(--border, #e5e7eb);
}

.send-btn:not(:disabled) {
  color: var(--accent, #4f46e5);
}

.send-btn:hover:not(:disabled) {
  background: var(--border, #e5e7eb);
}

.send-btn:disabled,
.attach-btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

textarea {
  flex: 1;
  border: none;
  background: transparent;
  resize: none;
  font-size: 0.875rem;
  line-height: 1.5;
  padding: 0.375rem 0;
  outline: none;
  font-family: inherit;
  min-height: 1.5rem;
  max-height: 12rem;
  overflow-y: auto;
}
</style>
