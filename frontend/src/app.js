/* HQ Agent — Application Shell */
(function () {
  "use strict";

  /* ---- Auth ------------------------------------------------- */
  var appLayout = document.getElementById("appLayout");
  var authGate = document.getElementById("authGate");
  var loginBtn = document.getElementById("loginBtn");
  var authError = document.getElementById("authError");
  var signOutBtn = document.getElementById("signOutBtn");
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
  }

  function showLogin(errorCode) {
    appLayout.hidden = true;
    authGate.hidden = false;
    if (errorCode) {
      authError.textContent =
        AUTH_ERRORS[errorCode] || "Sign-in failed. Please try again.";
      authError.hidden = false;
    } else {
      authError.hidden = true;
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
