/* HQ Agent — Auth module
   Auth0 handles sign-in; usermgmt (usermanagement.beconcrete.se) gates app access.

   AUTH0_DOMAIN and AUTH0_CLIENT_ID are injected at deploy time by the
   GitHub Actions workflow (envsubst). The placeholders below are replaced
   before the files are uploaded to the Static Web App.

   Token is kept in memory only — never written to localStorage or sessionStorage.
*/
(function (global) {
  "use strict";

  var AUTH0_DOMAIN = "${AUTH0_DOMAIN}";
  var AUTH0_CLIENT_ID = "${AUTH0_CLIENT_ID}";
  var ME_URL = "/api/me";
  var APP_ID = "hqagents";

  var _auth0 = null;
  var _idToken = null; // in-memory only
  var _userInfo = null;

  function _createClient() {
    return window.auth0.createAuth0Client({
      domain: AUTH0_DOMAIN,
      clientId: AUTH0_CLIENT_ID,
      authorizationParams: {
        redirect_uri: window.location.origin,
      },
      useRefreshTokens: true,
      cacheLocation: "memory",
    });
  }

  function _verifyAccess(token) {
    return fetch(ME_URL, {
      headers: { "X-Auth-Token": "Bearer " + token },
    })
      .then(function (res) {
        if (res.status === 403) {
          var err = new Error("no-access");
          err.code = "no-access";
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
    return _createClient()
      .then(function (client) {
        _auth0 = client;

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

        var _claims;
        return _auth0
          .getIdTokenClaims()
          .then(function (claims) {
            _claims = claims;
            _idToken = claims.__raw;
            return _verifyAccess(_idToken);
          })
          .then(function (me) {
            _userInfo = me;
            _userInfo.displayName =
              _claims.name || _claims.email || _claims.sub || "";
            return _userInfo;
          });
      });
  }

  function login() {
    if (_auth0) {
      _auth0.loginWithRedirect();
      return;
    }
    // init() failed before the Auth0 client was created — retry
    _createClient().then(function (client) {
      _auth0 = client;
      return client.loginWithRedirect();
    });
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
