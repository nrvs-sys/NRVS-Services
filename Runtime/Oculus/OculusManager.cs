using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_OCULUS_SUPPORT
using Oculus.Platform;
using Oculus.Platform.Models;
#endif
using UnityEngine.Events;
using UnityEngine.XR;

using Settings = UnityEngine.XR.XRSettings;

public class OculusManager : Singleton<OculusManager>, IPlayerPlatform
{
	[Header("Settings")]

	[SerializeField]
	[Tooltip("Enable Dynamic Resolution. This will allocate render buffers to maxDynamicResolutionScale size and " +
			 "will change the viewport to adapt performance.")]
	public bool enableDynamicResolution = false;

	[SerializeField]
	[Tooltip("Minimum scaling factor used when dynamic resolution is enabled.")]
	[RangeAttribute(0.7f, 1.3f)]
	public float minDynamicResolutionScale = 1.0f;

	[SerializeField]
	[Tooltip("Maximum scaling factor used when dynamic resolution is enabled.")]
	[RangeAttribute(0.7f, 1.3f)]
	public float maxDynamicResolutionScale = 1.0f;

	private const int _pixelStepPerFrame = 32;

	[Header("Events")]
	public UnityEvent onEntitlementPass;
	public UnityEvent onEntitlementFail;
	public UnityEvent<string> onDisplayNameSet;
	public UnityEvent<ulong> onUserIDSet;


	private string _displayName;
	public string displayName
	{
		get => _displayName;
		private set
		{
			if (value == _displayName)
				return;

			_displayName = value;

			onDisplayNameSet?.Invoke(_displayName);
			OnDisplayNameChanged?.Invoke(_displayName);
		}
	}

	private ulong _userID;
	public ulong userID
	{
		get => _userID;
		private set
		{
			if (value == _userID)
				return;

			_userID = value;

			onUserIDSet?.Invoke(_userID);
		}
	}

	public string playerID => userID != default ? userID.ToString() : string.Empty;

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

	public string userProof
	{
		get;
		private set;
	}

	public event Action<string> OnAchievementUnlocked;
	public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

	public bool hasUserProof => !string.IsNullOrEmpty(userProof);

	private readonly HashSet<string> _unlocked = new();
	private readonly Queue<System.Action> _offlineQueue = new();

	private const float FlushInterval = 5f;
	private readonly Dictionary<string, int> _pending = new();
	private float _lastFlush;

#if ENABLE_OCULUS_SUPPORT

	protected override void OnSingletonInitialized()
	{
		Ref.Register<IPlayerPlatform>(this);

		loginState = "Initializing";

		try
		{
			Core.AsyncInitialize().OnComplete(CoreInitializeCallback);
		}
		catch (UnityException e)
		{
			Debug.LogErrorFormat("Oculus Manager: Platform failed to initialize due to exception: %s.", e.Message);

			UnityEngine.Application.Quit();
		}
	}


	void Start()
	{
		if (enableDynamicResolution)
			XRSettings.eyeTextureResolutionScale = maxDynamicResolutionScale;

		ApplicationInfo.OnInternetAvailabilityChanged += ApplicationInfo_OnInternetAvailabilityChanged;
	}

	private void OnDestroy()
	{
		Ref.Unregister<IPlayerPlatform>(this);

		ApplicationInfo.OnInternetAvailabilityChanged -= ApplicationInfo_OnInternetAvailabilityChanged;
	}




	private void Update()
	{
		if (enableDynamicResolution)
		{
			OVRPlugin.Sizei recommendedResolution;
			if (OVRPlugin.GetEyeLayerRecommendedResolution(out recommendedResolution))
			{
				// Don't scale up or down more than a certain number of pixels per frame to avoid submitting a viewport that has disabled tiles.
				recommendedResolution.w = Math.Max(recommendedResolution.w,
					(int)(Settings.eyeTextureWidth * XRSettings.renderViewportScale) - _pixelStepPerFrame);
				recommendedResolution.w = Math.Min(recommendedResolution.w,
					(int)(Settings.eyeTextureWidth * XRSettings.renderViewportScale) + _pixelStepPerFrame);

				float scalingFactor = recommendedResolution.w / (float)Settings.eyeTextureWidth;
				scalingFactor = Math.Max(scalingFactor, minDynamicResolutionScale / maxDynamicResolutionScale);
				scalingFactor = Math.Min(scalingFactor, 1.0f);

				XRSettings.renderViewportScale = scalingFactor;
			}
		}

		// Batch AddToStat API calls
		if (isLoggedIn && Time.unscaledTime - _lastFlush >= FlushInterval)
		{
			FlushPendingCounts();
			_lastFlush = Time.unscaledTime;
		}
	}

	private void CoreInitializeCallback(Message<PlatformInitialize> msg)
	{
		if (msg.IsError)
		{
			var err = msg.GetError();
			Debug.LogErrorFormat("Platform failed to initialize due to exception: %s.", err.ToString());

			loginState = $"Platform initialize failed ({err.Message})";
			hasLoginError = true;

			UnityEngine.Application.Quit();
		}
		else
		{
			Entitlements.IsUserEntitledToApplication().OnComplete(EntitlementCheckCallback);
		}
	}

	// Called when the Oculus Platform completes the async entitlement check request and a result is available.
	void EntitlementCheckCallback(Message msg)
	{
		// If the user passed the entitlement check, msg.IsError will be false.
		// If the user failed the entitlement check, msg.IsError will be true.
		HandleEntitlementCheckResult(msg.IsError == false);
	}

	void HandleEntitlementCheckResult(bool result)
	{
		if (result) // User passed entitlement check
		{
			Debug.Log("Oculus Manager: Oculus user entitlement check successful.");

			loginState = "Entitlement passed";

			Users.GetLoggedInUser().OnComplete(GetLoggedInUserCallback);

			onEntitlementPass?.Invoke();
		}
		else // User failed entitlement check
		{
			Debug.LogError("Oculus Manager: Oculus user entitlement check failed! Closing application...");

			loginState = "Entitlement failed";
			hasLoginError = true;

			onEntitlementFail?.Invoke();


			UnityEngine.Application.Quit();
		}
	}

	private void GetLoggedInUserCallback(Message<User> message)
	{
		if (!message.IsError)
		{
			var user = message.GetUser();
			if (user != null)
			{
				displayName = user.DisplayName;
				userID = user.ID;

				Debug.Log($"Oculus Manager: Oculus logged in with initial User info: ({userID}) {displayName}");
			}
			else
			{
				Debug.LogWarning("Oculus Manager: Oculus logged in user was null.");
			}

			if (string.IsNullOrEmpty(displayName) && userID > 0)
			{
				Debug.LogWarning($"Oculus Manager: Oculus display name was empty. Trying again to get the Oculus user display name");

				Users.Get(userID).OnComplete(userCallback =>
				{
					if (userCallback == null || userCallback.IsError)
					{
						Debug.LogWarning($"Oculus Manager: Unable to get user info for userID: {userID}. Display name will be empty.");
					}
					else
					{
						displayName = userCallback.GetUser().DisplayName;

						Debug.Log($"Oculus Manager: Oculus user retrieved: ({userID}) {displayName}");
					}

					SetLoggedIn();
				});
			}
			else
				SetLoggedIn();

			GetUserProof();
		}
		else
		{
			loginState = $"Login failed ({message.GetError().Message})";
			hasLoginError = true;
		}
	}

	private void SetLoggedIn()
	{
		Debug.Log($"Oculus Manager: Oculus logged in user: ({userID}) {displayName}");

		isLoggedIn = true;
		loginState = "Logged in";

		PullInitialAchievementCache();
		Flush();                 // push anything queued while offline
	}

	private void GetUserProof()
	{
		if (hasUserProof)
			return;

		// An internet connection is required to use Users.GetUserProof()
		if (ApplicationInfo.internetAvailabilityStatus != ApplicationInfo.InternetAvailabilityStatus.Online)
		{
			Debug.LogError($"Oculus Manager: Internet is unavailable! Unable to get user proof.");
			return;
		}

		Users.GetUserProof().OnComplete(OnUserProofCallback);
	}

	private void OnUserProofCallback(Message<UserProof> msg)
	{
		if (msg.IsError)
		{
			Debug.LogErrorFormat("Oculus Manager: Error getting user proof. Error Message: {0}",
				msg.GetError().Message);
		}
		else
		{
			string oculusNonce = msg.Data.Value;
			userProof = oculusNonce;
		}
	}

    #region Achievements

	public bool IsAchievementUnlocked(string key) => _unlocked.Contains(key);

	public void UnlockAchievement(string key)
	{
		// avoid spam if we already know it's done
		if (_unlocked.Contains(key)) return;

		void DoUnlock()
		{
			Achievements.Unlock(key).OnComplete(msg =>
			{
				if (!msg.IsError)
				{
					_unlocked.Add(key);
					OnAchievementUnlocked?.Invoke(key);
					Debug.Log($"Oculus Manager: Achievement unlock ({key}) succeeded!");
				}
				else
					Debug.LogError($"Oculus Manager: Achievement unlock ({key}) failed: {msg.GetError().Message}");
			});
		}

		PushOrQueue(DoUnlock);
	}

	public int AddToStat(string statKey, int delta)
	{
		if (_unlocked.Contains(statKey)) return 0;

		if (_pending.TryGetValue(statKey, out var amt))
			_pending[statKey] = amt + delta;
		else
			_pending[statKey] = delta;

		return delta;
	}

	// Not used for this platform
	public void CaptureStats() { }

	private void FlushPendingCounts()
	{
		if (_pending.Count == 0) return;

		// copy to avoid modifying collection while iterating
		var snapshot = new Dictionary<string, int>(_pending);
		_pending.Clear();

		foreach (var kvp in snapshot)
		{
			string key = kvp.Key;
			int amt = kvp.Value;

			// skip tiers that unlocked while waiting
			if (_unlocked.Contains(key)) continue;

			void Send() =>
				Achievements.AddCount(key, (ulong)amt).OnComplete(msg =>
				{
					if (msg.IsError)
					{
						Debug.LogError($"Oculus Manager: AddCount({key},{amt}) failed: {msg.GetError().Message}");
						return;
					}

					if (msg.Data.JustUnlocked && !_unlocked.Contains(msg.Data.Name))
					{
						_unlocked.Add(msg.Data.Name);
						OnAchievementUnlocked?.Invoke(msg.Data.Name);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
						Debug.Log($"Oculus Manager: Achievement unlock ({key}) succeeded!");
#endif
					}
				});

			PushOrQueue(Send);
		}
	}


	public void Flush()
	{
		// Quest writes are immediate; just run queued ops if we’re back online
		if (isLoggedIn)
		{
			FlushPendingCounts();
			while (_offlineQueue.Count > 0)
				_offlineQueue.Dequeue().Invoke();
		}
	}

	private void PushOrQueue(System.Action action)
	{
		if (isLoggedIn)
			action.Invoke();
		else
			_offlineQueue.Enqueue(action);   // replay in Flush()
	}

	private void PullInitialAchievementCache()
	{
		Achievements.GetAllProgress().OnComplete(r =>
		{
			if (r.IsError) { Debug.LogWarning("Oculus Manager: Could not pre-fill achievement cache."); return; }

			foreach (var p in r.Data)
			{
				if (p.IsUnlocked) _unlocked.Add(p.Name);
			}
		});
	}

    #endregion

	// Try to complete any missing user info when internet becomes available
	private void TryRefreshUser()
	{
		// Only run if we're online
		if (ApplicationInfo.internetAvailabilityStatus != ApplicationInfo.InternetAvailabilityStatus.Online)
			return;

		// If we never obtained the user, try again
		if (!isLoggedIn)
		{
			Users.GetLoggedInUser().OnComplete(GetLoggedInUserCallback);
			return;
		}

		// If the display name is empty, try to fetch it using the known user id
		if (string.IsNullOrEmpty(displayName))
		{
			Users.Get(userID).OnComplete(userCallback =>
			{
				if (userCallback == null || userCallback.IsError)
				{
					Debug.LogWarning($"Oculus Manager: Unable to refresh user info for userID: {userID}. Display name will remain empty.");
				}
				else
				{
					displayName = userCallback.GetUser().DisplayName;
					Debug.Log($"Oculus Manager: Refreshed Oculus user display name after reconnect: ({userID}) {displayName}");
				}

				// If we were marked offline, restore logged-in state now that we can talk to the service
				if (!isLoggedIn)
				{
					isLoggedIn = true;
					loginState = "Logged in";
					Flush();
				}
			});
		}
		else
		{
			// We have a display name already; ensure we're marked online/logged in and flush any pending work
			if (!isLoggedIn)
			{
				isLoggedIn = true;
				loginState = "Logged in";
				Flush();
			}
		}
	}

	private void ApplicationInfo_OnInternetAvailabilityChanged(ApplicationInfo.InternetAvailabilityStatus status)
	{
		if (status == ApplicationInfo.InternetAvailabilityStatus.Online)
		{
			// Recover any missing data (in case the app started offline)
			TryRefreshUser();

			// When internet status is online, try to get user proof again (in case the app started offline)
			GetUserProof();
		}
	}
#else

    protected override void OnSingletonInitialized()
    {
        Debug.LogError("Oculus Manager: Oculus support is not enabled in project settings. OculusManager will not function.");
    }

    public bool IsAchievementUnlocked(string key)
    {
		Debug.LogError("Oculus Manager: Oculus support is not enabled in project settings. IsAchievementUnlocked will always return false.");
		return false;
    }

    public void UnlockAchievement(string key)
    {
        Debug.LogError("Oculus Manager: Oculus support is not enabled in project settings. UnlockAchievement will not function.");
    }

    public int AddToStat(string statKey, int delta)
    {
        Debug.LogError("Oculus Manager: Oculus support is not enabled in project settings. AddToStat will not function.");
        return 0;
    }

    public void CaptureStats()
    {
        Debug.LogError("Oculus Manager: Oculus support is not enabled in project settings. CaptureStats will not function.");
    }

    public void Flush()
    {
        Debug.LogError("Oculus Manager: Oculus support is not enabled in project settings. Flush will not function.");
    }

#endif
}
