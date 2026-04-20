import { reactive, readonly } from "vue";
import { createAuth0Client } from "@auth0/auth0-spa-js";

const APP_ID = "hqagents";

const state = reactive({
  isLoading: true,
  isAuthenticated: false,
  user: null,
  error: null, // null | 'access_denied' | 'error'
});

let _client = null;
let _idToken = null;

async function _fetchMe() {
  const res = await fetch("/api/me", {
    headers: { "X-Auth-Token": `Bearer ${_idToken}` },
  });

  if (res.status === 403) {
    state.error = "access_denied";
    return;
  }
  if (!res.ok) {
    state.error = "error";
    return;
  }

  const me = await res.json();
  if (!me.apps || !me.apps.includes(APP_ID)) {
    state.error = "access_denied";
    return;
  }

  state.user = {
    userId: me.userId,
    displayName: me.displayName || me.userId,
    roles: me.roles || [],
  };
  state.isAuthenticated = true;
}

export async function initAuth() {
  try {
    _client = await createAuth0Client({
      domain: import.meta.env.VITE_AUTH0_DOMAIN,
      clientId: import.meta.env.VITE_AUTH0_CLIENT_ID,
      authorizationParams: { redirect_uri: window.location.origin },
      useRefreshTokens: true,
      cacheLocation: "localstorage",
    });

    if (
      window.location.search.includes("code=") &&
      window.location.search.includes("state=")
    ) {
      await _client.handleRedirectCallback();
      window.history.replaceState({}, document.title, window.location.pathname);
    } else {
      // Attempt silent re-auth on page refresh — uses Auth0's session cookie.
      // Throws 'login_required' if the session has expired; that's expected and safe to ignore.
      try {
        await _client.getTokenSilently();
      } catch {
        /* not authenticated */
      }
    }

    if (await _client.isAuthenticated()) {
      const claims = await _client.getIdTokenClaims();
      // displayName comes from JWT claims — /api/me doesn't return it
      const displayName = claims.name || claims.email || claims.sub || "";
      _idToken = claims.__raw;
      await _fetchMe();
      if (state.user) state.user.displayName = displayName;
    }
  } catch {
    state.error = "error";
  } finally {
    state.isLoading = false;
  }
}

export function useAuth() {
  return {
    state: readonly(state),

    login() {
      if (_client) return _client.loginWithRedirect();
      // client failed to init — retry
      return createAuth0Client({
        domain: import.meta.env.VITE_AUTH0_DOMAIN,
        clientId: import.meta.env.VITE_AUTH0_CLIENT_ID,
        authorizationParams: { redirect_uri: window.location.origin },
      }).then((c) => {
        _client = c;
        return c.loginWithRedirect();
      });
    },

    logout() {
      _idToken = null;
      return _client.logout({
        logoutParams: { returnTo: window.location.origin },
      });
    },

    getToken: () => _idToken,

    hasRole(role) {
      const roles = state.user?.roles ?? [];
      return roles.includes("admin") || roles.includes(role);
    },
  };
}
