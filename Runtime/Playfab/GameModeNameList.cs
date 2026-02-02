using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

[CreateAssetMenu(fileName = "Game Mode Name List_ New", menuName = "Settings/Game Mode Name List")]
public class GameModeNameList : ScriptableObject
{
	public List<GameModeName> gameModeNames;

	[Serializable]
	public class GameModeName
	{
		[Tooltip("Common string name for the game mode used for backend processing")]
		public string name;
		[Tooltip("A localized string name for the game mode displayed in UI")]
		public LocalizedString localizedString;
		[Tooltip("Game mode IDs used to identify a game mode, set on the game mode object")]
		public List<string> gameModeIDs;
	}


	/// <summary>
	/// Gets the localized game mode name for a game mode from a game mode ID
	/// </summary>
	/// <param name="gameModeID"></param>
	/// <param name="localizedString"></param>
	/// <returns></returns>
	public bool TryGetLocalizedString(string gameModeID, out LocalizedString localizedString)
	{
		if (TryGet(gameModeID, out GameModeName gameModeName))
		{
			localizedString = gameModeName.localizedString;

			return true;
		}
		else
		{
			localizedString = null;

			return false;
		}
	}

	/// <summary>
	/// Gets the common string name for a game mode from a game mode ID
	/// </summary>
	/// <param name="gameModeID"></param>
	/// <param name="name"></param>
	/// <returns></returns>
	public bool TryGetName(string gameModeID, out string name)
	{
		if (TryGet(gameModeID, out GameModeName gameModeName))
		{
			name = gameModeName.name;

			return true;
		}
		else
		{
			name = gameModeID;

			return false;
		}
	}

	private bool TryGet(string gameModeID, out GameModeName gameModeName)
	{
		gameModeName = gameModeNames.Find(s => s.gameModeIDs.Contains(gameModeID));

		return gameModeName != null;
	}
}