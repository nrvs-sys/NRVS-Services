using Services.UGS;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "Condition_ Local Lobby Player_ Is Ready_ New", menuName = "Behaviors/Conditions/Lobby/Local Lobby Player_ Is Ready")]
public class LobbyLocalPlayerIsReadyConditionBehavior : ConditionBehavior
{
    protected override bool Evaluate() => Ref.TryGet<LobbyManager>(out var manager) && manager.IsLocalPlayerInJoinedLobby() && manager.GetPlayerDataValue(manager.GetLocalPlayer(), Constants.Services.UGS.Lobbies.PlayerDataKeys.IsReady) == true.ToString();
}
