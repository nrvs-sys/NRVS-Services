using Services.UGS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.Events;

namespace Services.Edgegap
{
    /// <summary>
    /// Manages the state of the Edgegap Relay.
    /// Centralizes session state, user authorization, and user removal.
    /// </summary>
    public class RelayManager : MonoBehaviour, IRelayPlatform
    {
        #region IPlatform Fields

        public string playerID => Ref.Get<LobbyManager>()?.playerID ?? "";

        public string displayName => Ref.Get<LobbyManager>()?.displayName ?? "";

        public string loginState => Ref.Get<LobbyManager>()?.loginState ?? "";

        public bool isLoggedIn => Ref.Get<LobbyManager>()?.isLoggedIn ?? false;

        public bool hasLoginError => Ref.Get<LobbyManager>()?.hasLoginError ?? false;

        public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

        #endregion

        [Header("Settings")]

        [SerializeField, Tooltip("The Edgegap relay profile token used to authenticate API requests.")]
        string relayProfileToken;

        [SerializeField, Tooltip("How long (in seconds) a client will wait for relay authorization before giving up.")]
        float authorizationTimeoutSeconds = 15f;

        [SerializeField, Tooltip("How frequently (in seconds) the client polls the relay session to check for authorization.")]
        float authorizationPollIntervalSeconds = 1.5f;

        [Header("Events")]

        [SerializeField, Tooltip("Fired when the relay session ID is first received from lobby data (informational only).")]
        UnityEvent<string> onSessionIdReceived;

        [SerializeField, Tooltip("Fired on non-host clients when they are authorized and ready to connect to the relay. Wire this to StartClient on the RelayConnectionBehaviour.")]
        public UnityEvent onClientReadyToConnect;

        [SerializeField, Tooltip("Fired on non-host clients when authorization fails or times out.")]
        public UnityEvent onClientAuthorizationFailed;

        /// <summary>
        /// The current relay session ID. Empty when no session is active.
        /// </summary>
        public string RelaySessionId { get; private set; } = "";

        /// <summary>
        /// The full session response from the most recent Create/Join/Authorize call.
        /// </summary>
        public RelayService.SessionResponse CurrentSession { get; private set; }

        /// <summary>
        /// Tracks player IDs whose IPs have already been authorized on the relay,
        /// so we don't double-authorize on every lobby update.
        /// </summary>
        readonly HashSet<string> authorizedPlayerIds = new HashSet<string>();

        RelayService relayService;

        /// <summary>
        /// Prevents multiple concurrent authorization-wait coroutines from running.
        /// </summary>
        Coroutine clientAuthorizationCoroutine;

        public RelayService GetRelayService()
        {
            relayService ??= new RelayService(relayProfileToken);
            return relayService;
        }

        #region Unity Methods

        void OnEnable()
        {
            Ref.Register<IRelayPlatform>(this);
            Ref.Register<RelayManager>(this);

            Ref.Instance.OnRegistered += Ref_OnRegister;
            Ref.Instance.OnUnregistered += Ref_OnUnregister;

            if (Ref.TryGet<LobbyManager>(out var lobbyManager))
            {
                Ref_OnRegister(typeof(LobbyManager), lobbyManager);
            }
        }

        void OnDisable()
        {
            if (Ref.Instance != null)
            {
                Ref.Unregister<IRelayPlatform>(this);
                Ref.Unregister<RelayManager>(this);

                Ref.Instance.OnRegistered -= Ref_OnRegister;
                Ref.Instance.OnUnregistered -= Ref_OnUnregister;

                if (Ref.TryGet<LobbyManager>(out var lobbyManager))
                {
                    Ref_OnUnregister(typeof(LobbyManager), lobbyManager);
                }
            }

            StopClientAuthorizationCoroutine();
        }

        #endregion

        #region Session State Methods

        /// <summary>
        /// Called by the host after creating a relay session. Stores the session,
        /// seeds the authorized set with all initial players, and broadcasts the ID via lobby data.
        /// </summary>
        public void SetSession(RelayService.SessionResponse session)
        {
            CurrentSession = session;
            RelaySessionId = session?.session_id ?? "";

            // Seed the authorized set with the players from the initial session creation
            authorizedPlayerIds.Clear();
            if (session?.session_users != null)
            {
                var lobbyManager = Ref.Get<LobbyManager>();
                if (lobbyManager?.joinedLobby?.Players != null)
                {
                    foreach (var player in lobbyManager.joinedLobby.Players)
                    {
                        var ip = lobbyManager.GetPlayerDataValue(player, Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp);
                        if (!string.IsNullOrEmpty(ip) && session.session_users.Exists(u => u.ip_address == ip))
                        {
                            authorizedPlayerIds.Add(player.Id);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(RelaySessionId))
            {
                StartRelaySessionOnClients(RelaySessionId);
                onSessionIdReceived?.Invoke(RelaySessionId);
            }
        }

        /// <summary>
        /// Clears all local relay state. Call when leaving a lobby or going offline.
        /// </summary>
        public void ClearSession()
        {
            StopClientAuthorizationCoroutine();
            CurrentSession = null;
            RelaySessionId = "";
            authorizedPlayerIds.Clear();
        }

        #endregion

        #region User Authorization / Removal

        /// <summary>
        /// Authorizes a new user on the relay session by their public IP.
        /// Called by the host when a late joiner's IP appears in lobby player data.
        /// </summary>
        public async Task<RelayService.SessionResponse> AuthorizeUserAsync(string userIp)
        {
            if (string.IsNullOrEmpty(RelaySessionId))
            {
                Debug.LogError("Relay Manager: Cannot authorize user — no active relay session.");
                return null;
            }

            try
            {
                Debug.Log($"Relay Manager: Authorizing user with IP {userIp} on session {RelaySessionId}");
                var response = await GetRelayService().AuthorizeUserAsync(RelaySessionId, userIp);
                CurrentSession = response;
                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"Relay Manager: Failed to authorize user: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Removes / revokes a user from the relay session by their authorization token.
        /// Called by the host when a player leaves or is kicked.
        /// </summary>
        public async Task<RelayService.SessionResponse> RemoveUserAsync(string userAuthorizationToken)
        {
            if (string.IsNullOrEmpty(RelaySessionId))
            {
                Debug.LogError("Relay Manager: Cannot remove user — no active relay session.");
                return null;
            }

            try
            {
                Debug.Log($"Relay Manager: Removing user (token: {userAuthorizationToken}) from session {RelaySessionId}");
                var response = await GetRelayService().RemoveUserAsync(RelaySessionId, userAuthorizationToken);
                CurrentSession = response;
                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"Relay Manager: Failed to remove user: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds a session user by their public IP address.
        /// </summary>
        public RelayService.SessionUser FindSessionUser(string publicIp)
        {
            return CurrentSession?.session_users?.Find(u => u.ip_address == publicIp);
        }

        /// <summary>
        /// Checks whether a given public IP is authorized in the current session.
        /// </summary>
        public bool IsUserAuthorized(string publicIp)
        {
            var user = FindSessionUser(publicIp);
            return user != null && user.authorization_token.HasValue && user.authorization_token.Value != 0;
        }

        /// <summary>
        /// Host-side: checks all lobby players and authorizes any whose IP is present
        /// but not yet authorized on the relay session.
        /// </summary>
        async Task AuthorizePendingPlayersAsync(Lobby lobby)
        {
            if (string.IsNullOrEmpty(RelaySessionId)) return;

            var lobbyManager = Ref.Get<LobbyManager>();
            if (lobbyManager == null || !lobbyManager.IsLocalPlayerLobbyHost(lobby)) return;

            foreach (var player in lobby.Players)
            {
                // Skip already-authorized players
                if (authorizedPlayerIds.Contains(player.Id)) continue;

                // Skip players who haven't published their IP yet
                var ip = lobbyManager.GetPlayerDataValue(player, Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp);
                if (string.IsNullOrEmpty(ip)) continue;

                // Authorize on the relay
                Debug.Log($"Relay Manager: Late joiner detected — authorizing player {player.Id} (IP: {ip})");
                var response = await AuthorizeUserAsync(ip);

                if (response != null)
                {
                    authorizedPlayerIds.Add(player.Id);
                }
            }
        }

        /// <summary>
        /// Host-side: revokes relay authorization for a player that left/was kicked.
        /// </summary>
        public async Task RevokePlayerAsync(Lobby lobby, Player player)
        {
            if (string.IsNullOrEmpty(RelaySessionId)) return;

            var lobbyManager = Ref.Get<LobbyManager>();
            if (lobbyManager == null || !lobbyManager.IsLocalPlayerLobbyHost(lobby)) return;

            var ip = lobbyManager.GetPlayerDataValue(player, Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp);
            if (string.IsNullOrEmpty(ip)) return;

            var sessionUser = FindSessionUser(ip);
            if (sessionUser?.authorization_token == null) return;

            Debug.Log($"Relay Manager: Revoking relay access for player {player.Id} (IP: {ip})");
            await RemoveUserAsync(sessionUser.authorization_token.Value.ToString());

            authorizedPlayerIds.Remove(player.Id);
        }

        #endregion

        #region Client Authorization Wait

        /// <summary>
        /// Client-side: begins waiting for authorization on the relay session.
        /// Once authorized, fires <see cref="onClientReadyToConnect"/>.
        /// If it times out, fires <see cref="onClientAuthorizationFailed"/>.
        /// </summary>
        void BeginWaitForAuthorization()
        {
            // Only non-host clients need to wait
            var lobbyManager = Ref.Get<LobbyManager>();
            if (lobbyManager == null || lobbyManager.IsLocalPlayerLobbyHost()) return;

            if (string.IsNullOrEmpty(RelaySessionId)) return;

            StopClientAuthorizationCoroutine();
            clientAuthorizationCoroutine = StartCoroutine(DoWaitForAuthorization());
        }

        void StopClientAuthorizationCoroutine()
        {
            if (clientAuthorizationCoroutine != null)
            {
                StopCoroutine(clientAuthorizationCoroutine);
                clientAuthorizationCoroutine = null;
            }
        }

        IEnumerator DoWaitForAuthorization()
        {
            var lobbyManager = Ref.Get<LobbyManager>();
            var localPlayer = lobbyManager?.GetLocalPlayer();

            if (localPlayer == null)
            {
                Debug.LogError("Relay Manager: Cannot wait for authorization — local player not found.");
                onClientAuthorizationFailed?.Invoke();
                clientAuthorizationCoroutine = null;
                yield break;
            }

            // First, ensure our public IP is available in lobby data.
            // LobbyIpLookup should have already started uploading it, but we wait for it here.
            float ipWaitElapsed = 0f;
            string localIP = null;

            while (ipWaitElapsed < authorizationTimeoutSeconds)
            {
                // Re-fetch the local player each iteration in case lobby data updated
                localPlayer = lobbyManager.GetLocalPlayer();
                if (localPlayer != null)
                    localIP = lobbyManager.GetPlayerDataValue(localPlayer, Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp);

                if (!string.IsNullOrEmpty(localIP))
                    break;

                ipWaitElapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (string.IsNullOrEmpty(localIP))
            {
                Debug.LogError("Relay Manager: Timed out waiting for local player's public IP to appear in lobby data.");
                onClientAuthorizationFailed?.Invoke();
                clientAuthorizationCoroutine = null;
                yield break;
            }

            Debug.Log($"Relay Manager: Local IP ({localIP}) available. Polling relay session for authorization...");

            // Now poll the relay API until our IP is authorized or timeout
            float authElapsed = 0f;
            var pollDelay = new WaitForSecondsRealtime(authorizationPollIntervalSeconds);

            while (authElapsed < authorizationTimeoutSeconds)
            {
                // Check cached session first
                if (IsUserAuthorized(localIP))
                {
                    Debug.Log("Relay Manager: Client authorized on relay session (cached).");
                    onClientReadyToConnect?.Invoke();
                    clientAuthorizationCoroutine = null;
                    yield break;
                }

                // Poll the relay API
                var task = GetRelayService().GetSessionAsync(RelaySessionId);
                yield return new WaitUntil(() => task.IsCompleted);

                if (task.Exception == null && task.Result != null)
                {
                    CurrentSession = task.Result;

                    if (IsUserAuthorized(localIP))
                    {
                        Debug.Log("Relay Manager: Client authorized on relay session.");
                        onClientReadyToConnect?.Invoke();
                        clientAuthorizationCoroutine = null;
                        yield break;
                    }
                }
                else if (task.Exception != null)
                {
                    Debug.LogWarning($"Relay Manager: Error polling for authorization: {task.Exception.Message}");
                }

                yield return pollDelay;
                authElapsed += authorizationPollIntervalSeconds;
            }

            Debug.LogError($"Relay Manager: Client authorization timed out after {authorizationTimeoutSeconds}s.");
            onClientAuthorizationFailed?.Invoke();
            clientAuthorizationCoroutine = null;
        }

        #endregion

        #region Lobby Relay Broadcast

        void StartRelaySessionOnClients(string relaySessionId)
        {
            var lobbyManager = Ref.Get<LobbyManager>();
            if (lobbyManager != null && lobbyManager.IsLocalPlayerLobbyHost())
            {
                Debug.Log("Relay Manager: Sending Relay Session to clients");
                _ = lobbyManager.SetLobbyDataValues(lobbyManager.joinedLobby, new LobbyManager.LobbyDataValue(Constants.Services.Edgegap.Relay.LobbyDataKeys.RelaySessionId, relaySessionId, DataObject.VisibilityOptions.Member));
            }
        }

        #endregion

        #region Event Handlers

        void Ref_OnRegister(System.Type type, object instance)
        {
            if (instance is LobbyManager lobbyManager)
            {
                lobbyManager.onLobbyJoined.AddListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onJoinedLobbyUpdated.AddListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onLobbyLeft.AddListener(LobbyManager_OnLobbyLeft);

                if (lobbyManager.joinedLobby != null)
                {
                    LobbyManager_OnJoinedLobbyUpdated(lobbyManager.joinedLobby);
                }
            }
        }

        void Ref_OnUnregister(System.Type type, object instance)
        {
            if (instance is LobbyManager lobbyManager)
            {
                lobbyManager.onLobbyJoined.RemoveListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onJoinedLobbyUpdated.RemoveListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onLobbyLeft.RemoveListener(LobbyManager_OnLobbyLeft);
            }
        }

        void LobbyManager_OnJoinedLobbyUpdated(Lobby lobby)
        {
            if (lobby == null) return;

            // --- Relay session ID propagation (for clients) ---
            var relaySessionId = lobby.Data.GetValueOrDefault(Constants.Services.Edgegap.Relay.LobbyDataKeys.RelaySessionId, new DataObject(DataObject.VisibilityOptions.Member, "")).Value;

            if (relaySessionId != RelaySessionId)
            {
                var previous = RelaySessionId;
                RelaySessionId = relaySessionId;

                Debug.Log($"Relay Manager: Relay session id updated from {previous} to {RelaySessionId}");

                if (!string.IsNullOrEmpty(RelaySessionId))
                {
                    onSessionIdReceived?.Invoke(RelaySessionId);

                    // Client-side: session ID just arrived — begin the authorization wait
                    BeginWaitForAuthorization();
                }
            }

            // --- Host-side: authorize any late joiners whose IP is now available ---
            _ = AuthorizePendingPlayersAsync(lobby);
        }

        void LobbyManager_OnLobbyLeft(Lobby lobby)
        {
            ClearSession();
        }

        #endregion
    }
}
