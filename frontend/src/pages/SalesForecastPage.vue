<template>
  <section class="forecast-page">
    <div class="chat-shell">
      <header class="chat-topbar">
        <div class="identity">
          <div class="mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <line x1="4" y1="20" x2="20" y2="20" />
              <line x1="7" y1="16" x2="7" y2="10" />
              <line x1="12" y1="16" x2="12" y2="6" />
              <line x1="17" y1="16" x2="17" y2="12" />
            </svg>
          </div>
          <div>
            <h2 class="chat-heading">Sales Forecast</h2>
            <span class="chat-subheading">Monthly revenue outlook</span>
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
            <h1>What does the month look like?</h1>
            <p>
              Ask about booked revenue, unbooked consultants, or individual
              consultant contribution for a specific month.
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
            <div v-if="msg.role === 'assistant'" class="avatar">SF</div>
            <div class="message-body">
              <div :class="['bubble', msg.error && 'bubble--error']">
                {{ msg.content }}
              </div>
            </div>
            <div v-if="msg.role === 'user'" class="avatar">BE</div>
          </article>

          <article v-if="chatPending" class="message assistant">
            <div class="avatar">SF</div>
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
            placeholder="Ask about forecast, revenue, or bookings..."
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
  </section>
</template>

<script setup>
import { ref, nextTick, onMounted } from "vue";
import { useAuth } from "../composables/useAuth";

const auth = useAuth();

const sessionId = ref(crypto.randomUUID());
const chatMessages = ref([]);
const chatInput = ref("");
const chatPending = ref(false);
const chatScroll = ref(null);
const chatInputEl = ref(null);
const promptHistory = ref([]);
const historyIndex = ref(0);

const suggestions = [
  "What is the forecast for this month?",
  "Which consultants are unbooked next month?",
  "Show me the full forecast for May 2026",
  "How much revenue does a booked senior consultant contribute in July?",
];

onMounted(() => {
  nextTick(() => chatInputEl.value?.focus());
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
    const history = chatMessages.value.map((entry) => ({
      role: entry.role,
      content: entry.content,
    }));

    const res = await fetch("/api/sales-forecast-chat", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Auth-Token": `Bearer ${auth.getToken()}`,
      },
      body: JSON.stringify({
        sessionId: sessionId.value,
        message: msg,
        history,
      }),
    });
    if (!res.ok) throw res;
    const data = await res.json();
    chatMessages.value.push({
      id: crypto.randomUUID(),
      role: "assistant",
      content: data.answer,
    });
  } catch (err) {
    const status = err?.status ?? 0;
    const content =
      status === 401
        ? "Your session has expired. Please refresh the page and sign in again."
        : status === 403
          ? "Access denied. You do not have access to Sales Forecast."
          : status === 503
            ? "The forecast service is temporarily unavailable. Please try again shortly."
            : status >= 400
              ? `Request failed (${status}). Please try again.`
              : "Could not reach the server. Check your connection and try again.";
    chatMessages.value.push({
      id: crypto.randomUUID(),
      role: "assistant",
      content,
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
</script>

<style scoped>
.forecast-page {
  --ink: #181511;
  --muted: #746f67;
  --line: #ddd7cd;
  --paper: #fbfaf7;
  --panel: #ffffff;
  --user: #111827;
  --user-ink: #f9fafb;
  --assistant: #f4efe5;
  --accent: #1f6b5f;
  --accent-soft: rgba(31, 107, 95, 0.12);
  min-height: 100%;
}

.forecast-page :deep(*) {
  box-sizing: border-box;
}

.chat-shell {
  display: flex;
  min-height: calc(100vh - 4rem);
  flex-direction: column;
  gap: 1rem;
  background: linear-gradient(180deg, #fffdf8 0%, #f6f1e7 100%);
  padding: 1.25rem;
}

.chat-topbar,
.conversation,
.composer-wrap {
  width: min(100%, 980px);
  margin: 0 auto;
}

.chat-topbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.identity {
  display: flex;
  align-items: center;
  gap: 0.9rem;
}

.mark,
.avatar,
.send-btn,
.icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
}

.mark {
  width: 2.9rem;
  height: 2.9rem;
  border-radius: 0.9rem;
  background: var(--accent-soft);
  color: var(--accent);
}

.mark svg {
  width: 1.35rem;
  height: 1.35rem;
  stroke-width: 1.8;
}

.chat-heading {
  margin: 0;
  color: var(--ink);
  font-size: 1.05rem;
  font-weight: 650;
}

.chat-subheading {
  display: block;
  margin-top: 0.2rem;
  color: var(--muted);
  font-size: 0.9rem;
}

.icon-btn,
.send-btn {
  width: 2.8rem;
  height: 2.8rem;
  border: 1px solid var(--line);
  border-radius: 0.9rem;
  background: rgba(255, 255, 255, 0.88);
  color: var(--ink);
  cursor: pointer;
  transition:
    transform 0.16s ease,
    border-color 0.16s ease,
    background 0.16s ease;
}

.icon-btn:hover,
.send-btn:hover:not(:disabled) {
  transform: translateY(-1px);
  border-color: rgba(31, 107, 95, 0.35);
}

.icon-btn svg,
.send-btn svg {
  width: 1.15rem;
  height: 1.15rem;
  stroke-width: 1.9;
}

.conversation {
  flex: 1;
  overflow: auto;
  padding-right: 0.2rem;
}

.thread {
  display: flex;
  min-height: 100%;
  flex-direction: column;
  justify-content: flex-end;
  gap: 1rem;
  padding-bottom: 0.25rem;
}

.welcome {
  display: grid;
  gap: 0.9rem;
  padding: 2rem 0 1.2rem;
}

.welcome h1 {
  margin: 0;
  color: var(--ink);
  font-size: clamp(1.9rem, 5vw, 3.3rem);
  line-height: 1.04;
}

.welcome p {
  max-width: 44rem;
  margin: 0;
  color: var(--muted);
  font-size: 1rem;
  line-height: 1.6;
}

.chat-suggestions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.65rem;
}

.chat-suggestions button {
  border: 1px solid rgba(24, 21, 17, 0.1);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.86);
  color: var(--ink);
  padding: 0.72rem 1rem;
  font: inherit;
  cursor: pointer;
}

.message {
  display: flex;
  align-items: flex-end;
  gap: 0.75rem;
}

.message.user {
  justify-content: flex-end;
}

.avatar {
  width: 2.5rem;
  height: 2.5rem;
  border-radius: 0.9rem;
  background: rgba(24, 21, 17, 0.08);
  color: var(--ink);
  font-size: 0.78rem;
  font-weight: 700;
  letter-spacing: 0;
}

.message-body {
  max-width: min(46rem, calc(100% - 4.5rem));
  display: grid;
  gap: 0.45rem;
}

.bubble {
  border-radius: 1.1rem;
  background: var(--assistant);
  color: var(--ink);
  padding: 0.95rem 1rem;
  white-space: pre-wrap;
  line-height: 1.55;
}

.message.user .bubble {
  background: var(--user);
  color: var(--user-ink);
}

.bubble--error {
  background: #fff1f1;
  color: #8a2121;
}

.typing-dots {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  border-radius: 999px;
  background: var(--assistant);
  padding: 0.9rem 1rem;
}

.typing-dots span {
  width: 0.45rem;
  height: 0.45rem;
  border-radius: 999px;
  background: rgba(24, 21, 17, 0.42);
  animation: pulse 0.9s ease-in-out infinite;
}

.typing-dots span:nth-child(2) {
  animation-delay: 0.12s;
}

.typing-dots span:nth-child(3) {
  animation-delay: 0.24s;
}

.composer-wrap {
  display: grid;
  gap: 0.65rem;
  padding-top: 0.5rem;
}

.composer {
  display: flex;
  align-items: flex-end;
  gap: 0.85rem;
  border: 1px solid rgba(24, 21, 17, 0.08);
  border-radius: 1.25rem;
  background: rgba(255, 255, 255, 0.92);
  padding: 0.85rem;
}

.composer textarea {
  width: 100%;
  min-height: 1.5rem;
  max-height: 9.25rem;
  resize: none;
  border: 0;
  background: transparent;
  color: var(--ink);
  font: inherit;
  line-height: 1.5;
  outline: none;
}

.composer textarea::placeholder,
.compose-hint {
  color: var(--muted);
}

.send-btn {
  background: var(--accent);
  border-color: transparent;
  color: #f8fbfa;
}

.send-btn:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}

.compose-hint {
  font-size: 0.83rem;
}

@keyframes pulse {
  0%,
  80%,
  100% {
    transform: translateY(0);
    opacity: 0.4;
  }
  40% {
    transform: translateY(-3px);
    opacity: 1;
  }
}

@media (max-width: 720px) {
  .chat-shell {
    min-height: calc(100vh - 3.25rem);
    padding: 1rem 0.9rem;
  }

  .message-body {
    max-width: calc(100% - 3.6rem);
  }

  .welcome {
    padding-top: 1.4rem;
  }
}
</style>
