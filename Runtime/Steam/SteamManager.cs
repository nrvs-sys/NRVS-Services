// The SteamManager is designed to work with Steamworks.NET
// This file is released into the public domain.
// Where that dedication is not recognized you are granted a perpetual,
// irrevocable license to copy and modify this file as you see fit.
//
// Version: 1.0.12

#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
#if !DISABLESTEAMWORKS
using System.Collections;
using Steamworks;
using WebSocketSharp;
#endif

//
// The SteamManager provides a base implementation of Steamworks.NET on which you can build upon.
// It handles the basics of starting up and shutting down the SteamAPI for use.
//
[DisallowMultipleComponent]
public class SteamManager : MonoBehaviour, IPlayerPlatform
{
	[SerializeField]
	ulong applicationID;

	[SerializeField]
	ulong demoApplicationID;

    /// <summary>
    /// Returns the Steam ID of the currently logged in player.
    /// </summary>
    public string playerID
	{
		get;
		private set;
	}

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

    public string loginState
    {
        get;
        private set;
    } = "Uninitialized";

    public bool hasLoginError
    {
        get;
        private set;
    }

    public bool isLoggedIn
    {
        get;
        private set;
    }

	public ulong steamID
	{
		get;
		private set;

	}

	public uint accountID
	{
		get;
		private set;
	}

	public event Action<string> OnAchievementUnlocked;
	public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

#if !DISABLESTEAMWORKS
	protected static bool s_EverInitialized = false;

	protected static SteamManager s_instance;
	protected static SteamManager Instance
	{
		get
		{
			if (s_instance == null)
			{
				return new GameObject("SteamManager").AddComponent<SteamManager>();
			}
			else
			{
				return s_instance;
			}
		}
	}

	protected bool m_bInitialized = false;
	public static bool Initialized
	{
		get
		{
			return Instance.m_bInitialized;
		}
	}

	/// <summary>
	/// The Steam application ID for the project.
	/// </summary>
	public ulong appID =>
#if !DEMO_MODE
		applicationID;
#else
		demoApplicationID;
#endif

    private string steamSessionAuthTicket;

    private Callback<GetTicketForWebApiResponse_t> steamAuthTicketForWebApiResponseCallback;

    protected SteamAPIWarningMessageHook_t m_SteamAPIWarningMessageHook;

	[AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
	protected static void SteamAPIDebugTextHook(int nSeverity, System.Text.StringBuilder pchDebugText)
	{
		Debug.LogWarning(pchDebugText);
	}

	// Achievements data
	private readonly HashSet<string>           _unlocked     = new();
	private readonly Queue<Action>             _offlineQueue = new();   // in case the user goes offline
	private Callback<UserStatsReceived_t>      _cbStatsReady;

#if UNITY_2019_3_OR_NEWER
	// In case of disabled Domain Reload, reset static members before entering Play Mode.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void InitOnPlayMode()
	{
		s_EverInitialized = false;
		s_instance = null;
	}
#endif

	protected virtual void Awake()
	{
		// Only one instance of SteamManager at a time!
		if (s_instance != null)
		{
			Destroy(gameObject);
			return;
		}
		s_instance = this;

		if (s_EverInitialized)
		{
			// This is almost always an error.
			// The most common case where this happens is when SteamManager gets destroyed because of Application.Quit(),
			// and then some Steamworks code in some other OnDestroy gets called afterwards, creating a new SteamManager.
			// You should never call Steamworks functions in OnDestroy, always prefer OnDisable if possible.
			throw new System.Exception("Tried to Initialize the SteamAPI twice in one session!");
		}

		Ref.Register<IPlayerPlatform>(this);
		Ref.Register<SteamManager>(this);

		loginState = "Initializing";

		// We want our SteamManager Instance to persist across scenes.
		DontDestroyOnLoad(gameObject);

		if (!Packsize.Test())
		{
			Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.", this);
		}

		if (!DllCheck.Test())
		{
			Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.", this);
		}

		try
		{
			// If Steam is not running or the game wasn't started through Steam, SteamAPI_RestartAppIfNecessary starts the
			// Steam client and also launches this game again if the User owns it. This can act as a rudimentary form of DRM.

			// Once you get a Steam AppID assigned by Valve, you need to replace AppId_t.Invalid with it and
			// remove steam_appid.txt from the game depot. eg: "(AppId_t)480" or "new AppId_t(480)".
			// See the Valve documentation for more information: https://partner.steamgames.com/doc/sdk/api#initialization_and_shutdown
			if (SteamAPI.RestartAppIfNecessary((AppId_t)appID))
			{
				Application.Quit();
				return;
			}
		}
		catch (System.DllNotFoundException e)
		{ // We catch this exception here, as it will be the first occurrence of it.
			Debug.LogError("[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.\n" + e, this);

			Application.Quit();
			return;
		}

		// Initializes the Steamworks API.
		// If this returns false then this indicates one of the following conditions:
		// [*] The Steam client isn't running. A running Steam client is required to provide implementations of the various Steamworks interfaces.
		// [*] The Steam client couldn't determine the App ID of game. If you're running your application from the executable or debugger directly then you must have a [code-inline]steam_appid.txt[/code-inline] in your game directory next to the executable, with your app ID in it and nothing else. Steam will look for this file in the current working directory. If you are running your executable from a different directory you may need to relocate the [code-inline]steam_appid.txt[/code-inline] file.
		// [*] Your application is not running under the same OS user context as the Steam client, such as a different user or administration access level.
		// [*] Ensure that you own a license for the App ID on the currently active Steam account. Your game must show up in your Steam library.
		// [*] Your App ID is not completely set up, i.e. in Release State: Unavailable, or it's missing default packages.
		// Valve's documentation for this is located here:
		// https://partner.steamgames.com/doc/sdk/api#initialization_and_shutdown
		m_bInitialized = SteamAPI.Init();
		if (!m_bInitialized)
		{
			Debug.LogError("[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information.", this);

			loginState = "Initialization failed";
			hasLoginError = true;

			return;
		}

		s_EverInitialized = true;

		InitializeUserInfo();

		_cbStatsReady = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);

        // Marked as unneeded as of this commit: https://github.com/rlabrecque/Steamworks.NET/commit/5289ecd8a3de6c0076efe096c36b95087c441eff
        //SteamUserStats.RequestCurrentStats();
    }

    // This should only ever get called on first load and after an Assembly reload, You should never Disable the Steamworks Manager yourself.
    protected virtual void OnEnable()
	{
		if (s_instance == null)
		{
			s_instance = this;
		}

		if (!m_bInitialized)
		{
			return;
		}

		if (m_SteamAPIWarningMessageHook == null)
		{
			// Set up our callback to receive warning messages from Steam.
			// You must launch with "-debug_steamapi" in the launch args to receive warnings.
			m_SteamAPIWarningMessageHook = new SteamAPIWarningMessageHook_t(SteamAPIDebugTextHook);
			SteamClient.SetWarningMessageHook(m_SteamAPIWarningMessageHook);
		}
	}

	// OnApplicationQuit gets called too early to shutdown the SteamAPI.
	// Because the SteamManager should be persistent and never disabled or destroyed we can shutdown the SteamAPI here.
	// Thus it is not recommended to perform any Steamworks work in other OnDestroy functions as the order of execution can not be garenteed upon Shutdown. Prefer OnDisable().
	protected virtual void OnDestroy()
	{
		Ref.Unregister<IPlayerPlatform>(this);
		Ref.Unregister<SteamManager>(this);

		if (s_instance != this)
		{
			return;
		}

		s_instance = null;

		if (!m_bInitialized)
		{
			return;
		}

		SteamAPI.Shutdown();
	}

	protected virtual void Update()
	{
		if (!m_bInitialized)
		{
			return;
		}

		// Run Steam client callbacks
		SteamAPI.RunCallbacks();
	}

	private void InitializeUserInfo()
	{
		var steamUserID = SteamUser.GetSteamID();
		this.steamID = steamUserID.m_SteamID;
		playerID = this.steamID.ToString();
		accountID = steamUserID.GetAccountID().m_AccountID;
		displayName = SteamFriends.GetPersonaName();

		Debug.Log($"Steam logged in user: ({playerID}) {displayName}");

		isLoggedIn = true;
		loginState = "Logged in";
	}

	public async Task<string> GetAuthorizationTicketAsync(string identity)
	{
        // Callback.Create return value must be assigned to a 
        // member variable to prevent the GC from cleaning it up.
        // Create the callback to receive events when the session ticket
        // is ready to use in the web API.
        // See GetAuthSessionTicket document for details.
        steamAuthTicketForWebApiResponseCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnAuthTicketCallback);

        SteamUser.GetAuthTicketForWebApi(identity);
        while (steamSessionAuthTicket.IsNullOrEmpty())
            await Task.Yield();

		return steamSessionAuthTicket;
	}

    void OnAuthTicketCallback(GetTicketForWebApiResponse_t callback)
    {
        steamSessionAuthTicket = BitConverter.ToString(callback.m_rgubTicket).Replace("-", string.Empty);
        steamAuthTicketForWebApiResponseCallback.Dispose();
        steamAuthTicketForWebApiResponseCallback = null;
        Debug.Log("Unity Game Services: Steam Session Ticket Recieved: " + steamSessionAuthTicket);
    }

	void OnUserStatsReceived(UserStatsReceived_t data)
	{
		if (data.m_nGameID != appID || data.m_eResult != EResult.k_EResultOK)
		{
			Debug.LogError($"SteamManager: OnUserStatsReceived Error! App will be unable to set user stats. gameID: {data.m_nGameID}, appID: {appID}, result: {data.m_eResult}");
			return;
		}
		
		var count = SteamUserStats.GetNumAchievements();
		for (uint i = 0; i < count; i++)
		{
			string key = SteamUserStats.GetAchievementName(i);
			if (SteamUserStats.GetAchievement(key, out bool achieved) && achieved)
				_unlocked.Add(key);

			Debug.Log($"SteamManager: Achievement \"{key}\" unlocked: {achieved}");
		}

		SteamUserStats.GetStat(AchievementKeys.OrbsCollected, out float orbProgress);
		Debug.Log($"SteamManager: Orb collection progress: {orbProgress}");
		Flush();   // run any queued unlocks
	}

#else
	public static bool Initialized {
		get {
			return false;
		}
	}

	public async Task<string> GetAuthorizationTicketAsync(string identity)
	{
		await Task.Yield();

		return string.Empty;
    }
#endif // !DISABLESTEAMWORKS


	#region Achievements

	public bool IsAchievementUnlocked(string key)
	{
#if !DISABLESTEAMWORKS
		return _unlocked.Contains(key);
#else
		return false;
#endif
	}

	public void UnlockAchievement(string key)
	{
#if !DISABLESTEAMWORKS
		// if we already marked it locally, bail
		if (_unlocked.Contains(key)) return;

		void DoUnlock()
		{
			if (SteamUserStats.SetAchievement(key))
			{
				_unlocked.Add(key);
				SteamUserStats.StoreStats();
				OnAchievementUnlocked?.Invoke(key);
				Debug.Log($"SteamManager: Achievement unlock ({key}) succeeded!");
			}
			else
				Debug.LogError($"SteamManager: Achievement unlock ({key}) failed.");
		}

		PushOrQueue(DoUnlock);
#endif
	}

	public int AddToStat(string statKey, int delta)
	{
		int current = 0;
#if !DISABLESTEAMWORKS
		SteamUserStats.GetStat(statKey, out current);
		current += delta;

		void DoAdd()
		{
			SteamUserStats.SetStat(statKey, current);
			Debug.Log($"SteamManager: Adding {delta} to {statKey} - current is {current}");
		}

		PushOrQueue(DoAdd);
#endif
		return current; // returns the total we *intend* to have
	}

	public void CaptureStats()
	{
#if !DISABLESTEAMWORKS
		SteamUserStats.StoreStats();
		Debug.Log($"SteamManager: Stats stored!");
#endif
	}

	public void Flush()
	{
		if (!isLoggedIn) return;

#if !DISABLESTEAMWORKS
		while (_offlineQueue.Count > 0)
			_offlineQueue.Dequeue().Invoke();
#endif
	}

	private void PushOrQueue(Action a)
	{
#if !DISABLESTEAMWORKS
		if (isLoggedIn)
			a.Invoke();
		else
			_offlineQueue.Enqueue(a);
#endif
	}

#endregion
}
