using Services.UGS;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "Condition_ Is Lobby Host", menuName = "Behaviors/Conditions/Lobby/Is Lobby Host")]
public class LobbyHostConditionBehavior : ConditionBehavior
{
    protected override bool Evaluate() => Ref.TryGet<LobbyManager>(out var manager) && manager.IsLocalPlayerLobbyHost();
}
