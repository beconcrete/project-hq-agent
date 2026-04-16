/* HQ Agent — Auth module
   Auth0 handles sign-in; usermgmt (usermanagement.beconcrete.se) gates app access.

   Token is kept in memory only — never written to localStorage or sessionStorage.
   Config (Auth0 domain + clientId) is fetched from /api/config at startup.
*/
(function (global) {
  "use strict";

  var USERMGMT_URL = "https://usermanagement.beconcrete.se/api/v1/me";
  var APP_ID = "hqagents";

  var _auth0 = null;
  var _idToken = null; // in-memory only
  var _userInfo = null;

  function _loadConfig() {
    return fetch("/api/config").then(function (res) {
      if (!res.ok) throw new Error("Failed to load app config");
      return res.json();
    });
  }

  function _verifyAccess(token) {
    return fetch(USERMGMT_URL, {
      headers: { "X-Auth-Token": "Bearer " + token },
    })
      .then(function (res) {
        if (res.status === 403) {
          var err = new Error("blocked");
          err.code = "blocked";
          throw err;
        }
        if (!res.ok) {
          var err = new Error("Auth service error");
          err.code = "service-error";
          throw err;
        }
        return res.json();
      })
      .then(function (me) {
        if (!me.apps || me.apps.indexOf(APP_ID) === -1) {
          var err = new Error("no-access");
          err.code = "no-access";
          throw err;
        }
        return me; // { userId, status, apps }
      });
  }

  function init() {
    return _loadConfig()
      .then(function (config) {
        return window.auth0.createAuth0Client({
          domain: config.auth0Domain,
          clientId: config.auth0ClientId,
          authorizationParams: {
            redirect_uri: window.location.origin,
          },
          useRefreshTokens: true,
          cacheLocation: "memory",
        });
      })
      .then(function (client) {
        _auth0 = client;

        // Handle redirect callback after Auth0 login
        if (
          window.location.search.indexOf("code=") !== -1 &&
          window.location.search.indexOf("state=") !== -1
        ) {
          return _auth0.handleRedirectCallback().then(function () {
            window.history.replaceState({}, document.title, "/");
          });
        }
      })
      .then(function () {
        return _auth0.isAuthenticated();
      })
      .then(function (isAuthenticated) {
        if (!isAuthenticated) return null;

        return _auth0
          .getIdTokenClaims()
          .then(function (claims) {
            _idToken = claims.__raw;
            return _verifyAccess(_idToken);
          })
          .then(function (me) {
            // Attach display name from Auth0 claims (populated later by app.js)
            _userInfo = me;
            return _auth0.getIdTokenClaims();
          })
          .then(function (claims) {
            _userInfo.displayName =
              claims.name || claims.email || claims.sub || "";
            return _userInfo;
          });
      });
  }

  function login() {
    _auth0.loginWithRedirect();
  }

  function logout() {
    _idToken = null;
    _userInfo = null;
    return _auth0.logout({
      logoutParams: { returnTo: window.location.origin },
    });
  }

  function getToken() {
    return _idToken;
  }

  function getUserInfo() {
    return _userInfo;
  }

  global.HQAuth = {
    init: init,
    login: login,
    logout: logout,
    getToken: getToken,
    getUserInfo: getUserInfo,
  };
})(window);
