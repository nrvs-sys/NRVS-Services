using Services.UGS;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "Condition_ Is Lobby Joined", menuName = "Behaviors/Conditions/Lobby/Is Lobby Joined")]
public class LobbyJoinedConditionBehavior : ConditionBehavior
{
    protected override bool Evaluate() => Ref.TryGet<LobbyManager>(out var manager) && manager.IsLocalPlayerInJoinedLobby();
}
