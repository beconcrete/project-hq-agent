/* HQ Agent — Application Shell */
(function () {
  "use strict";

  const sidebar = document.getElementById("sidebar");
  const sidebarToggle = document.getElementById("sidebarToggle");
  const topbarMenuBtn = document.getElementById("topbarMenuBtn");
  const sidebarOverlay = document.getElementById("sidebarOverlay");

  // Persist sidebar state
  const SIDEBAR_KEY = "hq-sidebar-state";

  function isMobile() {
    return window.innerWidth <= 640;
  }

  function isTablet() {
    return window.innerWidth > 640 && window.innerWidth <= 1024;
  }

  function initSidebar() {
    if (isMobile()) return;
    const saved = localStorage.getItem(SIDEBAR_KEY);
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

  // Re-evaluate on resize
  let resizeTimer;
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

  initSidebar();
})();
