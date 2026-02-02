using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;
using UnityAtoms.BaseAtoms;
using System;
using UnityEngine.Localization;
using PlayFab.ProgressionModels;


#if UNITY_EDITOR
using ParrelSync;
#endif

public class PlayfabManager : Singleton<PlayfabManager>, ILeaderboardPlatform
{
    [Serializable]
    public struct EntryData
    {
        public int position;
        public int applicationMode;
        public int title;
        public string name;
        public int score;
        public bool completed;
        public bool isLocalUser;
        public string statisticName;

        public EntryData(int position, int applicationMode, int title, string name, int score, bool completed, bool isLocalUser, string statisticName)
        {
            this.position = position;
            this.applicationMode = applicationMode;
            this.title = title;
            this.name = name;
            this.score = score;
            this.completed = completed;
            this.isLocalUser = isLocalUser;
            this.statisticName = statisticName;
        }

        public EntryData(int position)
        {
            this.position = position;
            this.applicationMode = -1;
            this.title = 11;
            this.name = "---";
            this.score = 0;
            this.completed = false;
            this.isLocalUser = false;
            this.statisticName = string.Empty;
        }

        public static bool operator ==(EntryData a, EntryData b)
        {
            return a.position == b.position
                && a.applicationMode == b.applicationMode
                && a.title == b.title
                && a.name == b.name
                && a.score == b.score
                && a.completed == b.completed
                && a.isLocalUser == b.isLocalUser;
        }

        public static bool operator !=(EntryData a, EntryData b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            if (obj is EntryData)
            {
                return this == (EntryData)obj;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (position, applicationMode, name, score, completed, isLocalUser).GetHashCode();
        }

        public override string ToString()
        {
            return $"Position: {position}, Application Mode: {applicationMode}, Title: {title}, Name: {name}, Score: {score}, Completed: {completed}, Is Local User: {isLocalUser}";
        }
    }

	[Serializable]
	public struct ScoreStatisticMetadata
	{
		public int applicationMode;
		public int emblem;
	}


	[Header("Settings")]
	public StringConstant editorPlayerID;

	public ApplicationVersion applicationVersion;

	[Header("System Message Settings")]
	public LocalizedString newUserString;
	public LocalizedString loggedInAsString;

	[Header("References")]
	public GameModeNameList gameModeNameList;
	public LevelNameList levelNameList;


	public string playerID
	{
		get;
		private set;
	} = "Offline Player";

	public PlayFab.ClientModels.EntityKey playerKey
	{
		get;
		private set;
	}

	private string _displayName = "Unknown Player";
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

	private Dictionary<string, EntryData> userStatisticEntries = new Dictionary<string, EntryData>();

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

	public event ILeaderboardPlatform.LogInHandler OnLogInChanged;
	public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

	private Coroutine loginCoroutine;

	public const string playTimeStatisticName = "Play Time";
	public const string runTotalStatisticName = "Run_Total";

	public const string applicationVersionKeyName = "Application Version";

	string customID = string.Empty;

	protected override void OnSingletonInitialized() { }


	public void Start()
	{
		Ref.Register<ILeaderboardPlatform>(this);

		ApplicationInfo.OnInternetAvailabilityChanged += ApplicationInfo_OnInternetAvailabilityChanged;

		LogIn();
	}

	void OnDestroy()
	{
		Ref.Unregister<ILeaderboardPlatform>(this);

		ApplicationInfo.OnInternetAvailabilityChanged -= ApplicationInfo_OnInternetAvailabilityChanged;

		if (Ref.TryGet(out IPlayerPlatform platform))
		{
			platform.OnDisplayNameChanged -= Platform_OnDisplayNameChanged;
		}
	}

	//private void OnApplicationQuit()
	//{
	//	// Log play time (minutes)
	//	int playTimeMinutes = (int)(Time.unscaledTime / 60f);
	//	List<PlayFab.ClientModels.StatisticUpdate> statisticUpdates = new List<PlayFab.ClientModels.StatisticUpdate>
	//	{
	//		new PlayFab.ClientModels.StatisticUpdate {
	//			StatisticName = playTimeStatisticName,
	//			Value = playTimeMinutes
	//		}
	//	};
		
	//	PlayFabClientAPI.UpdatePlayerStatistics(new UpdatePlayerStatisticsRequest { Statistics = statisticUpdates }, null, null);
	//}

	private void LogIn()
	{
		if (loginCoroutine != null)
			StopCoroutine(loginCoroutine);

		loginCoroutine = StartCoroutine(DoLogIn());
	}

	private IEnumerator DoLogIn()
	{
#if UNITY_EDITOR
		// In editor, everyone uses the testuser account
		customID = editorPlayerID.Value;

		// If this is a ParrelSync clone, give it a random test account
		if (ClonesManager.IsClone())
			customID = "PS Clone_" + ClonesManager.GetArgument();

		// lol
		if (false) yield return null;
#else
		// If there is a player platform available, use that ID
		IPlayerPlatform platform;

		while (!Ref.TryGet(out platform))
			yield return null;

		// Wait 1 frame to allow platform to potentially update logged in state after going offline
		yield return null;

		// Wait for platform login
		while (!platform.isLoggedIn)
			yield return null;

		// Set the id. Append the platform name to ensure uniqueness
		var platformName = $"{platform.GetType().Name.Replace("Manager", "")}"; // Removes "Manager" from the class name, if present

		customID = $"{platformName}_{platform.playerID}";

		platform.OnDisplayNameChanged += Platform_OnDisplayNameChanged;
#endif
		loginState = "Initializing";

		var request = new LoginWithCustomIDRequest
		{
			CustomId = customID,
			CreateAccount = true,
			InfoRequestParameters = new GetPlayerCombinedInfoRequestParams()
			{
				GetPlayerProfile = true
			}
		};

		PlayFabClientAPI.LoginWithCustomID(request, OnLoginSuccess, OnLoginFailure);
	}


	public void UpdateUserDisplayName(string displayName)
	{
		PlayFabClientAPI.UpdateUserTitleDisplayName(new UpdateUserTitleDisplayNameRequest { DisplayName = displayName }, OnUpdateUserTitleDisplaySuccess, OnFailure);
	}

	public EntryData? GetUserStatisticEntryData(string statisticName)
	{
		if (userStatisticEntries.TryGetValue(statisticName, out var entryData))
		{
			return entryData;
		}
		
		return null;
	}

	public void SetUserStatisticEntryData(string statisticName, EntryData entryData)
	{
		userStatisticEntries[statisticName] = entryData;
		Debug.Log($"PlayfabManager: Cached an entry for leaderboard \"{statisticName}\" with data: {entryData}");
	}

	public static string GetLeaderboardName(string gameModeID, int playerCount, string levelID)
	{
		if (Instance == null)
		{
			Debug.LogError($"PlayfabManager: Instance not found! Unable to get leaderboard name.");
			return string.Empty;
		}

		// Construct the leaderboard name from game mode, player count, and level
		// Format is {GameModeName}_P{PlayerCount}_{LevelName}. Ex: "Survival_P2_NightsideGateway"
		var leaderboardName = string.Empty;

		// Set the game mode name and player count
		if (Instance.gameModeNameList.TryGetName(gameModeID, out var gameModeName))
		{
			leaderboardName += $"{gameModeName.Replace(" ", "")}_P{playerCount}";
		}
		else
		{
			Debug.LogError($"PlayfabManager: GetLeaderboardName failed to find a game mode name from game mode id \"{gameModeID}\"!");
		}

		// Set the level name if the game mode is Survival
		if (gameModeName == "Survival")
		{
			if (Instance.levelNameList.TryGetName(levelID, out var levelName))
			{
				leaderboardName += $"_{levelName.Replace(" ", "")}";
			}
			else
			{
				Debug.LogError($"PlayfabManager: GetLeaderboardName failed to find a level name from level id \"{levelID}\"!");
			}
		}


		return leaderboardName;
	}

	private void UpdateUserHighScores()
	{
		// Initialize all leaderboard statistics (only higher stats get updated on PlayFab).
		var metaData = JsonUtility.ToJson(new ScoreStatisticMetadata()
		{
			applicationMode = -1,
			emblem = 11,
		});

		// Add a stat for each leaderboard name
		var statisticsUpdate = new List<PlayFab.ProgressionModels.StatisticUpdate>();

		foreach (var leaderboardName in LeaderboardNames.All)
		{
			statisticsUpdate.Add(new PlayFab.ProgressionModels.StatisticUpdate
			{
				Name = leaderboardName,
				Scores = new List<string> { 0.ToString(), },
				Metadata = metaData,
			});
		}

		PlayFabProgressionAPI.UpdateStatistics(
		new UpdateStatisticsRequest
		{
			Statistics = statisticsUpdate
		}, 
		response => 
		{
			//// Get positions for 1,2, and 3 player Ascension leaderboards
			//PlayFabProgressionAPI.GetLeaderboardAroundEntity(new GetLeaderboardAroundEntityRequest
			//{
			//	MaxSurroundingEntries = 1,
			//	LeaderboardName = GetScoreStatisticName(1),
			//}, response => OnGetScoreLeaderboardAroundPlayerSuccess(response, 1), OnFailure);

			//PlayFabProgressionAPI.GetLeaderboardAroundEntity(new GetLeaderboardAroundEntityRequest
			//{
			//	MaxSurroundingEntries = 1,
			//	LeaderboardName = GetScoreStatisticName(2),
			//}, response => OnGetScoreLeaderboardAroundPlayerSuccess(response, 2), OnFailure);

			//PlayFabProgressionAPI.GetLeaderboardAroundEntity(new GetLeaderboardAroundEntityRequest
			//{
			//	MaxSurroundingEntries = 1,
			//	LeaderboardName = GetScoreStatisticName(3),
			//}, response => OnGetScoreLeaderboardAroundPlayerSuccess(response, 3), OnFailure);
		}, OnFailure);
	}

	private void SetApplicationVersion()
	{
		PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest()
		{
			Data = new Dictionary<string, string>() {
			{applicationVersionKeyName, applicationVersion.GetVersion()},
		}
		},
		result => Debug.Log($"Successfully updated user application version: {applicationVersion.GetVersion()}"),
		error => 
		{
			Debug.LogError($"Error setting user application version: {error.GenerateErrorReport()}");
		});
	}


	private void OnLoginSuccess(LoginResult result)
	{
		Debug.Log($"PlayfabManager: Login successful");

		isLoggedIn = true;
		loginState = "Logged in";

		playerKey = result.EntityToken.Entity;

		if (result.NewlyCreated)
		{
#if !UNITY_EDITOR
			// Set user display name to the platform display name
			if (Ref.TryGet(out IPlayerPlatform platform) && !platform.displayName.IsNullOrEmpty())
			{
				displayName = platform.displayName;
			}
#else
			displayName = customID;
#endif
			playerID = result.PlayFabId;

			UpdateUserDisplayName(displayName);

			newUserString.GetLocalizedStringAsync().Completed += handle =>
			{
				SystemMessage.Log($"{handle.Result}");
			};

			Debug.Log($"New Playfab login created");
		}
		else
		{
			displayName = result.InfoResultPayload.PlayerProfile.DisplayName;
			playerID = result.PlayFabId;

#if !UNITY_EDITOR
			// If the platform display name has changed, update user display name to it
			if (Ref.TryGet(out IPlayerPlatform platform) && !platform.displayName.IsNullOrEmpty() && displayName != platform.displayName)
			{
				SystemMessage.Log($"User name changed from {displayName} to {platform.displayName}");

				displayName = platform.displayName;

				UpdateUserDisplayName(displayName);
			}
#endif


		}

		// Store the initial high scores for the player, used by leaderboards
		UpdateUserHighScores();

		loggedInAsString.GetLocalizedStringAsync().Completed += handle =>
		{
			SystemMessage.Log($"{handle.Result} {displayName}");
		};

		SetApplicationVersion();
	}

	private void OnLoginFailure(PlayFabError error)
	{
		var errorMessage = $"PlayfabManager: Login failed ({error.GenerateErrorReport()})";
		
		loginState = errorMessage;
		hasLoginError = true;
		
		SystemMessage.LogError("Playfab", error.Error.ToString());

		OnFailure(error);
	}

	private void OnFailure(PlayFabError error)
	{
		Debug.LogError($"PlayfabManager: {error.GenerateErrorReport()}");
	}

	private void OnUpdateUserTitleDisplaySuccess(UpdateUserTitleDisplayNameResult result)
	{
		displayName = result.DisplayName;

		Debug.Log($"PlayfabManager: Updated display name to { result.DisplayName }");
	}

	//private void OnGetScoreLeaderboardAroundPlayerSuccess(GetEntityLeaderboardResponse response, int playerCount)
	//{
	//	playerCount = Mathf.Clamp(playerCount, 1, 3);
	//	var statisticName = GetScoreStatisticName(playerCount);

	//	if (response.Rankings.Count > 0)
	//	{
	//		var leaderboardEntry = response.Rankings.Find(r => r.Entity.Id == playerKey.Id);
	//		var entryData = GetEntryData(leaderboardEntry, statisticName);
	//		SetUserStatisticEntryData(statisticName, entryData);

	//		Debug.Log($"Retrieved user high score for statistic {statisticName} - ({entryData})");
	//	}
	//	else
	//	{
	//		Debug.LogWarning($"No high score data found for ({playerID}) {displayName} with player count {playerCount}");
	//	}
	//}


	//public static string GetScoreStatisticName(int playerCount)
	//{
	//	playerCount = Mathf.Clamp(playerCount, 1, 3);

	//	return scoreStatisticName + playerCount.ToString();
	//}

	public static EntryData GetEntryData(EntityLeaderboardEntry entry, string statisticName)
	{
		// Score stats are pulled from attached metadata
		var json = string.IsNullOrEmpty(entry.Metadata) ? "{}" : entry.Metadata;
		var scoreMetadata = JsonUtility.FromJson<ScoreStatisticMetadata>(json);

		// Application mode
		int applicationMode = scoreMetadata.applicationMode;

		// Title
		int title = scoreMetadata.emblem;

		// Compare entry entity id to local entity id to determine if this is the local user
		bool isLocalUser = entry.Entity.Id == PlayfabManager.Instance.playerKey.Id;

		// Score comes from the first "column" in the statistic entry
		int score;

		if (!int.TryParse(entry.Scores[0], out score))
		{
			Debug.LogError($"PlayfabManager: Parsing the score from a string for the entry for {entry.DisplayName} failed!");
		}

		// Shift rank down for a zero based "position"
		var position = entry.Rank - 1;

		// Create data for the entry
		EntryData entryData = new EntryData(
			position,
			applicationMode,
			title,
			entry.DisplayName,
			score,
			score >= 200,
			isLocalUser,
			statisticName
			);


		return entryData;
	}



	private void ApplicationInfo_OnInternetAvailabilityChanged(ApplicationInfo.InternetAvailabilityStatus status)
	{
		bool isInternetAvailable = status == ApplicationInfo.InternetAvailabilityStatus.Online;

		// When internet becomes available and PlayFab is not currently logged in, try to log in again
		if (isInternetAvailable && !isLoggedIn)
			LogIn();
	}

	private void Platform_OnDisplayNameChanged(string displayName)
	{
		if (Ref.TryGet(out IPlayerPlatform platform) && platform.isLoggedIn)
			UpdateUserDisplayName(displayName);
	}
}