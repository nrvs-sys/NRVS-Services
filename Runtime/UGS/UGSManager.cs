using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Authentication;
using UnityEngine.Events;
using System.Threading;

namespace Services.UGS
{
    /// <summary>
    /// TODO: read DSA-required notifications to users (https://docs.unity.com/ugs/en-us/manual/authentication/manual/dsa-notifications)
    /// </summary>
    public class UGSManager : MonoBehaviour, IPlatform
    {
        const string lastNotificationReadDateKey = "UGS_Authentication_NotificationReadDate";

        [SerializeField]
        bool attemptPlatformAuthentication = false;

        public UnityEvent onInitialize;

        public string playerID => AuthenticationService.Instance.PlayerId;

        private string _displayName;
        public string displayName
        {
            get => _displayName;
            private set
			{
                if (_displayName == value)
                    return;

                _displayName = value;

                OnDisplayNameChanged?.Invoke(_displayName);
            }
        }

        public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

        public bool isAuthenticated => AuthenticationService.Instance.IsAuthorized;

        public string loginState
        {
            get;
            private set;
        } = "Uninitialized";

        private bool _isLoggedIn;
        public bool isLoggedIn
        {
            get => _isLoggedIn;
            private set
            {
                if (_isLoggedIn == value)
                    return;

                _isLoggedIn = value;

                OnLogInChanged?.Invoke(_isLoggedIn);
            }
        }

        public bool hasLoginError
        {
            get;
            private set;
        }

        public event LogInHandler OnLogInChanged;
        public delegate void LogInHandler(bool isLoggedIn);

        private int _loginInProgressFlag = 0;

        Queue<Notification> notifications = new();


        void Start()
        {
            Ref.Register<UGSManager>(this);

            ApplicationInfo.OnInternetAvailabilityChanged += ApplicationInfo_OnInternetAvailabilityChanged;

            _ = TryLogInAsync();
        }

        void OnDestroy()
        {
            Ref.Unregister<UGSManager>(this);

            ApplicationInfo.OnInternetAvailabilityChanged -= ApplicationInfo_OnInternetAvailabilityChanged;
        }


        public async Task TryLogInAsync()
        {
            if (isLoggedIn) return;

            // allow only one concurrent attempt
            if (Interlocked.CompareExchange(ref _loginInProgressFlag, 1, 0) != 0)
                return;

            try
            {
                await LogInAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _loginInProgressFlag, 0);
            }
        }

        private async Task LogInAsync()
        {
            var initializationOptions = new InitializationOptions();

#if UNITY_EDITOR
            // If this is a Parrel Sync instance, set the profile to the instance type in order to prevent conflicts
            var profileName = "ParrelSync_Main"; // Default profile name for the main instance
            if (ParrelSyncManager.type != ParrelSyncManager.ParrelInstanceType.Main)
            {
                var arg = ParrelSyncManager.GetArgument();
                profileName = $"ParrelSync_{arg}";
                initializationOptions.SetProfile(profileName);
            }
#endif

#if DEMO_MODE
            // In demo mode, set the environment to "demo"
            initializationOptions.SetEnvironmentName("demo");
#endif

            //Initialize the Unity Services engine
            loginState = "Initializing";
            try
            {
                // avoid re-initializing if already initialized
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync(initializationOptions);

                    AuthenticationService.Instance.SignedIn += AuthenticationService_SignedIn;
                    AuthenticationService.Instance.SignedOut += AuthenticationService_SignedOut;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                loginState = $"Initialization failed: {e}";
                hasLoginError = true;
                return;
            }

            displayName = AuthenticationService.Instance.PlayerName;

            onInitialize?.Invoke();


            // wait (briefly) for Unknown ? Online/Offline
            var status = await ApplicationInfo.WaitForKnownInternetStatusAsync(timeout: TimeSpan.FromSeconds(5));

            // if still Unknown (timeout) or explicitly Offline, bail
            if (status != ApplicationInfo.InternetAvailabilityStatus.Online)
            {
                Debug.LogError($"UGSManager: Internet is available! Unable to sign in to authentication services.");

                loginState = $"Sign in failed: offline";
                hasLoginError = true;

                return;
            }


#if UNITY_EDITOR
            // If this is a Parrel Sync instance, sign in anonymously
            if (!AuthenticationService.Instance.IsSignedIn && ParrelSyncManager.type != ParrelSyncManager.ParrelInstanceType.Main)
            {
                await SignInAnonymouslyAsync(profileName);
            }
#endif

            // If desired, attempt sign in with the current platform
            if (!AuthenticationService.Instance.IsSignedIn && attemptPlatformAuthentication)
            {
#if PLAYERPLATFORM_STANDALONE
            await SignInAnonymouslyAsync();
#elif PLAYERPLATFORM_OCULUS
                IPlayerPlatform platform = null;

                while (!Ref.TryGet(out platform))
                    await Task.Yield();

                var oculusManager = platform as OculusManager;

                if (oculusManager != null)
                {
                    // Wait for the PlayerPlatform to initialize
                    while (!oculusManager.hasUserProof)
                        await Task.Yield();

                    await SignInOculusAsync(oculusManager.userProof, oculusManager.userID.ToString());
                }
#elif PLAYERPLATFORM_STEAM
                IPlayerPlatform platform = null;
                while (!Ref.TryGet(out platform)) await Task.Yield();
                var steamManager = platform as SteamManager;
                if (steamManager != null)
                {
                    while (!steamManager.isLoggedIn) await Task.Yield();
                    const string steamIdentity = "unityauthenticationservice";
                    var steamSessionTicket = await steamManager.GetAuthorizationTicketAsync(steamIdentity);
                    await SignInSteamAsync(steamSessionTicket, steamIdentity);
                }
#endif
            }

            if (!AuthenticationService.Instance.IsSignedIn)
                await SignInAnonymouslyAsync();

            // Only set isLoggedIn to true when sign in was successful
            if (AuthenticationService.Instance.IsSignedIn)
            {
                displayName = AuthenticationService.Instance.PlayerName;

                loginState = "Logged in";
                isLoggedIn = true;
            }
        }

        void AuthenticationService_SignedIn()
        {
            Debug.Log("Unity Game Services: Authentication Signed In");
        }

        void AuthenticationService_SignedOut()
        {
            Debug.Log("Unity Game Services: Authentication Signed Out");
        }

        #region Authentication Methods

        async Task SignInAnonymouslyAsync(string anonymousPlayerName = "Anonymous")
        {
            loginState = "Signing in anonymously";

            try
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log("Unity Game Services: Sign in anonymously succeeded!");

                // Shows how to get the playerID
                Debug.Log($"Unity Game Services PlayerID: {AuthenticationService.Instance.PlayerId}");

                // Update the player name to Anonymous
                try
                {
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(anonymousPlayerName);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"UGSManager: Failed to update player name to '{anonymousPlayerName}': {ex}");
                }

                await VerifyLastNotificationDateAsync();
            }
            catch (AuthenticationException ex)
            {
                foreach (var notification in ex.Notifications)
                    notifications.Enqueue(notification);

                Debug.LogException(ex);

                loginState = $"Anonymous sign in authentication failed: {ex}";
                hasLoginError = true;
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);

                loginState = $"Anonymous sign in request failed: {ex}";
                hasLoginError = true;
            }
        }

        async Task SignInSteamAsync(string ticket, string identity)
        {
            loginState = "Signing in with Steam";

            try
            {
                await AuthenticationService.Instance.SignInWithSteamAsync(ticket, identity);

                Debug.Log("Unity Game Services: Sign in with Steam succeeded!");

                // Shows how to get the playerID
                Debug.Log($"Unity Game Services PlayerID: {AuthenticationService.Instance.PlayerId}");

                // Wait for the platform to be ready
                var platform = Ref.Get<IPlayerPlatform>();
                while (platform == null || !platform.isLoggedIn)
                {
                    await Task.Yield();
                    platform = Ref.Get<IPlayerPlatform>();
                }

                // Update the player name to Steam user name
                await AuthenticationService.Instance.UpdatePlayerNameAsync(platform.displayName);

                await VerifyLastNotificationDateAsync();
            }
            catch (AuthenticationException ex)
            {
                foreach (var notification in ex.Notifications)
                    notifications.Enqueue(notification);

                Debug.LogException(ex);

                loginState = $"Steam sign in authentication failed: {ex}";
                hasLoginError = true;
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);

                loginState = $"Steam sign in request failed: {ex}";
                hasLoginError = true;
            }
        }

        async Task SignInOculusAsync(string nonce, string userID)
        {
            loginState = "Signing in with Oculus";

            try
            {
                await AuthenticationService.Instance.SignInWithOculusAsync(nonce, userID);

                Debug.Log("Unity Game Services: Sign in with Oculus succeeded!");

                // Shows how to get the playerID
                Debug.Log($"Unity Game Services PlayerID: {AuthenticationService.Instance.PlayerId}");

                // Wait for the platform to be ready
                var platform = Ref.Get<IPlayerPlatform>();
                while (platform == null || !platform.isLoggedIn)
                {
                    await Task.Yield();
                    platform = Ref.Get<IPlayerPlatform>();
                }

                // Update the player name to Oculus user name
                await AuthenticationService.Instance.UpdatePlayerNameAsync(platform.displayName);

                await VerifyLastNotificationDateAsync();
            }
            catch (AuthenticationException ex)
            {
                if (ex.Notifications != null)
                    foreach (var notification in ex.Notifications)
                        notifications.Enqueue(notification);

                Debug.LogException(ex);

                loginState = $"Oculus sign in authentication failed: {ex}";
                hasLoginError = true;
            }
            catch (RequestFailedException ex)
            {
                Debug.LogException(ex);

                loginState = $"Oculus sign in request failed: {ex}";
                hasLoginError = true;
            }
        }


        #endregion

        #region Notification Methods

        async Task VerifyLastNotificationDateAsync()
        {
            // Verify the LastNotificationDate
            var lastNotificationDate = AuthenticationService.Instance.LastNotificationDate;
            long storedNotificationDate = GetLastNotificationReadDate();
            // Verify if the LastNotification date is available and greater than the last read notifications
            if (lastNotificationDate != null && long.Parse(lastNotificationDate) > storedNotificationDate)
            {
                // Retrieve the notifications from the backend
                foreach (var notification in await AuthenticationService.Instance.GetNotificationsAsync())
                    notifications.Enqueue(notification);
            }
        }

        public bool HasNotifications() => notifications.Count > 0;

        public void ReadNotification(Action<Notification> onNotificationRead)
        {
            if (notifications.Count > 0)
            {
                var notification = notifications.Dequeue();

                onNotificationRead?.Invoke(notification);

                long storedNotificationDate = GetLastNotificationReadDate();
                var notificationDate = long.Parse(notification.CreatedAt);
                if (notificationDate > storedNotificationDate)
                {
                    SaveNotificationReadDate(notificationDate);
                }
            }
        }

        public void ReadAllNotifications(Action<Notification> onNotificationRead)
        {
            while (notifications.Count > 0)
            {
                ReadNotification(onNotificationRead);
            }
        }

        void SaveNotificationReadDate(long notificationReadDate)
        {
            PlayerPrefs.SetString(lastNotificationReadDateKey, notificationReadDate.ToString());
        }

        long GetLastNotificationReadDate()
        {
            return long.Parse(PlayerPrefs.GetString(lastNotificationReadDateKey, "0"));
        }

        #endregion



        private void ApplicationInfo_OnInternetAvailabilityChanged(ApplicationInfo.InternetAvailabilityStatus status)
        {
            bool isInternetAvailable = status == ApplicationInfo.InternetAvailabilityStatus.Online;

            if (isInternetAvailable && !isLoggedIn)
            {
                _ = TryLogInAsync();
            }
        }
    }
}
