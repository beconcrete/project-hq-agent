<template>
  <section class="hr-page">
    <div class="chat-shell">
      <header class="chat-topbar">
        <div class="identity">
          <div class="mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <path d="M23 21v-2a4 4 0 0 0-3-3.87" />
              <path d="M16 3.13a4 4 0 0 1 0 7.75" />
            </svg>
          </div>
          <div>
            <h2 class="chat-heading">HR Assistant</h2>
            <span class="chat-subheading">Employee lifecycle management</span>
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
            <h1>What do you need to know about your team?</h1>
            <p>
              Add employees, update salaries, offboard, or ask about billing and
              compensation. All answers are based on your stored data.
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
            <div v-if="msg.role === 'assistant'" class="avatar">HR</div>
            <div class="message-body">
              <div :class="['bubble', msg.error && 'bubble--error']">
                {{ msg.content }}
              </div>
            </div>
            <div v-if="msg.role === 'user'" class="avatar">BE</div>
          </article>

          <article v-if="chatPending" class="message assistant">
            <div class="avatar">HR</div>
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
            placeholder="Ask about employees, salaries, or billing..."
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
  "List all active employees",
  "What would Anna's salary be for 160 hours?",
  "What's the current bonus threshold?",
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
    const res = await fetch("/api/hr-chat", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Auth-Token": `Bearer ${auth.getToken()}`,
      },
      body: JSON.stringify({ sessionId: sessionId.value, message: msg }),
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
          ? "Access denied. Admin role is required to use HR."
          : status === 503
            ? "The HR service is temporarily unavailable. Please try again shortly."
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
.hr-page {
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
  min-height: 82vh;
  display: grid;
  grid-template-rows: auto 1fr auto;
  background: rgba(255, 255, 255, 0.92);
  border: 1px solid var(--line);
  border-radius: 18px;
  box-shadow: 0 20px 55px rgba(31, 28, 22, 0.11);
  overflow: hidden;
}

.chat-topbar,
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

.identity {
  display: flex;
  align-items: center;
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
}

.chat-subheading {
  display: block;
  margin-top: 0.18rem;
  color: var(--muted);
  font-size: 0.82rem;
}

.icon-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2.25rem;
  height: 2.25rem;
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
  display: flex;
  align-items: center;
  justify-content: center;
  width: 2.65rem;
  height: 2.65rem;
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

@media (max-width: 780px) {
  .chat-shell {
    min-height: 78vh;
    border-radius: 13px;
  }
  .chat-subheading,
  .compose-hint {
    display: none;
  }
  .conversation {
    padding: 1.5rem 0.9rem 1.1rem;
  }
  .chat-suggestions {
    grid-template-columns: 1fr;
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
