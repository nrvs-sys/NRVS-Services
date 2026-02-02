using NRVS.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StandaloneManager : MonoBehaviour, IPlayerPlatform
{
	public SettingsBehavior nameSetting;


	public string playerID => SystemInfo.deviceUniqueIdentifier;

	public string displayName => nameSetting.GetString();

	public string loginState
	{
		get;
		private set;
	}

	public bool isLoggedIn
	{
		get;
		private set;
	}

	public bool hasLoginError
	{
		get;
		private set;
	}


	public event Action<string> OnAchievementUnlocked;
	public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

	private void Start()
	{
		isLoggedIn = true;

		Ref.Register<IPlayerPlatform>(this);
	}

	private void OnDestroy()
	{
		Ref.Unregister<IPlayerPlatform>(this);
	}


	#region Achievements

	// There are no achievements in a standalone mode, so don't do anything!
	public bool IsAchievementUnlocked(string key) => false;
	public void UnlockAchievement(string key) { }
	public int AddToStat(string statKey, int delta) => 0;
	public void CaptureStats() { }
	public void Flush() { }

	#endregion
}