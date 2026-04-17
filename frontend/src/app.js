/* HQ Agent — Application Shell */
(function () {
  "use strict";

  /* ---- Auth ------------------------------------------------- */
  var appLayout = document.getElementById("appLayout");
  var authGate = document.getElementById("authGate");
  var loginBtn = document.getElementById("loginBtn");
  var authError = document.getElementById("authError");
  var signOutBtn = document.getElementById("signOutBtn");
  var authSignOutBtn = document.getElementById("authSignOutBtn");
  var topbarUserName = document.getElementById("topbarUserName");

  var AUTH_ERRORS = {
    blocked: "Your account has been suspended. Contact your administrator.",
    "no-access":
      "You don't have access to HQ Agent. Contact your administrator.",
    "service-error": "Authentication service is unavailable. Try again later.",
  };

  function showApp(user) {
    authGate.hidden = true;
    appLayout.hidden = false;
    if (topbarUserName && user && user.displayName) {
      topbarUserName.textContent = user.displayName;
    }
    initSidebar();
    initContracts();
  }

  function showLogin(errorCode) {
    appLayout.hidden = true;
    authGate.hidden = false;
    if (errorCode) {
      authError.textContent =
        AUTH_ERRORS[errorCode] || "Sign-in failed. Please try again.";
      authError.hidden = false;
      loginBtn.hidden = true;
      authSignOutBtn.hidden = false;
    } else {
      authError.hidden = true;
      loginBtn.hidden = false;
      authSignOutBtn.hidden = true;
    }
  }

  if (loginBtn) {
    loginBtn.addEventListener("click", function () {
      HQAuth.login();
    });
  }

  if (signOutBtn) {
    signOutBtn.addEventListener("click", function () {
      HQAuth.logout();
    });
  }

  if (authSignOutBtn) {
    authSignOutBtn.addEventListener("click", function () {
      HQAuth.logout();
    });
  }

  HQAuth.init()
    .then(function (user) {
      if (user) {
        showApp(user);
      } else {
        showLogin(null);
      }
    })
    .catch(function (err) {
      showLogin(err.code || "service-error");
    });

  /* ---- Contracts module ------------------------------------- */
  var contracts = []; // in-session contract list

  var contractsUpload = document.getElementById("contractsUpload");
  var contractsDropzone = document.getElementById("contractsDropzone");
  var contractsGrid = document.getElementById("contractsGrid");
  var contractsEmpty = document.getElementById("contractsEmpty");
  var contractsEmptyTitle = document.getElementById("contractsEmptyTitle");
  var contractsEmptyText = document.getElementById("contractsEmptyText");
  var contractFileInput = document.getElementById("contractFileInput");
  var uploadProgress = document.getElementById("uploadProgress");
  var uploadProgressLabel = document.getElementById("uploadProgressLabel");
  var uploadError = document.getElementById("uploadError");
  var uploadErrorText = document.getElementById("uploadErrorText");
  var uploadRetryBtn = document.getElementById("uploadRetryBtn");

  function initContracts() {
    var isAdmin = HQAuth.hasRole("admin");

    if (isAdmin) {
      contractsUpload.hidden = false;
      contractsEmptyTitle.textContent = "No contracts yet";
      contractsEmptyText.textContent =
        "Drop a PDF or DOCX above to get started.";
    } else {
      contractsEmptyText.textContent = "No contracts have been uploaded yet.";
    }

    renderContractList();
  }

  function renderContractList() {
    if (contracts.length === 0) {
      contractsGrid.hidden = true;
      contractsEmpty.hidden = false;
      return;
    }
    contractsEmpty.hidden = true;
    contractsGrid.hidden = false;
    contractsGrid.innerHTML = contracts
      .map(function (c) {
        return (
          '<div class="contract-card">' +
          '<div class="contract-card-header">' +
          '<div class="contract-card-icon">' +
          '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">' +
          '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>' +
          '<polyline points="14 2 14 8 20 8"/></svg></div>' +
          '<div class="contract-card-info">' +
          '<div class="contract-card-name">' +
          escapeHtml(c.fileName) +
          "</div>" +
          '<div class="contract-card-meta">Uploaded just now</div>' +
          "</div>" +
          '<span class="badge badge-processing"><span class="badge-dot"></span>Processing</span>' +
          "</div>" +
          "</div>"
        );
      })
      .join("");
  }

  function escapeHtml(str) {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function setUploadState(state, message) {
    uploadProgress.hidden = state !== "uploading";
    uploadError.hidden = state !== "error";
    contractsDropzone.classList.toggle(
      "dropzone-uploading",
      state === "uploading",
    );
    if (state === "uploading" && message)
      uploadProgressLabel.textContent = message;
    if (state === "error" && message) uploadErrorText.textContent = message;
  }

  function uploadFile(file) {
    // Client-side validation
    var ext = file.name.split(".").pop().toLowerCase();
    if (ext !== "pdf" && ext !== "docx") {
      setUploadState("error", "Only PDF and DOCX files are accepted.");
      return;
    }
    if (file.size > 20 * 1024 * 1024) {
      setUploadState("error", "File exceeds the 20 MB size limit.");
      return;
    }

    setUploadState("uploading", "Uploading " + file.name + "\u2026");

    var formData = new FormData();
    formData.append("file", file);

    fetch("/api/upload-contract", {
      method: "POST",
      headers: { "X-Auth-Token": "Bearer " + HQAuth.getToken() },
      body: formData,
    })
      .then(function (res) {
        return res.json().then(function (body) {
          return { ok: res.ok, status: res.status, body: body };
        });
      })
      .then(function (result) {
        if (!result.ok) {
          var msg =
            result.status === 403
              ? "You do not have permission to upload contracts."
              : result.body.error || "Upload failed. Please try again.";
          setUploadState("error", msg);
          return;
        }
        setUploadState("idle");
        contracts.unshift({
          correlationId: result.body.correlationId,
          blobName: result.body.blobName,
          fileName: result.body.fileName,
          status: "processing",
        });
        renderContractList();
      })
      .catch(function () {
        setUploadState("error", "Network error. Please try again.");
      });
  }

  // Dropzone — click to browse
  if (contractsDropzone) {
    contractsDropzone.addEventListener("click", function () {
      if (contractFileInput) contractFileInput.click();
    });
  }

  if (contractFileInput) {
    contractFileInput.addEventListener("change", function () {
      if (contractFileInput.files && contractFileInput.files.length > 0) {
        Array.prototype.forEach.call(contractFileInput.files, uploadFile);
        contractFileInput.value = "";
      }
    });
  }

  // Dropzone — drag and drop
  if (contractsDropzone) {
    contractsDropzone.addEventListener("dragover", function (e) {
      e.preventDefault();
      contractsDropzone.classList.add("drag-active");
    });

    contractsDropzone.addEventListener("dragleave", function () {
      contractsDropzone.classList.remove("drag-active");
    });

    contractsDropzone.addEventListener("drop", function (e) {
      e.preventDefault();
      contractsDropzone.classList.remove("drag-active");
      var files = e.dataTransfer && e.dataTransfer.files;
      if (files && files.length > 0) {
        Array.prototype.forEach.call(files, uploadFile);
      }
    });
  }

  // Retry button
  if (uploadRetryBtn) {
    uploadRetryBtn.addEventListener("click", function () {
      setUploadState("idle");
      if (contractFileInput) contractFileInput.click();
    });
  }

  /* ---- Module switching ------------------------------------- */
  var MODULE_LABELS = { contracts: "Contracts", "auth-test": "Auth Test" };

  function switchModule(name) {
    document.querySelectorAll(".module[data-module]").forEach(function (el) {
      el.hidden = el.dataset.module !== name;
    });
    document.querySelectorAll(".nav-item[data-module]").forEach(function (el) {
      el.classList.toggle("active", el.dataset.module === name);
    });
    if (breadcrumbCurrent) {
      breadcrumbCurrent.textContent = MODULE_LABELS[name] || name;
    }
  }

  document
    .querySelectorAll(".nav-item[data-module]:not(.coming-soon) .nav-link")
    .forEach(function (link) {
      link.addEventListener("click", function (e) {
        e.preventDefault();
        var module = link.closest(".nav-item").dataset.module;
        switchModule(module);
        if (isMobile()) closeMobileSidebar();
      });
    });

  /* ---- Auth Test -------------------------------------------- */
  var authTestResult = document.getElementById("authTestResult");

  function runAuthTest(action) {
    authTestResult.hidden = false;
    authTestResult.className = "auth-test-result auth-test-loading";
    authTestResult.textContent = "Checking…";

    fetch("/api/auth-test?action=" + action, {
      headers: { "X-Auth-Token": "Bearer " + HQAuth.getToken() },
    })
      .then(function (res) {
        return res.json().then(function (body) {
          return { ok: res.ok, body: body };
        });
      })
      .then(function (result) {
        authTestResult.className =
          "auth-test-result " +
          (result.ok ? "auth-test-allowed" : "auth-test-denied");
        authTestResult.textContent = result.ok
          ? "Allowed\nRoles: " + (result.body.roles || []).join(", ")
          : "Not allowed\nRoles: " + (result.body.roles || []).join(", ");
      })
      .catch(function () {
        authTestResult.className = "auth-test-result auth-test-error";
        authTestResult.textContent = "Error: could not reach auth service";
      });
  }

  var authTestUserBtn = document.getElementById("authTestUserBtn");
  var authTestAdminBtn = document.getElementById("authTestAdminBtn");

  if (authTestUserBtn) {
    authTestUserBtn.addEventListener("click", function () {
      runAuthTest("user");
    });
  }
  if (authTestAdminBtn) {
    authTestAdminBtn.addEventListener("click", function () {
      runAuthTest("admin");
    });
  }

  /* ---- Sidebar ---------------------------------------------- */
  var sidebar = document.getElementById("sidebar");
  var sidebarToggle = document.getElementById("sidebarToggle");
  var topbarMenuBtn = document.getElementById("topbarMenuBtn");
  var sidebarOverlay = document.getElementById("sidebarOverlay");

  var SIDEBAR_KEY = "hq-sidebar-state";

  function isMobile() {
    return window.innerWidth <= 640;
  }

  function isTablet() {
    return window.innerWidth > 640 && window.innerWidth <= 1024;
  }

  function initSidebar() {
    if (isMobile()) return;
    var saved = localStorage.getItem(SIDEBAR_KEY);
    if (isTablet()) {
      sidebar.classList.toggle("expanded", saved === "expanded");
    } else {
      sidebar.classList.toggle("collapsed", saved === "collapsed");
    }
  }

  function toggleDesktopSidebar() {
    if (isTablet()) {
      sidebar.classList.toggle("expanded");
      localStorage.setItem(
        SIDEBAR_KEY,
        sidebar.classList.contains("expanded") ? "expanded" : "collapsed",
      );
    } else {
      sidebar.classList.toggle("collapsed");
      localStorage.setItem(
        SIDEBAR_KEY,
        sidebar.classList.contains("collapsed") ? "collapsed" : "expanded",
      );
    }
  }

  function openMobileSidebar() {
    sidebar.classList.add("open");
    document.body.style.overflow = "hidden";
  }

  function closeMobileSidebar() {
    sidebar.classList.remove("open");
    document.body.style.overflow = "";
  }

  if (sidebarToggle) {
    sidebarToggle.addEventListener("click", toggleDesktopSidebar);
  }

  if (topbarMenuBtn) {
    topbarMenuBtn.addEventListener("click", openMobileSidebar);
  }

  if (sidebarOverlay) {
    sidebarOverlay.addEventListener("click", closeMobileSidebar);
  }

  var resizeTimer;
  window.addEventListener("resize", function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      if (!isMobile()) {
        closeMobileSidebar();
        initSidebar();
      }
    }, 100);
  });

  // Disable coming-soon links
  document
    .querySelectorAll(".nav-item.coming-soon .nav-link")
    .forEach(function (link) {
      link.addEventListener("click", function (e) {
        e.preventDefault();
      });
    });
})();
