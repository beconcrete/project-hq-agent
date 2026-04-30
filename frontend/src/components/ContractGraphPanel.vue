<template>
  <div class="accordion accordion--graph" :class="{ open }">
    <button class="accordion-trigger" type="button" @click="toggle">
      <span class="accordion-label">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          width="18"
          height="18"
        >
          <circle cx="12" cy="4" r="2" />
          <circle cx="4" cy="20" r="2" />
          <circle cx="20" cy="20" r="2" />
          <path d="M12 6v4M12 10 6 18M12 10l6 8" />
        </svg>
        Contract Graph
        <span v-if="contractCount > 0" class="badge-count">{{
          contractCount
        }}</span>
      </span>
      <svg
        class="chevron"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
      >
        <path d="m6 9 6 6 6-6" />
      </svg>
    </button>

    <div class="accordion-panel">
      <div class="accordion-panel-inner graph-panel-inner">
        <div ref="graphBodyEl" class="graph-body">
          <div ref="canvasEl" class="graph-canvas" />

          <div v-if="hasGraph" class="graph-controls">
            <button
              class="ctrl-btn"
              title="Fit to screen"
              @click="fitGraph(true)"
            >
              ⊞
            </button>
            <button class="ctrl-btn" title="Zoom in" @click="zoomIn">+</button>
            <button class="ctrl-btn" title="Zoom out" @click="zoomOut">
              −
            </button>
          </div>

          <div v-if="focusedPartyId" class="graph-breadcrumb">
            <button class="breadcrumb-btn" type="button" @click="exitFocus">
              ← All parties
            </button>
          </div>

          <div v-if="loading" class="graph-overlay">
            <div class="graph-state-card">
              <p class="graph-state-kicker">Loading graph</p>
              <p class="graph-state-copy">
                Collecting contracts and counterparties…
              </p>
            </div>
          </div>
          <div v-else-if="error" class="graph-overlay">
            <div class="graph-state-card">
              <p class="graph-state-kicker">Graph unavailable</p>
              <p class="graph-state-copy">{{ error }}</p>
              <button class="graph-state-btn" type="button" @click="retryFetch">
                Try again
              </button>
            </div>
          </div>
          <div v-else-if="initialized && !hasGraph" class="graph-overlay">
            <div class="graph-state-card">
              <p class="graph-state-kicker">No contracts yet</p>
              <p class="graph-state-copy">
                Upload a contract to see the relationship map.
              </p>
            </div>
          </div>

          <ContractNodeDetail
            :node="selectedNode"
            :accent-color="selectedAccent"
            @close="selectedNode = null"
            @assign-party="openAssignModal"
            @contract-deleted="onContractDeleted"
          />
        </div>
      </div>
    </div>

    <AssignPartyModal
      v-if="assignModalOpen"
      :contract="assignModalContract"
      :known-parties="knownParties"
      @assigned="onPartyAssigned"
      @cancel="assignModalOpen = false"
    />
  </div>
</template>

<script setup>
import { ref, computed, watch, nextTick, onUnmounted } from "vue";
import cytoscape from "cytoscape";
import ContractNodeDetail from "./ContractNodeDetail.vue";
import AssignPartyModal from "./AssignPartyModal.vue";
import { useContractGraph } from "../composables/useContractGraph";

const emit = defineEmits(["contract-deleted"]);

const { graph, loading, error, fetch } = useContractGraph();

const open = ref(false);
const canvasEl = ref(null);
const graphBodyEl = ref(null);
const selectedNode = ref(null);
const focusedPartyId = ref(null);
const initialized = ref(false);
const fetched = ref(false);
const assignModalOpen = ref(false);
const assignModalContract = ref(null);

let cy = null;
let resizeObserver = null;

const ROOT_ID = "__root__";

const PARTY_PALETTE = [
  "#c17a2b",
  "#2c7ed6",
  "#b5487c",
  "#6b5dd3",
  "#d95f43",
  "#1d9a73",
  "#9b7a20",
  "#c05e8c",
];

// ─── Computed ────────────────────────────────────────────────────────────────

const hasGraph = computed(() => graph.value.nodes.length > 0);

const contractCount = computed(
  () => graph.value.nodes.filter((n) => n.type === "contract").length,
);

const selectedAccent = computed(() => selectedNode.value?.color ?? "#126b5f");

const knownParties = computed(() =>
  graph.value.nodes
    .filter((n) => n.type === "party" && n.id !== "party:__unknown__")
    .map((n) => n.label),
);

// ─── Accordion toggle ─────────────────────────────────────────────────────────

async function toggle() {
  open.value = !open.value;
  if (open.value && !fetched.value) {
    fetched.value = true;
    await nextTick();
    initCytoscape();
    await fetch();
  } else if (open.value && cy) {
    await nextTick();
    fitGraph(false);
  }
}

// ─── Public API ───────────────────────────────────────────────────────────────

async function reloadGraph() {
  selectedNode.value = null;
  focusedPartyId.value = null;
  await fetch();
}

defineExpose({ reloadGraph });

// ─── Graph sync ───────────────────────────────────────────────────────────────

watch(
  () => graph.value,
  () => {
    if (cy) syncGraphElements();
  },
  { deep: true },
);

function syncGraphElements() {
  cy.elements().remove();
  if (!hasGraph.value) {
    initialized.value = true;
    return;
  }
  const elements = buildElements();
  cy.add(elements);
  applyVisibility();
  runLayout({ animate: false });
  initialized.value = true;
}

// ─── Element builder ──────────────────────────────────────────────────────────

function buildElements() {
  const elements = [];
  const contractsByParty = {};

  // Count contracts per party
  for (const node of graph.value.nodes) {
    if (node.type !== "contract") continue;
    const partyId = node.partyId ?? "party:__unknown__";
    contractsByParty[partyId] = (contractsByParty[partyId] ?? 0) + 1;
  }

  for (const node of graph.value.nodes) {
    if (node.type === "root") {
      elements.push({
        data: {
          id: ROOT_ID,
          type: "root",
          label: node.label,
          color: "#126b5f",
          size: 96,
          shape: "ellipse",
          record: node,
        },
      });
    } else if (node.type === "party") {
      const color =
        node.id === "party:__unknown__" ? "#6b7280" : colorForParty(node.label);
      elements.push({
        data: {
          id: node.id,
          type: "party",
          label: node.label,
          color,
          size: 72,
          shape: "round-rectangle",
          record: { ...node, contractCount: contractsByParty[node.id] ?? 0 },
        },
      });
    } else if (node.type === "contract") {
      const partyId = node.partyId ?? "party:__unknown__";
      const partyNode = graph.value.nodes.find((n) => n.id === partyId);
      const partyColor =
        partyNode?.id === "party:__unknown__"
          ? "#6b7280"
          : colorForParty(partyNode?.label ?? "");
      elements.push({
        data: {
          id: node.id,
          type: "contract",
          label: truncateLabel(
            node.label || node.documentType || node.fileName || "Contract",
          ),
          color: partyColor,
          size: 48,
          shape: "rectangle",
          parentPartyId: partyId,
          record: node,
        },
      });
    }
  }

  for (const edge of graph.value.edges) {
    const sourceId = edge.source === "__root__" ? ROOT_ID : edge.source;
    elements.push({
      data: {
        id: edge.id,
        source: sourceId,
        target: edge.target,
        color: colorForEdge(edge.target),
      },
    });
  }

  return elements;
}

function colorForParty(name) {
  if (!name) return "#6b7280";
  let hash = 0;
  for (let i = 0; i < name.length; i++) {
    hash = (hash * 31 + name.charCodeAt(i)) >>> 0;
  }
  return PARTY_PALETTE[hash % PARTY_PALETTE.length];
}

function colorForEdge(targetId) {
  if (!cy) return "#5f7d99";
  const targetNode = cy.$id(targetId);
  return targetNode.length
    ? (targetNode.data("color") ?? "#5f7d99")
    : "#5f7d99";
}

function truncateLabel(label) {
  if (!label) return "";
  return label.length > 32 ? `${label.slice(0, 29)}…` : label;
}

// ─── Visibility ───────────────────────────────────────────────────────────────

function applyVisibility() {
  if (!cy) return;
  cy.elements().removeClass("is-hidden is-ancestor is-focused");

  if (!focusedPartyId.value) {
    // Overview: show root + party nodes only; hide contracts
    cy.nodes().forEach((node) => {
      if (node.data("type") === "contract") node.addClass("is-hidden");
    });
    cy.edges().forEach((edge) => {
      const target = edge.target();
      if (target.data("type") === "contract") edge.addClass("is-hidden");
    });
    return;
  }

  // Focus mode: show focused party + its contracts + root (as ancestor)
  const focusedId = focusedPartyId.value;
  cy.nodes().forEach((node) => {
    const id = node.id();
    const type = node.data("type");
    if (id === ROOT_ID) {
      node.addClass("is-ancestor");
    } else if (id === focusedId) {
      node.addClass("is-focused");
    } else if (
      type === "contract" &&
      node.data("parentPartyId") === focusedId
    ) {
      // visible, no class
    } else {
      node.addClass("is-hidden");
    }
  });
  cy.edges().forEach((edge) => {
    const src = edge.source().id();
    const tgt = edge.target().id();
    const tgtType = edge.target().data("type");
    const srcType = edge.source().data("type");
    const isRootToFocused = src === ROOT_ID && tgt === focusedId;
    const isFocusedToContract =
      src === focusedId &&
      tgtType === "contract" &&
      edge.target().data("parentPartyId") === focusedId;
    const isRootToParty = srcType === "root" && tgtType === "party";
    if (!isRootToFocused && !isFocusedToContract) {
      if (!isRootToParty || tgt !== focusedId) edge.addClass("is-hidden");
    }
  });
}

// ─── Layout ───────────────────────────────────────────────────────────────────

function runLayout({ animate = true } = {}) {
  if (!cy || !hasGraph.value) return;
  focusedPartyId.value
    ? runFocusLayout({ animate })
    : runOverviewLayout({ animate });
}

function runOverviewLayout({ animate = true } = {}) {
  const root = cy.$id(ROOT_ID);
  if (!root.length) return;
  const center = { x: cy.width() / 2, y: cy.height() / 2 };
  placeNode(root, center, animate);

  const partyNodes = cy
    .nodes()
    .filter((n) => !n.hasClass("is-hidden") && n.id() !== ROOT_ID);
  const radius = Math.max(160, Math.min(320, 120 + partyNodes.length * 28));
  layoutRing(partyNodes, center, radius, animate);

  setTimeout(() => fitGraph(animate), animate ? 420 : 0);
}

function runFocusLayout({ animate = true } = {}) {
  const focusedNode = cy.$id(focusedPartyId.value);
  if (!focusedNode.length) return;
  const center = { x: cy.width() / 2, y: cy.height() / 2 };

  // Place root above
  const rootNode = cy.$id(ROOT_ID);
  if (rootNode.length)
    placeNode(rootNode, { x: center.x, y: center.y - 240 }, animate);

  // Place focused party at center
  placeNode(focusedNode, center, animate);

  // Place contracts in a ring
  const contractNodes = cy
    .nodes()
    .filter(
      (n) =>
        n.data("type") === "contract" &&
        n.data("parentPartyId") === focusedPartyId.value &&
        !n.hasClass("is-hidden"),
    );
  const count = contractNodes.length;
  const radius =
    count === 0
      ? 0
      : count === 1
        ? 180
        : Math.max(150, Math.min(300, 120 + count * 22));

  if (count === 1) {
    placeNode(
      contractNodes.first(),
      { x: center.x + radius, y: center.y },
      animate,
    );
  } else if (count > 1) {
    layoutRing(contractNodes, center, radius, animate, Math.PI / 2);
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
  if (animate) {
    node.animate({ position, duration: 360, easing: "ease-out-cubic" });
  } else {
    node.position(position);
  }
}

// ─── Zoom / fit ───────────────────────────────────────────────────────────────

function fitGraph(animate = false) {
  if (!cy || !hasGraph.value) return;
  const visible = cy.elements().filter((el) => !el.hasClass("is-hidden"));
  const padding = focusedPartyId.value ? 100 : 80;
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

// ─── Focus navigation ─────────────────────────────────────────────────────────

function focusParty(partyId) {
  focusedPartyId.value = partyId;
  selectedNode.value = null;
  applyVisibility();
  runLayout({ animate: true });
}

function exitFocus() {
  focusedPartyId.value = null;
  selectedNode.value = null;
  applyVisibility();
  runLayout({ animate: true });
}

// ─── Actions ──────────────────────────────────────────────────────────────────

function openAssignModal(node) {
  assignModalContract.value = node.data;
  assignModalOpen.value = true;
}

async function onPartyAssigned() {
  assignModalOpen.value = false;
  selectedNode.value = null;
  await reloadGraph();
}

async function onContractDeleted(rowKey) {
  selectedNode.value = null;
  emit("contract-deleted", rowKey);
  await reloadGraph();
}

async function retryFetch() {
  await fetch();
}

// ─── Cytoscape init ───────────────────────────────────────────────────────────

const cytoscapeStyle = [
  {
    selector: "node",
    style: {
      width: "data(size)",
      height: "data(size)",
      shape: "data(shape)",
      "background-color": "data(color)",
      "background-opacity": 0.92,
      "border-width": 1.5,
      "border-color": "#f1f5f9",
      "border-style": "solid",
      label: "data(label)",
      color: "#f1f5f9",
      "font-size": 10,
      "font-family": 'Georgia, Cambria, "Times New Roman", serif',
      "font-weight": 600,
      "text-wrap": "wrap",
      "text-max-width": 88,
      "text-halign": "center",
      "text-valign": "bottom",
      "text-margin-y": 8,
      "overlay-padding": 8,
      "overlay-opacity": 0,
      "shadow-blur": 24,
      "shadow-color": "data(color)",
      "shadow-opacity": 0.38,
      "shadow-offset-x": 0,
      "shadow-offset-y": 0,
      "transition-property":
        "opacity, width, height, border-width, shadow-blur, shadow-opacity",
      "transition-duration": "200ms",
    },
  },
  {
    selector: "node[type = 'root']",
    style: {
      "font-size": 13,
      "font-weight": 700,
      "text-valign": "center",
      "text-margin-y": 0,
      "border-width": 3,
      "border-color": "rgba(241,245,249,0.75)",
      "shadow-blur": 36,
      "shadow-opacity": 0.6,
    },
  },
  {
    selector: "node[type = 'party']",
    style: {
      "font-size": 11,
      "font-weight": 700,
      "text-valign": "center",
      "text-margin-y": 0,
      "border-width": 2,
      "shadow-blur": 28,
      "shadow-opacity": 0.5,
    },
  },
  {
    selector: "node[type = 'contract']",
    style: {
      "font-size": 9,
      "text-max-width": 72,
      "shadow-blur": 16,
      "shadow-opacity": 0.28,
      "background-opacity": 0.72,
    },
  },
  {
    selector: "node.hovered",
    style: {
      "border-width": 3,
      "shadow-blur": 32,
      "shadow-opacity": 0.62,
    },
  },
  {
    selector: "node:selected",
    style: {
      "border-width": 4,
      "border-color": "#ffffff",
      "shadow-blur": 44,
      "shadow-opacity": 0.76,
    },
  },
  {
    selector: "node.is-ancestor",
    style: {
      opacity: 0.55,
      "border-style": "dashed",
    },
  },
  {
    selector: "node.is-hidden",
    style: { display: "none" },
  },
  {
    selector: "edge",
    style: {
      width: 1.8,
      "line-color": "data(color)",
      opacity: 0.35,
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
    minZoom: 0.2,
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

    // Single tap — select
    if (!node.hasClass("is-ancestor")) {
      selectedNode.value = {
        type: nodeType,
        color: node.data("color"),
        data: node.data("record"),
      };
    }

    // Double tap — navigate
    if (lastTapId === nodeId && now - lastTapAt < 320) {
      selectedNode.value = null;
      if (node.hasClass("is-ancestor") || nodeId === ROOT_ID) {
        exitFocus();
      } else if (nodeType === "party") {
        focusParty(nodeId);
      }
    }

    lastTapId = nodeId;
    lastTapAt = now;
  });

  cy.on("tap", (event) => {
    if (event.target === cy) {
      if (focusedPartyId.value) {
        exitFocus();
      } else {
        selectedNode.value = null;
      }
    }
  });

  cy.on("mouseover", "node", (e) => e.target.addClass("hovered"));
  cy.on("mouseout", "node", (e) => e.target.removeClass("hovered"));

  resizeObserver = new ResizeObserver(() => {
    if (hasGraph.value && open.value) runLayout({ animate: false });
  });
  resizeObserver.observe(canvasEl.value);
}

onUnmounted(() => {
  resizeObserver?.disconnect();
  cy?.destroy();
  cy = null;
});
</script>

<style scoped>
.accordion--graph {
  grid-column: 1 / -1;
}

.graph-panel-inner {
  padding: 0 !important;
}

.graph-body {
  position: relative;
  overflow: hidden;
}

.graph-canvas {
  width: 100%;
  height: 480px;
  background: #0d1b2a;
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
  border: 1px solid rgba(241, 245, 249, 0.18);
  border-radius: 7px;
  background: rgba(13, 27, 42, 0.82);
  color: #c8d8e8;
  font-size: 1rem;
  line-height: 1;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  backdrop-filter: blur(4px);
  transition: background 0.12s;
}
.ctrl-btn:hover {
  background: rgba(18, 107, 95, 0.7);
}

.graph-breadcrumb {
  position: absolute;
  top: 0.75rem;
  left: 0.75rem;
  z-index: 5;
}

.breadcrumb-btn {
  padding: 0.35rem 0.7rem;
  border: 1px solid rgba(241, 245, 249, 0.18);
  border-radius: 7px;
  background: rgba(13, 27, 42, 0.82);
  color: #c8d8e8;
  font: inherit;
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  backdrop-filter: blur(4px);
  transition: background 0.12s;
}
.breadcrumb-btn:hover {
  background: rgba(18, 107, 95, 0.7);
}

.graph-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(13, 27, 42, 0.72);
  z-index: 8;
}

.graph-state-card {
  background: rgba(18, 28, 40, 0.95);
  border: 1px solid rgba(241, 245, 249, 0.12);
  border-radius: 14px;
  padding: 1.5rem 2rem;
  text-align: center;
  max-width: 320px;
}

.graph-state-kicker {
  font-size: 0.72rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #6baa9f;
  margin: 0 0 0.4rem;
}

.graph-state-copy {
  font-size: 0.88rem;
  color: #94a3b8;
  margin: 0 0 0.75rem;
  line-height: 1.45;
}

.graph-state-btn {
  padding: 0.45rem 1rem;
  border: 1px solid rgba(18, 107, 95, 0.6);
  border-radius: 8px;
  background: transparent;
  color: #6baa9f;
  font: inherit;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  transition: background 0.12s;
}
.graph-state-btn:hover {
  background: rgba(18, 107, 95, 0.18);
}
</style>
