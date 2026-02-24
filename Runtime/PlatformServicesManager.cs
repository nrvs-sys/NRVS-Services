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

	[Space(10)]

	public List<SceneReference> additionalPlatformScenes;

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

		for (int i = 0; i < additionalPlatformScenes.Count; ++i)
		{
			SceneManager.LoadSceneAsync(additionalPlatformScenes[i], LoadSceneMode.Additive);
		}
	}
}
