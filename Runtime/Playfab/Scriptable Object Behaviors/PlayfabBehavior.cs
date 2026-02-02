using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Playfab Behavior_ New", menuName = "Behaviors/Services/Playfab")]
public class PlayfabBehavior : ScriptableObject
{
    public ConditionBehavior condition;

    public void UpdateUserDisplayName(string displayName)
	{
		if (condition != null && !condition.If())
			return;

		PlayfabManager.Instance?.UpdateUserDisplayName(displayName);
	}
}