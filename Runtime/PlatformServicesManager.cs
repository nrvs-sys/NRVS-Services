using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlatformServicesManager : MonoBehaviour
{
	[Header("Platform Scenes")]
	public SceneReference standalonePlatformScene;
	public SceneReference oculusPlatformScene;
	public SceneReference steamPlatformScene;
	public SceneReference UGSPlatformScene;
	public SceneReference edgegapPlatformScene;
	public SceneReference playFabPlatformScene;

	private void Start()
	{
        // Load the player platform
#if PLAYERPLATFORM_STANDALONE
		SceneManager.LoadSceneAsync(standalonePlatformScene, LoadSceneMode.Additive);
#elif PLAYERPLATFORM_OCULUS
        SceneManager.LoadSceneAsync(oculusPlatformScene, LoadSceneMode.Additive);
#elif PLAYERPLATFORM_STEAM
		SceneManager.LoadSceneAsync(steamPlatformScene, LoadSceneMode.Additive);
#endif

        // Load the UGS platform
        SceneManager.LoadSceneAsync(UGSPlatformScene, LoadSceneMode.Additive);

        // Load the Edgegap platform
        SceneManager.LoadSceneAsync(edgegapPlatformScene, LoadSceneMode.Additive);

        // Load the leaderboard platform
        SceneManager.LoadSceneAsync(playFabPlatformScene, LoadSceneMode.Additive);
	}
}