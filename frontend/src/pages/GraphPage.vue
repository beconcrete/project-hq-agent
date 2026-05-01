<template>
  <div class="graph-page">
    <div ref="canvasEl" class="graph-canvas" />

    <div v-if="hasGraph" class="graph-controls">
      <button class="ctrl-btn" title="Fit to screen" @click="fitGraph(true)">
        ⊞
      </button>
      <button class="ctrl-btn" title="Zoom in" @click="zoomIn">+</button>
      <button class="ctrl-btn" title="Zoom out" @click="zoomOut">−</button>
    </div>

    <div v-if="focusedId" class="graph-breadcrumb">
      <button class="breadcrumb-btn" type="button" @click="exitFocus">
        ← All
      </button>
    </div>

    <div v-if="loading" class="graph-overlay">
      <div class="graph-state-card">
        <p class="graph-state-kicker">Loading graph</p>
        <p class="graph-state-copy">
          Fetching customers, contracts, projects and people…
        </p>
      </div>
    </div>
    <div v-else-if="error" class="graph-overlay">
      <div class="graph-state-card">
        <p class="graph-state-kicker">Graph unavailable</p>
        <p class="graph-state-copy">{{ error }}</p>
        <button class="graph-state-btn" type="button" @click="loadGraph">
          Try again
        </button>
      </div>
    </div>
    <div v-else-if="initialized && !hasGraph" class="graph-overlay">
      <div class="graph-state-card">
        <p class="graph-state-kicker">Nothing to show yet</p>
        <p class="graph-state-copy">
          Add customers, upload contracts or create projects to see the map.
        </p>
      </div>
    </div>

    <Transition name="detail-panel">
      <aside
        v-if="selectedNode"
        class="node-detail"
        :style="{ '--node-accent': selectedNode.color || '#126b5f' }"
      >
        <button class="node-detail-close" type="button" @click="selectedNode = null">×</button>
        <div class="detail-badge">{{ nodeTypeLabel(selectedNode.type) }}</div>
        <h3 class="detail-title">{{ selectedNode.label }}</h3>

        <template v-if="selectedNode.type === 'customer'">
          <p v-if="selectedNode.orgNumber" class="node-detail-row">
            Org: {{ selectedNode.orgNumber }}
          </p>
          <p class="node-detail-row">
            {{ selectedNode.contractCount }} contract{{
              selectedNode.contractCount !== 1 ? "s" : ""
            }}
            · {{ selectedNode.projectCount }} project{{
              selectedNode.projectCount !== 1 ? "s" : ""
            }}
          </p>
        </template>

        <template v-if="selectedNode.type === 'contract'">
          <dl class="node-detail-fields">
            <div v-if="selectedNode.counterparty" class="node-detail-field">
              <dt>Counterparty</dt>
              <dd>{{ selectedNode.counterparty }}</dd>
            </div>
            <div v-if="selectedNode.expiryDate" class="node-detail-field">
              <dt>Expires</dt>
              <dd>{{ selectedNode.expiryDate }}</dd>
            </div>
            <div v-if="selectedNode.noticeDeadline" class="node-detail-field">
              <dt>Notice deadline</dt>
              <dd>{{ selectedNode.noticeDeadline }}</dd>
            </div>
            <div v-if="selectedNode.noticePeriodDays != null" class="node-detail-field">
              <dt>Notice period</dt>
              <dd>{{ selectedNode.noticePeriodDays }} days</dd>
            </div>
            <div v-if="selectedNode.autoRenewal != null" class="node-detail-field">
              <dt>Renewal</dt>
              <dd>{{ selectedNode.autoRenewal ? "Auto-renews" : "No auto-renewal" }}</dd>
            </div>
            <div v-if="contractPaymentSummary" class="node-detail-field">
              <dt>Payment</dt>
              <dd>{{ contractPaymentSummary }}</dd>
            </div>
            <div v-if="selectedNode.reviewState" class="node-detail-field">
              <dt>Review</dt>
              <dd>
                <span class="node-review-chip" :class="`node-review-chip--${selectedNode.reviewState}`">
                  {{ formatReviewState(selectedNode.reviewState) }}
                </span>
              </dd>
            </div>
          </dl>

          <div v-if="selectedNode.riskFlags && selectedNode.riskFlags.length" class="node-detail-risks">
            <p class="node-detail-section">Risk flags</p>
            <ul class="node-risk-list">
              <li v-for="flag in selectedNode.riskFlags" :key="flag">{{ flag }}</li>
            </ul>
          </div>

          <div v-if="selectedNode.peopleMentioned && selectedNode.peopleMentioned.length" class="node-detail-people">
            <p class="node-detail-section">People</p>
            <ul class="node-people-list">
              <li v-for="person in selectedNode.peopleMentioned" :key="person">{{ person }}</li>
            </ul>
          </div>

          <button class="node-detail-link" type="button" @click="openContract(selectedNode.contractId)">
            View contract →
          </button>
        </template>

        <template v-if="selectedNode.type === 'project'">
          <p class="node-detail-row">{{ selectedNode.status }}</p>
          <p v-if="selectedNode.startDate" class="node-detail-row">
            {{ selectedNode.startDate }}{{ selectedNode.endDate ? " → " + selectedNode.endDate : "" }}
          </p>

          <div class="timereport-section">
            <p class="node-detail-section">Hours reported</p>
            <div v-if="projectTimereportsLoading" class="timereport-state">Loading…</div>
            <template v-else-if="projectTimereports">
              <p v-if="projectTimereports.months.length === 0" class="timereport-state timereport-state--empty">
                No hours reported yet
              </p>
              <div v-else class="timereport-months">
                <div v-for="m in projectTimereports.months" :key="m.month" class="timereport-month">
                  <button class="month-header" type="button" @click="toggleMonth(m.month)">
                    <span class="month-label">{{ formatMonth(m.month) }}</span>
                    <span class="month-hours">{{ formatHours(m.totalHours) }}</span>
                    <span class="month-chevron" :class="{ open: openMonths[m.month] }">›</span>
                  </button>
                  <div v-if="openMonths[m.month]" class="month-entries">
                    <div v-for="(entry, i) in m.entries" :key="i" class="month-entry">
                      <div class="entry-meta">
                        <span class="entry-date">{{ entry.date }}</span>
                        <span class="entry-hours">{{ entry.hours }}h</span>
                      </div>
                      <p v-if="entry.note" class="entry-note">{{ entry.note }}</p>
                    </div>
                  </div>
                </div>
              </div>
            </template>
          </div>
        </template>

        <template v-if="selectedNode.type === 'employee'">
          <p class="node-detail-row">{{ selectedNode.email }}</p>
          <p class="node-detail-row">{{ selectedNode.status }}</p>
        </template>
      </aside>
    </Transition>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import cytoscape from "cytoscape";
import { useHqGraph } from "../composables/useHqGraph";
import { useAuth } from "../composables/useAuth";

const { graph, loading, error, fetch } = useHqGraph();
const auth = useAuth();

const canvasEl = ref(null);
const selectedNode = ref(null);
const focusedId = ref(null);
const initialized = ref(false);
const projectTimereports = ref(null);
const projectTimereportsLoading = ref(false);
const openMonths = ref({});

let cy = null;
let resizeObserver = null;

const ROOT_ID = "__root__";
const INFO_ID = "__info__";

const PALETTE = [
  "#c17a2b",
  "#2c7ed6",
  "#b5487c",
  "#6b5dd3",
  "#d95f43",
  "#1d9a73",
  "#9b7a20",
  "#c05e8c",
];

const hasGraph = computed(() => graph.value.nodes.length > 0);

const contractPaymentSummary = computed(() => {
  const n = selectedNode.value;
  if (!n || n.type !== "contract") return null;
  const parts = [];
  if (n.paymentAmount != null) {
    const formatted = Number(n.paymentAmount).toLocaleString("sv-SE");
    parts.push(n.paymentCurrency ? `${formatted} ${n.paymentCurrency}` : formatted);
  }
  if (n.paymentUnit) parts.push(n.paymentUnit);
  if (n.paymentType) parts.push(n.paymentType);
  return parts.length ? parts.join(" · ") : null;
});

const REVIEW_LABELS = {
  approved: "Approved",
  approved_by_extraction: "Auto-approved",
  pending_review: "Pending review",
  rejected: "Rejected",
  duplicate_deleted: "Duplicate",
  failed: "Failed",
};

function formatReviewState(state) {
  if (!state) return "—";
  return REVIEW_LABELS[state] ?? state.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
}

async function openContract(contractId) {
  const tab = window.open("", "_blank");
  try {
    const res = await globalThis.fetch(
      `/api/get-contract-download-url?correlationId=${encodeURIComponent(contractId)}`,
      { headers: { "X-Auth-Token": `Bearer ${auth.getToken()}` } },
    );
    if (!res.ok) throw new Error(`Failed (${res.status})`);
    const { url } = await res.json();
    if (tab) tab.location.href = url;
    else window.location.href = url;
  } catch {
    if (tab) tab.close();
  }
}

// ── Project timereports ─────────────────────────────────────────────────────

watch(selectedNode, async (node) => {
  if (!node || node.type !== "project") {
    projectTimereports.value = null;
    openMonths.value = {};
    return;
  }
  await fetchProjectTimereports(node.projectId);
});

async function fetchProjectTimereports(projectId) {
  projectTimereportsLoading.value = true;
  projectTimereports.value = null;
  openMonths.value = {};
  try {
    const headers = {};
    const token = auth.getToken();
    if (token) headers["X-Auth-Token"] = `Bearer ${token}`;
    const res = await globalThis.fetch(
      `/api/project-timereports?projectId=${encodeURIComponent(projectId)}`,
      { headers },
    );
    if (!res.ok) throw new Error(`Failed (${res.status})`);
    const data = await res.json();
    projectTimereports.value = data;
    if (data.months.length > 0) openMonths.value = { [data.months[0].month]: true };
  } catch {
    projectTimereports.value = { months: [] };
  } finally {
    projectTimereportsLoading.value = false;
  }
}

function toggleMonth(month) {
  openMonths.value = { ...openMonths.value, [month]: !openMonths.value[month] };
}

const MONTH_NAMES = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

function formatMonth(ym) {
  const [year, month] = ym.split("-");
  return `${MONTH_NAMES[parseInt(month, 10) - 1]} ${year}`;
}

function formatHours(h) {
  return h % 1 === 0 ? `${h}h` : `${h.toFixed(1)}h`;
}

// ── Lifecycle ───────────────────────────────────────────────────────────────

onMounted(async () => {
  initCytoscape();
  await loadGraph();
});

onUnmounted(() => {
  resizeObserver?.disconnect();
  cy?.destroy();
  cy = null;
});

async function loadGraph() {
  await fetch();
}

// ── Graph sync ──────────────────────────────────────────────────────────────

watch(
  () => graph.value,
  () => {
    if (cy) syncElements();
  },
  { deep: true },
);

function syncElements() {
  cy.elements().remove();
  if (!hasGraph.value) {
    initialized.value = true;
    return;
  }
  cy.add(buildElements());
  applyVisibility();
  runLayout({ animate: false });
  initialized.value = true;
}

// ── Element builder ─────────────────────────────────────────────────────────

function buildElements() {
  const elements = [];

  for (const node of graph.value.nodes) {
    switch (node.type) {
      case "root":
        elements.push({
          data: {
            id: ROOT_ID,
            type: "root",
            label: node.label,
            color: "#126b5f",
            nw: 160,
            nh: 72,
            shape: "ellipse",
            record: node,
          },
        });
        break;

      case "info":
        elements.push({
          data: {
            id: INFO_ID,
            type: "info",
            label: node.label,
            color: "#4a73a8",
            nw: 120,
            nh: 52,
            shape: "hexagon",
            record: node,
          },
        });
        break;

      case "customer": {
        const color =
          node.id === "__unlinked__" ? "#6b7280" : colorFor(node.label);
        elements.push({
          data: {
            id: node.id,
            type: "customer",
            label: node.label,
            color,
            nw: 148,
            nh: 56,
            shape: "round-rectangle",
            record: node,
          },
        });
        break;
      }

      case "contract": {
        const custColor = customerColorFor(node.parentId);
        elements.push({
          data: {
            id: node.id,
            type: "contract",
            label: truncate(
              node.label || node.documentType || node.fileName || "Contract",
            ),
            color: custColor,
            nw: 130,
            nh: 50,
            shape: "rectangle",
            parentId: node.parentId,
            record: node,
          },
        });
        break;
      }

      case "project": {
        const custColor = customerColorFor(node.parentId);
        elements.push({
          data: {
            id: node.id,
            type: "project",
            label: truncate(node.label),
            color: custColor,
            nw: 130,
            nh: 50,
            shape: "round-rectangle",
            parentId: node.parentId,
            record: node,
          },
        });
        break;
      }

      case "employee":
        elements.push({
          data: {
            id: node.id,
            type: "employee",
            label: truncate(node.label),
            color: "#5f85a3",
            nw: 120,
            nh: 44,
            shape: "ellipse",
            parentId: INFO_ID,
            record: node,
          },
        });
        break;
    }
  }

  for (const edge of graph.value.edges) {
    elements.push({
      data: {
        id: edge.id,
        source: edge.source === "__root__" ? ROOT_ID : edge.source,
        target: edge.target,
      },
    });
  }

  return elements;
}

function customerColorFor(parentId) {
  if (!parentId || parentId === "__unlinked__") return "#6b7280";
  const node = graph.value.nodes.find((n) => n.id === parentId);
  return node ? colorFor(node.label) : "#6b7280";
}

function colorFor(name) {
  if (!name) return "#6b7280";
  let hash = 0;
  for (let i = 0; i < name.length; i++)
    hash = (hash * 31 + name.charCodeAt(i)) >>> 0;
  return PALETTE[hash % PALETTE.length];
}

function truncate(label) {
  if (!label) return "";
  return label.length > 32 ? `${label.slice(0, 29)}…` : label;
}

// ── Visibility ──────────────────────────────────────────────────────────────

function applyVisibility() {
  if (!cy) return;
  cy.elements().removeClass("is-hidden is-ancestor is-focused");

  if (!focusedId.value) {
    // Overview: show root, customers, info — hide contracts, projects, employees
    cy.nodes().forEach((n) => {
      const t = n.data("type");
      if (t === "contract" || t === "project" || t === "employee")
        n.addClass("is-hidden");
    });
    cy.edges().forEach((e) => {
      const tgt = e.target().data("type");
      if (tgt === "contract" || tgt === "project" || tgt === "employee")
        e.addClass("is-hidden");
    });
    return;
  }

  const fid = focusedId.value;
  const focusedType = fid === INFO_ID ? "info" : "customer";

  cy.nodes().forEach((n) => {
    const id = n.id();
    const t = n.data("type");
    if (id === ROOT_ID) {
      n.addClass("is-ancestor");
    } else if (id === fid) {
      n.addClass("is-focused");
    } else if (
      focusedType === "customer" &&
      (t === "contract" || t === "project") &&
      n.data("parentId") === fid
    ) {
      // visible
    } else if (focusedType === "info" && t === "employee") {
      // visible
    } else {
      n.addClass("is-hidden");
    }
  });

  cy.edges().forEach((e) => {
    const src = e.source().id();
    const tgt = e.target().id();
    const tgtType = e.target().data("type");
    const isRootToFocused = src === ROOT_ID && tgt === fid;
    const isChild =
      src === fid &&
      ((focusedType === "customer" &&
        (tgtType === "contract" || tgtType === "project")) ||
        (focusedType === "info" && tgtType === "employee"));
    if (!isRootToFocused && !isChild) e.addClass("is-hidden");
  });
}

// ── Layout ──────────────────────────────────────────────────────────────────

function runLayout({ animate = true } = {}) {
  if (!cy || !hasGraph.value) return;
  focusedId.value
    ? runFocusLayout({ animate })
    : runOverviewLayout({ animate });
}

function runOverviewLayout({ animate = true } = {}) {
  const root = cy.$id(ROOT_ID);
  if (!root.length) return;
  const center = { x: cy.width() / 2, y: cy.height() / 2 };
  placeNode(root, center, animate);

  const level1 = cy
    .nodes()
    .filter((n) => !n.hasClass("is-hidden") && n.id() !== ROOT_ID);
  const radius = Math.max(160, Math.min(340, 120 + level1.length * 30));
  layoutRing(level1, center, radius, animate);

  setTimeout(() => fitGraph(animate), animate ? 420 : 0);
}

function runFocusLayout({ animate = true } = {}) {
  const fid = focusedId.value;
  const focused = cy.$id(fid);
  if (!focused.length) return;
  const center = { x: cy.width() / 2, y: cy.height() / 2 };

  const root = cy.$id(ROOT_ID);
  if (root.length) placeNode(root, { x: center.x, y: center.y - 240 }, animate);

  placeNode(focused, center, animate);

  const children = cy
    .nodes()
    .filter(
      (n) =>
        !n.hasClass("is-hidden") &&
        !n.hasClass("is-ancestor") &&
        n.id() !== fid,
    );
  const count = children.length;
  const radius =
    count === 0
      ? 0
      : count === 1
        ? 180
        : Math.max(150, Math.min(320, 120 + count * 22));
  if (count === 1) {
    placeNode(children.first(), { x: center.x + radius, y: center.y }, animate);
  } else if (count > 1) {
    layoutRing(children, center, radius, animate, Math.PI / 2);
  }

  setTimeout(() => fitGraph(animate), animate ? 420 : 0);
}

function layoutRing(nodes, center, radius, animate, angleOffset = 0) {
  const ordered = nodes
    .toArray()
    .sort((a, b) =>
      String(a.data("label")).localeCompare(String(b.data("label"))),
    );
  ordered.forEach((node, i) => {
    const angle =
      angleOffset -
      Math.PI / 2 +
      (Math.PI * 2 * i) / Math.max(ordered.length, 1);
    placeNode(
      node,
      {
        x: center.x + Math.cos(angle) * radius,
        y: center.y + Math.sin(angle) * radius,
      },
      animate,
    );
  });
}

function placeNode(node, position, animate) {
  if (animate)
    node.animate({ position, duration: 360, easing: "ease-out-cubic" });
  else node.position(position);
}

// ── Zoom / fit ──────────────────────────────────────────────────────────────

function fitGraph(animate = false) {
  if (!cy || !hasGraph.value) return;
  const visible = cy.elements().filter((el) => !el.hasClass("is-hidden"));
  const padding = focusedId.value ? 100 : 80;
  if (animate) {
    cy.animate({
      fit: { eles: visible, padding },
      duration: 440,
      easing: "ease-out-cubic",
    });
  } else {
    cy.fit(visible, padding);
  }
}

function zoomIn() {
  cy?.zoom({
    level: cy.zoom() * 1.2,
    renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 },
  });
}
function zoomOut() {
  cy?.zoom({
    level: cy.zoom() * 0.82,
    renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 },
  });
}

// ── Focus navigation ────────────────────────────────────────────────────────

function focusNode(id) {
  focusedId.value = id;
  selectedNode.value = null;
  applyVisibility();
  runLayout({ animate: true });
}

function exitFocus() {
  focusedId.value = null;
  selectedNode.value = null;
  applyVisibility();
  runLayout({ animate: true });
}

// ── Node type label ─────────────────────────────────────────────────────────

function nodeTypeLabel(type) {
  return (
    {
      root: "Company",
      customer: "Customer",
      contract: "Contract",
      project: "Project",
      info: "Info",
      employee: "Employee",
    }[type] ?? type
  );
}

// ── Cytoscape init ──────────────────────────────────────────────────────────

const cytoscapeStyle = [
  {
    selector: "node",
    style: {
      width: "data(nw)",
      height: "data(nh)",
      shape: "data(shape)",
      "background-color": "data(color)",
      "background-opacity": 1,
      "border-width": 0,
      label: "data(label)",
      color: "#ffffff",
      "font-size": 12,
      "font-family": 'Georgia, Cambria, "Times New Roman", serif',
      "font-weight": 600,
      "text-wrap": "wrap",
      "text-max-width": 110,
      "text-halign": "center",
      "text-valign": "center",
      "overlay-padding": 8,
      "overlay-opacity": 0,
      "shadow-blur": 20,
      "shadow-color": "data(color)",
      "shadow-opacity": 0.28,
      "shadow-offset-x": 0,
      "shadow-offset-y": 4,
      "transition-property":
        "opacity, width, height, border-width, shadow-blur, shadow-opacity",
      "transition-duration": "200ms",
    },
  },
  {
    selector: "node[type = 'root']",
    style: {
      "font-size": 15,
      "font-weight": 700,
      "text-max-width": 132,
      "shadow-blur": 32,
      "shadow-opacity": 0.32,
    },
  },
  {
    selector: "node[type = 'customer']",
    style: { "font-size": 13, "font-weight": 700, "text-max-width": 120 },
  },
  {
    selector: "node[type = 'info']",
    style: { "font-size": 13, "font-weight": 700, "text-max-width": 100 },
  },
  {
    selector: "node[type = 'contract'], node[type = 'project']",
    style: {
      "font-size": 11,
      "text-max-width": 104,
      "shadow-blur": 14,
      "shadow-opacity": 0.22,
    },
  },
  {
    selector: "node[type = 'employee']",
    style: {
      "font-size": 11,
      "text-max-width": 100,
      "shadow-blur": 14,
      "shadow-opacity": 0.22,
    },
  },
  {
    selector: "node[type = 'project']",
    style: {
      "border-width": 2,
      "border-color": "rgba(255,255,255,0.4)",
      "border-style": "dashed",
    },
  },
  {
    selector: "node.hovered",
    style: { "shadow-blur": 30, "shadow-opacity": 0.48 },
  },
  {
    selector: "node:selected",
    style: {
      "border-width": 3,
      "border-color": "rgba(0,0,0,0.18)",
      "shadow-blur": 40,
      "shadow-opacity": 0.55,
    },
  },
  {
    selector: "node.is-ancestor",
    style: { opacity: 0.45 },
  },
  {
    selector: "node.is-hidden",
    style: { display: "none" },
  },
  {
    selector: "edge",
    style: {
      width: 1.5,
      "line-color": "#c8bfb0",
      opacity: 0.7,
      "curve-style": "bezier",
      "target-arrow-shape": "none",
      "line-cap": "round",
    },
  },
  {
    selector: "edge.is-hidden",
    style: { display: "none" },
  },
];

function initCytoscape() {
  if (cy) return;
  cy = cytoscape({
    container: canvasEl.value,
    elements: [],
    style: cytoscapeStyle,
    minZoom: 0.15,
    maxZoom: 3,
    wheelSensitivity: 0.22,
    boxSelectionEnabled: false,
    selectionType: "single",
  });

  let lastTapId = null;
  let lastTapAt = 0;

  cy.on("tap", "node", (event) => {
    const node = event.target;
    const nodeId = node.id();
    const nodeType = node.data("type");
    const now = Date.now();

    if (!node.hasClass("is-ancestor")) {
      selectedNode.value = {
        ...node.data("record"),
        type: nodeType,
        label: node.data("label"),
        color: node.data("color"),
      };
    }

    if (lastTapId === nodeId && now - lastTapAt < 320) {
      selectedNode.value = null;
      if (node.hasClass("is-ancestor") || nodeId === ROOT_ID) {
        exitFocus();
      } else if (nodeType === "customer" || nodeType === "info") {
        focusNode(nodeId);
      }
    }

    lastTapId = nodeId;
    lastTapAt = now;
  });

  cy.on("tap", (event) => {
    if (event.target === cy) {
      if (focusedId.value) exitFocus();
      else selectedNode.value = null;
    }
  });

  cy.on("mouseover", "node", (e) => e.target.addClass("hovered"));
  cy.on("mouseout", "node", (e) => e.target.removeClass("hovered"));

  resizeObserver = new ResizeObserver(() => {
    if (hasGraph.value) runLayout({ animate: false });
  });
  resizeObserver.observe(canvasEl.value);
}
</script>

<style scoped>
.graph-page {
  position: relative;
  height: 100%;
  min-height: 0;
  overflow: hidden;
  background: #fbfaf7;
}

.graph-canvas {
  width: 100%;
  height: 100%;
  display: block;
}

.graph-controls {
  position: absolute;
  bottom: 1rem;
  right: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  z-index: 5;
}

.ctrl-btn {
  width: 2rem;
  height: 2rem;
  border: 1px solid #ddd7cd;
  border-radius: 7px;
  background: rgba(255, 255, 255, 0.9);
  color: #746f67;
  font-size: 1rem;
  line-height: 1;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  backdrop-filter: blur(4px);
  transition:
    background 0.12s,
    color 0.12s;
}
.ctrl-btn:hover {
  background: #e7f2ef;
  color: #126b5f;
}

.graph-breadcrumb {
  position: absolute;
  top: 0.75rem;
  left: 0.75rem;
  z-index: 5;
}

.breadcrumb-btn {
  padding: 0.35rem 0.7rem;
  border: 1px solid #ddd7cd;
  border-radius: 7px;
  background: rgba(255, 255, 255, 0.9);
  color: #746f67;
  font: inherit;
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  backdrop-filter: blur(4px);
  transition:
    background 0.12s,
    color 0.12s;
}
.breadcrumb-btn:hover {
  background: #e7f2ef;
  color: #126b5f;
}

.graph-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(251, 250, 247, 0.82);
  z-index: 8;
}

.graph-state-card {
  background: #fff;
  border: 1px solid #ddd7cd;
  border-radius: 14px;
  padding: 1.5rem 2rem;
  text-align: center;
  max-width: 320px;
  box-shadow: 0 4px 24px rgba(24, 21, 17, 0.08);
}

.graph-state-kicker {
  font-size: 0.72rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #126b5f;
  margin: 0 0 0.4rem;
}

.graph-state-copy {
  font-size: 0.88rem;
  color: #746f67;
  margin: 0 0 0.75rem;
  line-height: 1.45;
}

.graph-state-btn {
  padding: 0.45rem 1rem;
  border: 1px solid #b7d2cc;
  border-radius: 8px;
  background: transparent;
  color: #126b5f;
  font: inherit;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.12s;
}
.graph-state-btn:hover {
  background: #e7f2ef;
}

.node-detail {
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

.node-detail-close {
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
.node-detail-close:hover {
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

/* Slide-in from right */
.detail-panel-enter-active,
.detail-panel-leave-active {
  transition: transform 0.22s ease, opacity 0.22s ease;
}
.detail-panel-enter-from,
.detail-panel-leave-to {
  transform: translateX(100%);
  opacity: 0;
}

.node-detail-row {
  font-size: 0.78rem;
  color: #746f67;
  margin: 0.15rem 0 0;
  line-height: 1.35;
}

.node-detail-fields {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin: 0.35rem 0 0;
}

.node-detail-field {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
}

.node-detail-field dt {
  font-size: 0.62rem;
  font-weight: 700;
  color: #9a9388;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.node-detail-field dd {
  font-size: 0.78rem;
  color: #181511;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 0.3rem;
}

.node-review-chip {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 0.1rem 0.4rem;
  border-radius: 5px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}
.node-review-chip--approved { background: #d1fae5; color: #065f46; }
.node-review-chip--approved_by_extraction { background: #dcfce7; color: #166534; }
.node-review-chip--pending_review { background: #fef3c7; color: #92400e; }
.node-review-chip--rejected { background: #fee2e2; color: #991b1b; }
.node-review-chip--duplicate_deleted { background: #f3f4f6; color: #374151; }
.node-review-chip--failed { background: #fee2e2; color: #991b1b; }

.node-detail-section {
  font-size: 0.62rem;
  font-weight: 700;
  color: #9a9388;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin: 0 0 0.25rem;
}

.node-detail-risks,
.node-detail-people {
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid #f0ede6;
}

.node-risk-list,
.node-people-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.node-risk-list li {
  font-size: 0.72rem;
  color: #92400e;
  background: #fef3c7;
  border-radius: 4px;
  padding: 0.15rem 0.4rem;
}

.node-people-list li {
  font-size: 0.72rem;
  color: #181511;
  background: #fbfaf7;
  border: 1px solid #ddd7cd;
  border-radius: 4px;
  padding: 0.15rem 0.4rem;
}

.node-detail-link {
  display: block;
  margin-top: 0.65rem;
  padding-top: 0.55rem;
  border: none;
  border-top: 1px solid #f0ede6;
  background: none;
  font-size: 0.78rem;
  font-weight: 600;
  color: #126b5f;
  text-decoration: none;
  cursor: pointer;
  transition: opacity 0.12s;
}
.node-detail-link:hover {
  opacity: 0.75;
  text-decoration: underline;
}

.timereport-section {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px solid #f0ede6;
}

.timereport-state {
  font-size: 0.8rem;
  color: #9a9388;
}
.timereport-state--empty {
  font-style: italic;
}

.timereport-months {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.timereport-month {
  border: 1px solid #ede9e2;
  border-radius: 8px;
  overflow: hidden;
}

.month-header {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.5rem 0.6rem;
  border: 0;
  background: #fbfaf7;
  cursor: pointer;
  text-align: left;
  transition: background 0.1s;
}
.month-header:hover {
  background: #f3f0ea;
}

.month-label {
  flex: 1;
  font-size: 0.8rem;
  font-weight: 700;
  color: #181511;
}

.month-hours {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--node-accent);
}

.month-chevron {
  font-size: 1rem;
  color: #9a9388;
  line-height: 1;
  transition: transform 0.15s ease;
  display: inline-block;
}
.month-chevron.open {
  transform: rotate(90deg);
}

.month-entries {
  display: flex;
  flex-direction: column;
  gap: 0;
  border-top: 1px solid #ede9e2;
}

.month-entry {
  padding: 0.5rem 0.6rem;
  border-bottom: 1px solid #f5f2ec;
}
.month-entry:last-child {
  border-bottom: 0;
}

.entry-meta {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 0.5rem;
  margin-bottom: 0.2rem;
}

.entry-date {
  font-size: 0.72rem;
  color: #9a9388;
}

.entry-hours {
  font-size: 0.75rem;
  font-weight: 700;
  color: #181511;
  white-space: nowrap;
}

.entry-note {
  font-size: 0.78rem;
  color: #4a4540;
  margin: 0;
  line-height: 1.4;
}
</style>
