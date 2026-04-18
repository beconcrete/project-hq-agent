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
        multiple
        hidden
        @change="onFileChange"
      />
    </div>

    <!-- Contract list -->
    <div v-if="contracts.length > 0" class="contracts-grid">
      <div
        v-for="contract in contracts"
        :key="contract.id"
        class="contract-card"
      >
        <div class="contract-card-icon">
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
        </div>
        <div class="contract-card-body">
          <div class="contract-card-name">{{ contract.name }}</div>
          <div class="contract-card-meta">{{ contract.uploadedAt }}</div>
        </div>
        <span class="badge badge-processing">Processing</span>
      </div>
    </div>

    <!-- Empty state -->
    <div v-else class="contracts-empty">
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
  </section>
</template>

<script setup>
import { ref } from "vue";
import { useAuth } from "../composables/useAuth";

const auth = useAuth();

const fileInput = ref(null);
const isDragging = ref(false);
const uploadState = ref("idle"); // 'idle' | 'uploading' | 'error'
const uploadError = ref("");
const contracts = ref([]);

const MAX_SIZE = 20 * 1024 * 1024;
const ALLOWED_TYPES = [
  "application/pdf",
  "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
];

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
      contracts.value.push({
        id: data.id || crypto.randomUUID(),
        name: file.name,
        uploadedAt: new Date().toLocaleString(),
      });
    }
    uploadState.value = "idle";
  } catch (err) {
    uploadError.value = err.message;
    uploadState.value = "error";
  }
}
</script>
