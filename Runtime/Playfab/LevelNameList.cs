using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

[CreateAssetMenu(fileName = "Level Name List_ New", menuName = "Settings/Level Name List")]
public class LevelNameList : ScriptableObject
{
	public List<LevelName> levelNames;

	[Serializable]
	public class LevelName
	{
		[Tooltip("Common string name for the level used for backend processing")]
		public string name;
		[Tooltip("A localized string name for the level displayed in UI")]
		public LocalizedString localizedString;
		[Tooltip("Level IDs used to identify a level, derived from scene names")]
		public List<string> levelIDs;
	}


	/// <summary>
	/// Gets the localized level name for a level from a level ID
	/// </summary>
	/// <param name="levelID"></param>
	/// <param name="localizedString"></param>
	/// <returns></returns>
	public bool TryGetLocalizedString(string levelID, out LocalizedString localizedString)
	{
		if (TryGet(levelID, out LevelName levelName))
		{
			localizedString = levelName.localizedString;

			return true;
		}
		else
		{
			localizedString = null;

			return false;
		}
	}

	public bool TryGetLocalizedStringFromLevelName(string name, out LocalizedString localizedString)
	{
		var levelName = levelNames.Find(l => l.name == name);

		if (levelName != null)
		{
			localizedString = levelName.localizedString;

			return true;
		}
		else
		{
			localizedString = null;

			return false;
		}
	}	

	/// <summary>
	/// Gets the common string name for a level from a level ID
	/// </summary>
	/// <param name="levelID"></param>
	/// <param name="name"></param>
	/// <returns></returns>
	public bool TryGetName(string levelID, out string name)
	{
		if (TryGet(levelID, out LevelName levelName))
		{
			name = levelName.name;

			return true;
		}
		else
		{
			name = levelID;

			return false;
		}
	}

	private bool TryGet(string levelID, out LevelName levelName)
	{
		levelName = levelNames.Find(s => s.levelIDs.Contains(levelID));

		return levelName != null;
	}
}