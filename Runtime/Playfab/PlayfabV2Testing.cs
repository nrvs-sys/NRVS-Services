using PlayFab;
using PlayFab.ClientModels;
using PlayFab.DataModels;
using PlayFab.ProgressionModels;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityAtoms.BaseAtoms;
using UnityEngine;

public class PlayfabV2Testing : MonoBehaviour
{
	[Header("Settings")]
	public StringConstant editorPlayerID;

	public string statisticName = "Ascension_P1";

	public int statisticUpdateValue = 100;

	public int applicationMode = 1;

	public int emblem = 3;

	public string leaderboardName = "Ascension_P1";


	[Header("Debugging")]

	[SerializeField]
	private string _playerID;
	public string playerID
	{
		get => _playerID;
		private set => _playerID = value;
	}

	[SerializeField]
	private string _entityId;
	public string entityId
	{
		get => _entityId;
		private set => _entityId = value;
	}

	[SerializeField]
	private string _entityType;
	public string entityType
	{
		get => _entityType;
		private set => _entityType = value;
	}

	private PlayFabAuthenticationContext authenticationContext;

	[SerializeField]
	private string _displayName = "Unknown Player";
	public string displayName
	{
		get => _displayName;
		private set => _displayName = value;
	}

	[SerializeField]
	private bool _isLoggedIn;
	public bool isLoggedIn
	{
		get => _isLoggedIn;
		private set => _isLoggedIn = value;
	}

	[SerializeField]
	string customID = string.Empty;



	[Serializable]
	public struct ScoreStatisticMetadata
	{
		public int applicationMode;
		public int emblem;
	}



	public void Start()
	{
		customID = editorPlayerID.Value;


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


	private void OnLoginSuccess(LoginResult result)
	{
		Debug.Log($"Playfab login successful");

		isLoggedIn = true;

		if (result.NewlyCreated)
		{
			displayName = customID;

			playerID = result.PlayFabId;

			UpdateUserDisplayName(displayName);

			Debug.Log($"New Playfab login created");
		}
		else
		{
			displayName = result.InfoResultPayload.PlayerProfile.DisplayName;
			playerID = result.PlayFabId;
		}

		entityId = result.EntityToken.Entity.Id;
		entityType = result.EntityToken.Entity.Type;
		authenticationContext = result.AuthenticationContext;
	}

	private void OnLoginFailure(PlayFabError error)
	{
		var errorMessage = $"Login failed ({error.Error})";

		SystemMessage.Log(errorMessage);

		OnFailure(error);
	}

	public void UpdateUserDisplayName(string displayName)
	{
		PlayFabClientAPI.UpdateUserTitleDisplayName(new UpdateUserTitleDisplayNameRequest { DisplayName = displayName }, OnUpdateUserTitleDisplaySuccess, OnFailure);
	}

	private void OnUpdateUserTitleDisplaySuccess(UpdateUserTitleDisplayNameResult result)
	{
		displayName = result.DisplayName;

		Debug.Log($"Updated display name to { result.DisplayName }");
	}

	private void OnFailure(PlayFabError error)
	{
		Debug.LogError(error.GenerateErrorReport());
	}


	public void GetObjects()
	{
		var getRequest = new GetObjectsRequest { Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType } };
		PlayFabDataAPI.GetObjects(getRequest, GetObjectsSuccess, OnPlayFabError);
	}

	private void GetObjectsSuccess(GetObjectsResponse response)
	{
		var objectDetails = string.Empty;

		foreach (var o in response.Objects)
		{
			objectDetails += $"object key: {o.Key}, object name: {o.Value.ObjectName}, object contents: {o.Value.DataObject}\n";
		}

		Debug.LogError($"GetObjects returned successfully! details: \n{objectDetails}");
	}

	private void OnPlayFabError(PlayFabError error)
	{
		var errorDetails = string.Empty;

		if (error.ErrorDetails != null)
		{
			foreach (var e in error.ErrorDetails)
			{
				var message = string.Empty;

				foreach (var m in e.Value)
				{
					message += $"{m} - ";
				}

				errorDetails += $"error: {e.Key}, message: {message}\n";
			}
		}

		Debug.LogError($"PlayFab returned an error! Code: {error.Error}, message: {error.ErrorMessage}, details: \n{errorDetails}");
	}


	public void SetObjects()
	{
		var data = new Dictionary<string, object>()
		{
			{"Health", 100},
			{"Mana", 10000}
		};
		
		var dataList = new List<SetObject>()
		{
			new SetObject()
			{
				ObjectName = "PlayerData",
				DataObject = data
			},
		};

		var request = new SetObjectsRequest()
		{
			Entity = new PlayFab.DataModels.EntityKey { Id = entityId, Type = entityType }, // Saved from GetEntityToken, or a specified key created from a titlePlayerId, CharacterId, etc
			Objects = dataList,
		};

		PlayFabDataAPI.SetObjects(request, SetObjectsSuccess, OnPlayFabError);
	}

	private void SetObjectsSuccess(SetObjectsResponse response)
	{
		Debug.Log(response.ProfileVersion);
	}


	public void UpdateStatistic()
	{
		var metaData = JsonUtility.ToJson(new ScoreStatisticMetadata()
		{
			applicationMode = applicationMode,
			emblem = emblem,
		});

		var request = new UpdateStatisticsRequest()
		{
			Statistics = new List<PlayFab.ProgressionModels.StatisticUpdate>()
			{
				new PlayFab.ProgressionModels.StatisticUpdate()
				{
					Name = statisticName,
					Scores = new List<string>()
					{
						statisticUpdateValue.ToString(),
					},
					Metadata = metaData,
				},
			},
		};
		
		PlayFabProgressionAPI.UpdateStatistics(request, UpdateStatisticsSuccess, OnPlayFabError);
	}

	private void UpdateStatisticsSuccess(UpdateStatisticsResponse response)
	{
		var statisticsUpdated = string.Empty;

		foreach (var stat in response.Statistics)
		{
			var scores = string.Empty;

			foreach (var score in stat.Value.Scores)
			{
				scores += $"{score}, ";
			}

			statisticsUpdated += $"key: {stat.Key}, scores: {scores}, metadata: {stat.Value.Metadata} \n";
		}

		Debug.LogError($"Update statistics success! statistics updated: {statisticsUpdated}");
	}


	public void GetLeaderboard()
	{
		var request = new GetLeaderboardAroundEntityRequest()
		{
			LeaderboardName = leaderboardName,
			MaxSurroundingEntries = 50,
		};

		PlayFabProgressionAPI.GetLeaderboardAroundEntity(request, GetLeaderboardSuccess, OnPlayFabError);
	}

	private void GetLeaderboardSuccess(GetEntityLeaderboardResponse response)
	{
		var rankings = string.Empty;

		foreach (var rank in response.Rankings)
		{
			rankings += $"[{rank.Rank}] {rank.DisplayName} - {rank.Scores[0]}: {rank.Metadata} \n";
		}

		Debug.LogError($"Get leaderboard success! Total entries: {response.EntryCount} \n{rankings}");
	}
}