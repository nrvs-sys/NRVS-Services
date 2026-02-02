public interface IPlatform
{
	string playerID { get; }
	string displayName { get; }
	string loginState { get; }
	bool isLoggedIn { get; }
	bool hasLoginError { get; }

    public delegate void DisplayNameChangedHandler(string displayName);
    public event DisplayNameChangedHandler OnDisplayNameChanged;
}

public interface IPlayerPlatform : IPlatform 
{
    bool IsAchievementUnlocked(string key);
    void UnlockAchievement(string key);

    /// <summary>
    /// Adds <paramref name="delta"/> to an int stat and returns the new total on Steam, or the delta on Oculus.
    /// IMPORTANT: On Steam, achievements are not unlocked automatically when a stat threshold is reached (they are on Oculus).
    /// Intead, the state must be tracked manually and <see cref="UnlockAchievement(string)"/> must be called once the achievement threshold is reached.
    /// </summary>
    int AddToStat(string statKey, int delta);

    /// <summary>
    /// Only implemented on Steam.
    /// Send the changed stats and achievements data to the server for permanent storage.
    /// Storing stats on Steam can be rate limited and it is recommended to only do it on the order of minutes rather than seconds.
    /// </summary>
    void CaptureStats();

    /// <summary>
    /// Flush any offline queued writes to the backend immediately
    /// </summary>
    void Flush();

    /// <summary>
    /// Fires when an achievement has been confirmed unlocked
    /// </summary>
    event System.Action<string> OnAchievementUnlocked;
}

// TODO - remove
public interface IOnlinePlatform : IPlatform { }
public interface ILeaderboardPlatform : IPlatform 
{
    public event LogInHandler OnLogInChanged;
    public delegate void LogInHandler(bool isLoggedIn);
}
public interface ILobbyPlatform : IPlatform { }
public interface IRelayPlatform : IPlatform { }