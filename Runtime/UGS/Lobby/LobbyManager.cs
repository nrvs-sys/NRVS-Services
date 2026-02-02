using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.Events;

namespace Services.UGS
{
    public class LobbyManager : MonoBehaviour, ILobbyPlatform
    {

        #region Types

        //Manages the Amount of times you can hit a service call.
        //Adds a buffer to account for ping times.
        //Will Queue the latest overflow task for when the cooldown ends.
        //Created to mimic the way rate limits are implemented Here:  https://docs.unity.com/lobby/rate-limits.html
        public class ServiceRateLimiter
        {
            public Action<bool> onCooldownChange;

            // config
            readonly int _maxCalls;
            readonly int _windowMs;

            // state
            readonly object _lockObj = new();
            long _windowStartMs;     // Environment.TickCount64 at window start
            int _callsInWindow;
            bool _isCoolingDown;     // cached for change notifications

            public int coolDownMS => _windowMs;

            /// <param name="callTimes">Max calls allowed in the window.</param>
            /// <param name="coolDownSeconds">Window length in seconds.</param>
            /// <param name="pingBufferMs">Extra ms added to the window to account for latency.</param>
            public ServiceRateLimiter(int callTimes, float coolDownSeconds, int pingBufferMs = 100)
            {
                _maxCalls = Mathf.Max(1, callTimes);
                _windowMs = Mathf.Max(1, Mathf.CeilToInt(coolDownSeconds * 1000f) + pingBufferMs);
                _windowStartMs = Environment.TickCount;
                _callsInWindow = 0;
                _isCoolingDown = false;
            }

            /// <summary>
            /// Returns true if a token is available **now** and consumes it; otherwise false (do not call the service).
            /// </summary>
            public bool TryAcquire()
            {
                lock (_lockObj)
                {
                    RollWindowIfNeeded_NoLock();

                    if (_callsInWindow < _maxCalls)
                    {
                        _callsInWindow++;
                        UpdateCooldownFlag_NoLock();
                        return true;
                    }

                    UpdateCooldownFlag_NoLock();
                    return false;
                }
            }

            /// <summary>
            /// Waits until a token is available, then consumes it.
            /// </summary>
            public async Task QueueUntilCooldown()
            {
                while (true)
                {
                    int delayMs;
                    lock (_lockObj)
                    {
                        RollWindowIfNeeded_NoLock();

                        if (_callsInWindow < _maxCalls)
                        {
                            _callsInWindow++;
                            UpdateCooldownFlag_NoLock();
                            return; // allowed now
                        }

                        // need to wait until this fixed window ends
                        long now = Environment.TickCount;
                        var elapsed = (int)(now - _windowStartMs);
                        delayMs = Mathf.Max(1, _windowMs - elapsed);
                        UpdateCooldownFlag_NoLock();
                    }

                    // wait outside the lock
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    // loop: on wake we re-check and consume
                }
            }

            /// <summary>True iff the current window is saturated.</summary>
            public bool IsCoolingDown
            {
                get
                {
                    lock (_lockObj)
                    {
                        RollWindowIfNeeded_NoLock();
                        return _callsInWindow >= _maxCalls;
                    }
                }
            }

            void RollWindowIfNeeded_NoLock()
            {
                long now = Environment.TickCount;
                if (now - _windowStartMs >= _windowMs)
                {
                    _windowStartMs = now;
                    _callsInWindow = 0;
                }
            }

            void UpdateCooldownFlag_NoLock()
            {
                bool cooling = _callsInWindow >= _maxCalls;
                if (cooling != _isCoolingDown)
                {
                    _isCoolingDown = cooling;
                    onCooldownChange?.Invoke(_isCoolingDown);
                }
            }
        }


        public enum RequestType
        {
            Query = 0,
            Join,
            QuickJoin,
            Host
        }

        public struct LobbyDataValue
        {
            public string key;
            public string value;
            public DataObject.VisibilityOptions visibilityOptions;

            public LobbyDataValue(string key, string value, DataObject.VisibilityOptions visibilityOptions)
            {
                this.key = key;
                this.value = value;
                this.visibilityOptions = visibilityOptions;
            }
        }

        public struct PlayerDataValue
        {
            public string key;
            public string value;
            public PlayerDataObject.VisibilityOptions visibilityOptions;

            public PlayerDataValue(string key, string value, PlayerDataObject.VisibilityOptions visibilityOptions)
            {
                this.key = key;
                this.value = value;
                this.visibilityOptions = visibilityOptions;
            }
        }

        #endregion

        #region Serialized Fields

        [Header("Settings")]

        [SerializeField, Min(1)]
        float heartbeatPingInterval = 5f;

        [SerializeField, Min(1)]
        float queryLobbiesInterval = 5f;

        [SerializeField, Min(1)]
        int maxLobbySize = 3;

        [SerializeField]
        bool enableHostMigration;

        [SerializeField, Min(2f)]
        float heartbeatIntervalSec = 5f;

        [SerializeField, Min(5f)]
        float heartbeatTimeoutSec = 15f;

        [Header("Events")]

        public UnityEvent<Lobby> onLobbyHosted;

        public UnityEvent onLobbyHostEnd;

        public UnityEvent<Lobby> onLobbyJoined;

        public UnityEvent<Lobby> onJoinedLobbyUpdated;

        public UnityEvent<Lobby> onLobbyLeft;

        public UnityEvent<Lobby> onLobbyKicked;

        public UnityEvent<List<Lobby>> onLobbiesLoaded;

        #endregion

        #region Service Rate Limiters

        ServiceRateLimiter queryLobbiesRateLimiter = new ServiceRateLimiter(1, 1f);
        ServiceRateLimiter createLobbyRateLimiter = new ServiceRateLimiter(2, 6f);
        ServiceRateLimiter joinLobbyRateLimiter = new ServiceRateLimiter(2, 6f);
        ServiceRateLimiter quickJoinRateLimiter = new ServiceRateLimiter(1, 1f);
        ServiceRateLimiter getLobbyRateLimiter = new ServiceRateLimiter(1, 1f);
        ServiceRateLimiter deleteLobbyRateLimiter = new ServiceRateLimiter(2, 1f);
        ServiceRateLimiter updateLobbyRateLimiter = new ServiceRateLimiter(5, 5f);
        ServiceRateLimiter updatePlayersRateLimiter = new ServiceRateLimiter(5, 5f);
        ServiceRateLimiter leaveLobbyOrRemovePlayerRateLimiter = new ServiceRateLimiter(5, 1);
        ServiceRateLimiter lobbyHeartbeatRateLimiter = new ServiceRateLimiter(1, 1);

        #endregion

        #region IPlatform Fields

        public string playerID => Ref.Get<UGSManager>()?.playerID ?? "";

        public string displayName => Ref.Get<UGSManager>()?.displayName ?? "";

        public string loginState => Ref.Get<UGSManager>()?.loginState ?? "";

        public bool isLoggedIn => Ref.Get<UGSManager>()?.isLoggedIn ?? false;

        public bool hasLoginError => Ref.Get<UGSManager>()?.hasLoginError ?? false;

        public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

        #endregion

        const string kHeartbeatKey = "HeartbeatEpoch";  // unix seconds as string
        const string kSleepingKey = "IsSleeping";

        public bool isInitialized => localPlayerId != null;

        /// <summary>
        /// Returns true if the Lobby Manager is attempting to host a lobby (the coroutine is running), or is hosting a lobby.
        /// </summary>
        public bool isHostingLobbyInProgress => hostingLobbyCoroutine != null;

        public Lobby joinedLobby { get; private set; }

        public ILobbyEvents joinedLobbyEvents { get; private set; }

        List<Lobby> availableLobbies;

        string localPlayerId;

        Coroutine hostingLobbyCoroutine;

        Coroutine queryLobbiesCoroutine;

        Coroutine heartbeatCoroutine;
        Coroutine hostWatchdogCoroutine;

        #region Unity Methods

        void Awake()
        {
            Ref.Register<ILobbyPlatform>(this);
            Ref.Register<LobbyManager>(this);
        }

        void Start()
        {
            Ref.Instance.OnRegistered += Ref_OnRegistered;
            Ref.Instance.OnUnregistered += Ref_OnUnregistered;

            ApplicationInfo.OnInternetAvailabilityChanged += ApplicationInfo_OnInternetAvailabilityChanged;

            if (Ref.TryGet(out UGSManager ugsManager))
                Ref_OnRegistered(typeof(UGSManager), ugsManager);
        }

        void OnDestroy()
        {
            if (quitting) return;
            EndConnectionsImmediately();

            Ref.Unregister<ILobbyPlatform>(this);
            Ref.Unregister<LobbyManager>(this);

            if (Ref.Instance != null)
            {
                Ref.Instance.OnRegistered -= Ref_OnRegistered;
                Ref.Instance.OnUnregistered -= Ref_OnUnregistered;
            }

            if (Ref.TryGet(out UGSManager uGSManager))
                Ref_OnUnregistered(typeof(UGSManager), uGSManager);

            AuthenticationService.Instance.SignedIn -= AuthenticationService_OnSignedIn;
            AuthenticationService.Instance.SignedOut -= AuthenticationService_OnSignedOut;

            ApplicationInfo.OnInternetAvailabilityChanged -= ApplicationInfo_OnInternetAvailabilityChanged;
        }

        bool quitting = false;

        void OnApplicationQuit()
        {
            quitting = true;
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                //EndConnectionsImmediately();
            }
        }

        public void EndConnectionsImmediately()
        {
            if (joinedLobbyEvents != null)
            {
                joinedLobbyEvents.UnsubscribeAsync();
                joinedLobbyEvents = null;
            }

            // Fire off direct lobby service calls to avoid the async waits requirements in the `LeaveJoinedLobby()` method
            if (IsLocalPlayerInJoinedLobby())
            {
                if (!joinedLobby.Id.IsNullOrEmpty())
                {
                    // Delete lobby if Host migration is disabled or if there is only 1 player remaining
                    if (IsLocalPlayerLobbyHost(joinedLobby) && (!enableHostMigration || joinedLobby.Players.Count == 1))
                    {
                        Debug.Log($"Lobby Manager: Request - Deleting lobby {joinedLobby.Id} due to Application Quit/Pause...");
                        LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
                    }
                    else
                    {
                        Debug.Log($"Lobby Manager: Request - Removing player {playerID} from lobby {joinedLobby.Id} due to Application Quit/Pause...");
                        LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, playerID);
                    }
                }

                // important to null joined as this will be calld before OnDestroy during an application quit
                var leftLobby = joinedLobby;
                joinedLobby = null;

                availableLobbies?.RemoveAll(lobby => lobby.Id == leftLobby.Id);

                onLobbyLeft?.Invoke(leftLobby);

                onLobbiesLoaded?.Invoke(availableLobbies);
            }
        }

        #endregion

        #region Host Lobby Methods

        public void StartHostLobby(Dictionary<string, DataObject> lobbyData, bool isPrivate)
        {
            if (hostingLobbyCoroutine != null)
                return;

            hostingLobbyCoroutine = StartCoroutine(DoHostLobby(lobbyData, isPrivate));
        }

        IEnumerator DoHostLobby(Dictionary<string, DataObject> lobbyData, bool isPrivate)
        {
            ILeaderboardPlatform leaderboardPlatform = null;

            while (!isInitialized || !Ref.TryGet(out leaderboardPlatform) || !leaderboardPlatform.isLoggedIn)
                yield return null;

            if (joinedLobby == null)
            {
                var t = Task.Run(async () => await CreateLobby($"{leaderboardPlatform.displayName}'s Lobby", maxLobbySize, new()
                {
                    IsPrivate = isPrivate,
                    Player = CreateLocalPlayer(),
                    Data = lobbyData
                }));

                yield return new WaitUntil(() => t.IsCompleted);

                LobbyJoined(t.Result);
            }

            onLobbyHosted?.Invoke(joinedLobby);

            yield return new WaitForSecondsRealtime(heartbeatPingInterval);

            while (IsLocalPlayerLobbyHost())
            {
                _ = SendHeartbeatPing(joinedLobby.Id);

                yield return new WaitForSecondsRealtime(heartbeatPingInterval);
            }

            hostingLobbyCoroutine = null;
        }

        public async Task StopHostLobby(bool deleteLobby)
        {
            if (hostingLobbyCoroutine != null)
                StopCoroutine(hostingLobbyCoroutine);

            hostingLobbyCoroutine = null;

            if (IsLocalPlayerLobbyHost())
            {
                if (joinedLobbyEvents != null)
                    await joinedLobbyEvents.UnsubscribeAsync();

                if (deleteLobby)
                    await DeleteLobby(joinedLobby.Id);
                else
                    await RemovePlayer(joinedLobby.Id, localPlayerId);

                var leftLobby = joinedLobby;

                joinedLobby = null;

                joinedLobbyEvents = null;

                StopHeartbeat();
                StopHostWatchdog();

                onLobbyHostEnd?.Invoke();

                onLobbyLeft?.Invoke(leftLobby);

                availableLobbies?.RemoveAll(lobby => lobby.Id == leftLobby.Id);

                onLobbiesLoaded?.Invoke(availableLobbies);
            }
        }

        #endregion

        #region Query Lobbies Methods

        public void StartQueryingLobbies(QueryLobbiesOptions options = null)
        {
            if (queryLobbiesCoroutine != null)
            {
                Debug.LogWarning("Lobby Manager: Already querying lobbies on an interval.");
                return;
            }

            queryLobbiesCoroutine = StartCoroutine(DoQueryingLobbies(options));
        }

        IEnumerator DoQueryingLobbies(QueryLobbiesOptions options)
        {
            while (!isInitialized)
                yield return null;

            while (true)
            {
                var t = Task.Run(async () => await QueryLobbies(options));

                yield return new WaitUntil(() => t.IsCompleted);

                if (t.Result != null)
                {
                    availableLobbies = t.Result;

                    onLobbiesLoaded?.Invoke(availableLobbies);
                }

                yield return new WaitForSecondsRealtime(queryLobbiesInterval);
            }
        }

        public void StopQueryingLobbies()
        {
            if (queryLobbiesCoroutine != null)
                StopCoroutine(queryLobbiesCoroutine);

            queryLobbiesCoroutine = null;
        }

        #endregion

        #region Lobby Joining Methods

        public async Task JoinLobbyByID(string ID)
        {
            var lobby = await JoinLobbyByID(ID, new()
            {
                Player = CreateLocalPlayer()
            });

            LobbyJoined(lobby);
        }

        public async Task<Lobby> JoinLobbyByCode(string code)
        {
            var lobby = await JoinLobbyByCode(code, new()
            {
                Player = CreateLocalPlayer()
            });

            LobbyJoined(lobby);

            return lobby;
        }

        public async Task<Lobby> QuickJoinLobby(List<QueryFilter> filters = null)
        {
            var lobby = await QuickJoinLobby(new QuickJoinLobbyOptions()
            {
                Player = CreateLocalPlayer(),
                Filter = filters
            });

            LobbyJoined(lobby);

            return lobby;
        }

        async void LobbyJoined(Lobby lobby)
        {
            if (!IsLocalPlayerInLobby(lobby))
                return;
            
            joinedLobby = lobby;

            // Create callbacks for the various lobby events
            var callbacks = new LobbyEventCallbacks();

            callbacks.LobbyChanged += async changes =>
            {
                if (changes.LobbyDeleted)
                {
                    _ = LeaveJoinedLobby();
                }
                else if (joinedLobby != null)
                {
                    // If the version associated with a new set of lobby changes is not exactly one greater than the last observed lobby version,
                    // an update has been received out of order or lost.
                    // If you have a missing message, get the full lobby state.
                    if (changes.Version.Value != joinedLobby.Version + 1)
                    {
                        Debug.LogWarning($"Lobby Manager: Lobby Event Callbacks: Version mismatch (found version {changes.Version.Value}) instead of expected {joinedLobby.Version + 1}. Getting full lobby state.");

                        var latestLobby = await GetLobby(joinedLobby.Id);

                        if (latestLobby != null)
                            joinedLobby = latestLobby;
                        else
                            return;
                    }
                    else
                    {
                        changes.ApplyToLobby(joinedLobby);
                    }

                    // Handle Host Migration
                    if (enableHostMigration && hostingLobbyCoroutine == null && IsLocalPlayerLobbyHost(joinedLobby))
                    {
                        Debug.Log("Lobby Manager: Local Player has become Host");
                        StartHostLobby(null, joinedLobby.IsPrivate);
                    }

                    StartOrStopHostWatchdog();

                    onJoinedLobbyUpdated?.Invoke(joinedLobby);
                }
            };

            callbacks.PlayerDataAdded += changes =>
            {
                onJoinedLobbyUpdated?.Invoke(joinedLobby);
            };

            callbacks.PlayerDataChanged += changes =>
            {
                onJoinedLobbyUpdated?.Invoke(joinedLobby);
            };

            callbacks.KickedFromLobby += () =>
            {
                Debug.Log("Lobby Manager: Local Player kicked from lobby!");

                if (joinedLobbyEvents != null)
                    _ = joinedLobbyEvents.UnsubscribeAsync();

                onLobbyKicked?.Invoke(joinedLobby);

                joinedLobby = null;

                joinedLobbyEvents = null;
            };

            callbacks.LobbyEventConnectionStateChanged += state =>
            {
                switch (state)
                {
                    case LobbyEventConnectionState.Unsubscribed:
                        /* Update the UI if necessary, as the subscription has been stopped. */
                        break;
                    case LobbyEventConnectionState.Subscribing:
                        /* Update the UI if necessary, while waiting to be subscribed. */
                        break;
                    case LobbyEventConnectionState.Subscribed:
                        /* Update the UI if necessary, to show subscription is working. */
                        break;
                    case LobbyEventConnectionState.Unsynced:
                        /* Update the UI to show connection problems. Lobby will attempt to reconnect automatically. */
                        break;
                    case LobbyEventConnectionState.Error:
                        /* Update the UI to show the connection has errored. Lobby will not attempt to reconnect as something has gone wrong. */
                        break;
                }

                Debug.Log($"Lobby Manager: Lobby Event Connection State Changed: {state}");
            };

            try
            {
                joinedLobbyEvents = await Lobbies.Instance.SubscribeToLobbyEventsAsync(joinedLobby.Id, callbacks);

                // Enable Events for the Local Player
                (LobbyService.Instance as ILobbyServiceSDKConfiguration).EnableLocalPlayerLobbyEvents(true);
            }
            catch (LobbyServiceException ex)
            {
                switch (ex.Reason)
                {
                    case LobbyExceptionReason.AlreadySubscribedToLobby: Debug.LogWarning($"Already subscribed to lobby[{joinedLobby.Id}]. We did not need to try and subscribe again. Exception Message: {ex.Message}"); break;
                    case LobbyExceptionReason.SubscriptionToLobbyLostWhileBusy: Debug.LogError($"Subscription to lobby events was lost while it was busy trying to subscribe. Exception Message: {ex.Message}"); throw;
                    case LobbyExceptionReason.LobbyEventServiceConnectionError: Debug.LogError($"Failed to connect to lobby events. Exception Message: {ex.Message}"); throw;
                    default: throw;
                }
            }

            StartHeartbeat();
            StartOrStopHostWatchdog();

            onLobbyJoined?.Invoke(lobby);
        }

        public async Task LeaveJoinedLobby()
        {
            if (IsLocalPlayerInJoinedLobby())
                Debug.Log("Lobby Manager: Leaving joined lobby...");

            if (joinedLobbyEvents != null)
            {
                await joinedLobbyEvents.UnsubscribeAsync();
                joinedLobbyEvents = null;
            }

            if (IsLocalPlayerLobbyHost())
            {
                await StopHostLobby(!enableHostMigration || joinedLobby.Players.Count <= 1);
            }
            else
            {
                if (joinedLobby != null)
                {
                    var leftLobby = joinedLobby;

                    _ = RemovePlayer(joinedLobby.Id, localPlayerId);

                    joinedLobby = null;

                    StopHeartbeat();
                    StopHostWatchdog();

                    availableLobbies?.RemoveAll(lobby => lobby.Id == leftLobby.Id);

                    onLobbyLeft?.Invoke(leftLobby);

                    onLobbiesLoaded?.Invoke(availableLobbies);
                }
            }
        }

        #endregion

        #region QOL Methods

        public bool IsLocalPlayerLobbyHost(Lobby lobby) => lobby != null && lobby.HostId == localPlayerId;

        public bool IsLocalPlayerLobbyHost() => IsLocalPlayerLobbyHost(joinedLobby);

        public bool IsLocalPlayerInJoinedLobby() => IsLocalPlayerInLobby(joinedLobby);

        public bool IsLocalPlayerInLobby(Lobby lobby)
        {
            if (lobby != null && lobby.Players != null)
                foreach (var player in lobby.Players)
                    if (player.Id == localPlayerId)
                        return true;

            return false;
        }

        Player CreateLocalPlayer() => new(
        id: localPlayerId,
        data: new()
        {
            { Constants.Services.UGS.Lobbies.PlayerDataKeys.DisplayName, new(PlayerDataObject.VisibilityOptions.Member, Ref.TryGet(out ILeaderboardPlatform leaderboardPlatform) ? leaderboardPlatform.displayName : "") },
            { Constants.Services.UGS.Lobbies.PlayerDataKeys.IsReady, new(PlayerDataObject.VisibilityOptions.Member, false.ToString()) }
        }
        );

        /// <summary>
        /// Gets the local player from the joined lobby
        /// </summary>
        /// <returns></returns>
        public Player GetLocalPlayer() => joinedLobby?.Players.Find(player => player.Id == localPlayerId);

        public ServiceRateLimiter GetRateLimit(RequestType type)
        {
            switch (type)
            {
                case RequestType.Query:
                    return queryLobbiesRateLimiter;
                case RequestType.Join:
                    return joinLobbyRateLimiter;
                case RequestType.QuickJoin:
                    return quickJoinRateLimiter;
                case RequestType.Host:
                    return createLobbyRateLimiter;
                default:
                    return null;
            }
        }

        public string GetLobbyDataValue(Lobby lobby, string key)
        {
            if (lobby == null)
                return null;

            if (lobby.Data.TryGetValue(key, out var dataObject))
                return dataObject.Value;
            else
                return null;
        }

        public async Task<Lobby> SetLobbyDataValue(Lobby lobby, LobbyDataValue lobbyDataValue)
        {
            if (lobby == null)
                return null;

            var newData = new Dictionary<string, DataObject>(lobby.Data);
            newData[lobbyDataValue.key] = new(lobbyDataValue.visibilityOptions, lobbyDataValue.value);
            return await UpdateLobby(lobby.Id, new() { Data = newData });
        }

        public async Task<Lobby> SetLobbyDataValues(Lobby lobby, params LobbyDataValue[] values)
        {
            if (lobby == null)
                return null;

            var newData = new Dictionary<string, DataObject>(lobby.Data);
            foreach (var value in values)
                newData[value.key] = new(value.visibilityOptions, value.value);
            return await UpdateLobby(lobby.Id, new() { Data = newData });
        }

        public async Task<Lobby> SetLobbyLocked(Lobby lobby, bool locked)
        {
            if (lobby == null)
                return null;

            return await UpdateLobby(lobby.Id, new() { IsLocked = locked });
        }

        public async Task<Lobby> SetLobbyPrivate(Lobby lobby, bool isPrivate)
        {
            if (lobby == null)
                return null;
            return await UpdateLobby(lobby.Id, new() { IsPrivate = isPrivate });
        }

        public string GetPlayerDataValue(Player player, string key)
        {
            if (player.Data.TryGetValue(key, out var dataObject))
                return dataObject.Value;
            else
                return null;
        }

        public async Task<Lobby> SetPlayerDataValue(Lobby lobby, Player player, PlayerDataValue playerDataValue)
        {
            var newData = new Dictionary<string, PlayerDataObject>(player.Data);
            newData[playerDataValue.key] = new(playerDataValue.visibilityOptions, playerDataValue.value);
            return await UpdatePlayer(lobby.Id, player.Id, new() { Data = newData });
        }

        public async Task<Lobby> SetPlayerDataValues(Lobby lobby, Player player, params PlayerDataValue[] values)
        {
            var newData = new Dictionary<string, PlayerDataObject>(player.Data);
            foreach (var value in values)
                newData[value.key] = new(value.visibilityOptions, value.value);
            return await UpdatePlayer(lobby.Id, player.Id, new() { Data = newData });
        }

        #endregion

        #region Client Heartbeat Methods

        static string NowUnixSecondsString()
        {
            long unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return unix.ToString();
        }

        static long ParseUnixSecondsOr(string s, long fallback = 0)
            => (long.TryParse(s, out var v) ? v : fallback);

        bool TryGetPlayerData(Player p, string key, out string value)
        {
            value = null;
            if (p == null || p.Data == null) return false;
            if (p.Data.TryGetValue(key, out var obj))
            {
                value = obj?.Value;
                return true;
            }
            return false;
        }

        Task UpdateLocalPlayerDataAsync(Dictionary<string, PlayerDataObject> delta, bool waitForLimiterCooldown = true, bool logRequest = true)
        {
            if (joinedLobby == null) return Task.CompletedTask;
            var localPlayer = GetLocalPlayer();
            if (localPlayer == null) return Task.CompletedTask;
            return UpdatePlayer(joinedLobby.Id, localPlayer.Id, new UpdatePlayerOptions { Data = delta }, waitForLimiterCooldown, logRequest);
        }

        void StartHeartbeat()
        {
            if (heartbeatCoroutine != null) return;
            if (!IsLocalPlayerInJoinedLobby()) return;
            heartbeatCoroutine = StartCoroutine(DoHeartbeat());
        }

        void StopHeartbeat()
        {
            if (heartbeatCoroutine != null)
                StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }

        IEnumerator DoHeartbeat()
        {
            // Write an immediate first beat
            _ = UpdateLocalPlayerDataAsync(new Dictionary<string, PlayerDataObject>
            {
                [kHeartbeatKey] = new(PlayerDataObject.VisibilityOptions.Member, NowUnixSecondsString())
            });

            var wait = new WaitForSecondsRealtime(Mathf.Max(2f, heartbeatIntervalSec));
            while (IsLocalPlayerInJoinedLobby())
            {
                // Rate-limited in your UpdatePlayer() path; this aligns with the limiter (5 per 5s).
                _ = UpdateLocalPlayerDataAsync(new Dictionary<string, PlayerDataObject>
                {
                    [kHeartbeatKey] = new(PlayerDataObject.VisibilityOptions.Member, NowUnixSecondsString())
                }, waitForLimiterCooldown: false, logRequest: false);

                yield return wait;
            }

            heartbeatCoroutine = null;
        }

        void StartOrStopHostWatchdog()
        {
            if (IsLocalPlayerLobbyHost(joinedLobby))
            {
                if (hostWatchdogCoroutine == null)
                    hostWatchdogCoroutine = StartCoroutine(DoStaleKickWatchdog());
            }
            else
            {
                StopHostWatchdog();
            }
        }

        void StopHostWatchdog()
        {
            if (hostWatchdogCoroutine != null)
                StopCoroutine(hostWatchdogCoroutine);
            hostWatchdogCoroutine = null;
        }

        IEnumerator DoStaleKickWatchdog()
        {
            var wait = new WaitForSecondsRealtime(3f); // check slightly faster than heartbeat interval
            while (IsLocalPlayerLobbyHost(joinedLobby) && joinedLobby != null)
            {
                // Get a stable snapshot reference (joinedLobby can be changed by callbacks)
                var snapshot = joinedLobby;

                if (snapshot?.Players != null)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    for (int i = snapshot.Players.Count - 1; i >= 0; i--)
                    {
                        var p = snapshot.Players[i];
                        if (p == null) continue;

                        // Never kick yourself here
                        if (p.Id == localPlayerId) continue;

                        // If the player explicitly marked sleeping, skip kicking.
                        if (TryGetPlayerData(p, kSleepingKey, out var sleepingStr) &&
                            bool.TryParse(sleepingStr, out var sleeping) && sleeping)
                        {
                            continue;
                        }

                        // Read their last heartbeat
                        long lastBeat = 0;
                        if (!TryGetPlayerData(p, kHeartbeatKey, out var beatStr))
                        {
                            // If they never wrote, treat join time as "recent" to avoid false kicks
                            lastBeat = now;
                        }
                        else
                        {
                            lastBeat = ParseUnixSecondsOr(beatStr, 0);
                        }

                        // Consider them stale if too old (allowing clock skew)
                        if (now - lastBeat >= (long)heartbeatTimeoutSec)
                        {
                            Debug.LogWarning($"Lobby Watchdog: Kicking stale player {p.Id} (lastBeat={lastBeat}, now={now})");
                            // Fire-and-forget; rate limiter will gate
                            _ = RemovePlayer(snapshot.Id, p.Id);
                        }
                    }
                }

                yield return wait;
            }

            hostWatchdogCoroutine = null;
        }


        #endregion

        #region Event Handlers

        private void Ref_OnRegistered(Type type, object instance)
        {
            var ugsManager = instance as UGSManager;

            if (ugsManager != null)
            {
                ugsManager.OnLogInChanged += UgsManager_OnLogInChanged;
                UgsManager_OnLogInChanged(ugsManager.isLoggedIn);
            }
        }

        private void Ref_OnUnregistered(Type type, object instance)
        {
            var ugsManager = instance as UGSManager;

            if (ugsManager != null)
            {
                ugsManager.OnLogInChanged -= UgsManager_OnLogInChanged;
                UgsManager_OnLogInChanged(false);
            }
        }

        private void UgsManager_OnLogInChanged(bool isLoggedIn)
        {
            if (isLoggedIn)
            {
                AuthenticationService.Instance.SignedIn += AuthenticationService_OnSignedIn;
                AuthenticationService.Instance.SignedOut += AuthenticationService_OnSignedOut;

                if (AuthenticationService.Instance.IsSignedIn)
                    AuthenticationService_OnSignedIn();
            }
            else
            {
                AuthenticationService.Instance.SignedIn -= AuthenticationService_OnSignedIn;
                AuthenticationService.Instance.SignedOut -= AuthenticationService_OnSignedOut;

                AuthenticationService_OnSignedOut();
            }
        }

        void AuthenticationService_OnSignedIn()
        {
            // Once the player is authenticated, we can get the player ID. The Lobby Manager is considered initialized at this point.
            localPlayerId = AuthenticationService.Instance.PlayerId;
        }

        void AuthenticationService_OnSignedOut()
        {
            EndConnectionsImmediately();
        }

        private void ApplicationInfo_OnInternetAvailabilityChanged(ApplicationInfo.InternetAvailabilityStatus status)
        {
            bool isInternetAvailable = status == ApplicationInfo.InternetAvailabilityStatus.Online;

            if (!isInternetAvailable)
                EndConnectionsImmediately();
        }

        #endregion

        #region Lobby Service API Calls

        async Task<Lobby> CreateLobby(string name, int maxPlayers, CreateLobbyOptions options = null, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await createLobbyRateLimiter.QueueUntilCooldown();
            else if (!createLobbyRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log($"Lobby Manager: Request - Creating lobby {name}...");
                var lobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, options);
                Debug.Log($"Lobby Manager: Lobby {lobby.Name} created!");
                return lobby;
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task<Lobby> GetLobby(string ID, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await getLobbyRateLimiter.QueueUntilCooldown();
            else if (!getLobbyRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log($"Lobby Manager: Request - Getting lobby {ID}...");
                return await LobbyService.Instance.GetLobbyAsync(ID);
            }
            catch (LobbyServiceException e) { Debug.Log(e); }
            return null;
        }

        public async Task<Lobby> UpdateLobby(string ID, UpdateLobbyOptions options, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await updateLobbyRateLimiter.QueueUntilCooldown();
            else if (!updateLobbyRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log($"Lobby Manager: Request - Updating lobby {ID}...");
                return await LobbyService.Instance.UpdateLobbyAsync(ID, options);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task DeleteLobby(string ID, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await deleteLobbyRateLimiter.QueueUntilCooldown();
            else if (!deleteLobbyRateLimiter.TryAcquire()) return;

            try
            {
                Debug.Log($"Lobby Manager: Request - Deleting lobby {ID}...");
                await LobbyService.Instance.DeleteLobbyAsync(ID);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
        }

        async Task SendHeartbeatPing(string ID, bool waitForLimiterCooldown = true)
        {
            // Host lobby heartbeat = MUST-RUN → we wait by default.
            if (waitForLimiterCooldown) await lobbyHeartbeatRateLimiter.QueueUntilCooldown();
            else if (!lobbyHeartbeatRateLimiter.TryAcquire()) return;

            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(ID);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
        }

        public async Task<List<Lobby>> QueryLobbies(QueryLobbiesOptions options = null, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await queryLobbiesRateLimiter.QueueUntilCooldown();
            else if (!queryLobbiesRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log("Lobby Manager: Request - Querying lobbies...");
                var queryResponse = await Lobbies.Instance.QueryLobbiesAsync(options);
                return queryResponse.Results;
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task<Lobby> JoinLobbyByID(string ID, JoinLobbyByIdOptions options = null, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await joinLobbyRateLimiter.QueueUntilCooldown();
            else if (!joinLobbyRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log($"Lobby Manager: Request - Joining lobby {ID}...");
                return await LobbyService.Instance.JoinLobbyByIdAsync(ID, options);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task<Lobby> JoinLobbyByCode(string code, JoinLobbyByCodeOptions options = null, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await joinLobbyRateLimiter.QueueUntilCooldown();
            else if (!joinLobbyRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log($"Lobby Manager: Request - Joining lobby by code {code}...");
                return await LobbyService.Instance.JoinLobbyByCodeAsync(code, options);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task<Lobby> QuickJoinLobby(QuickJoinLobbyOptions options = null, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await quickJoinRateLimiter.QueueUntilCooldown();
            else if (!quickJoinRateLimiter.TryAcquire()) return null;

            try
            {
                Debug.Log("Lobby Manager: Request - Quick joining lobby...");
                return await LobbyService.Instance.QuickJoinLobbyAsync(options);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task<Lobby> UpdatePlayer(string lobbyID, string playerID, UpdatePlayerOptions options, bool waitForLimiterCooldown = true, bool logRequest = true)
        {
            // Client heartbeats & presence flips can call with waitForLimiterCooldown=false (best-effort).
            if (waitForLimiterCooldown) await updatePlayersRateLimiter.QueueUntilCooldown();
            else if (!updatePlayersRateLimiter.TryAcquire()) return null;

            try
            {
                if (logRequest)
                    Debug.Log($"Lobby Manager: Request - Updating player {playerID} in lobby {lobbyID}...");
                return await LobbyService.Instance.UpdatePlayerAsync(lobbyID, playerID, options);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
            return null;
        }

        async Task RemovePlayer(string lobbyID, string playerID, bool waitForLimiterCooldown = true)
        {
            if (waitForLimiterCooldown) await leaveLobbyOrRemovePlayerRateLimiter.QueueUntilCooldown();
            else if (!leaveLobbyOrRemovePlayerRateLimiter.TryAcquire()) return;

            try
            {
                Debug.Log($"Lobby Manager: Request - Removing player {playerID} from lobby {lobbyID}...");
                await LobbyService.Instance.RemovePlayerAsync(lobbyID, playerID);
            }
            catch (LobbyServiceException e) { Debug.LogError(e); }
        }


        #endregion
    }
}
