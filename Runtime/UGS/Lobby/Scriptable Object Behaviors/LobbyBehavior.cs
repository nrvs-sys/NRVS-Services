using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Services.UGS;

[CreateAssetMenu(fileName = "Lobby_ New", menuName = "Behaviors/Services/Lobby")]
public class LobbyBehavior : ScriptableObject
{
    public void JoinLobbyByID(string ID) => Ref.Get<LobbyManager>()?.JoinLobbyByID(ID);
    public void JoinLobbyByCode(string code) => Ref.Get<LobbyManager>()?.JoinLobbyByCode(code);
    public void LeaveJoinedLobby() => Ref.Get<LobbyManager>()?.LeaveJoinedLobby();

    public void SetLobbyAsUnlocked()
    {
        if (Ref.TryGet<LobbyManager>(out var lobbyManager) && lobbyManager.IsLocalPlayerLobbyHost())
        {
            _ = lobbyManager.UpdateLobby(lobbyManager.joinedLobby.Id, new() { IsLocked = false });
        }
    }

    public void SetLobbyAsLocked()
    {
        if (Ref.TryGet<LobbyManager>(out var lobbyManager) && lobbyManager.IsLocalPlayerLobbyHost())
        {
            _ = lobbyManager.UpdateLobby(lobbyManager.joinedLobby.Id, new() { IsLocked = true });
        }
    }

    public void SetLobbyAsLockedAndPrivate()
    {
        if (Ref.TryGet<LobbyManager>(out var lobbyManager) && lobbyManager.IsLocalPlayerLobbyHost())
        {
            _ = lobbyManager.UpdateLobby(lobbyManager.joinedLobby.Id, new() { IsLocked = true, IsPrivate = true });
        }
    }

    public void SetLobbyAsUnlockedAndPublic()
    {
        if (Ref.TryGet<LobbyManager>(out var lobbyManager) && lobbyManager.IsLocalPlayerLobbyHost())
        {
            _ = lobbyManager.UpdateLobby(lobbyManager.joinedLobby.Id, new() { IsLocked = false, IsPrivate = false });
        }
    }

    public void SetPlayerReadyState(bool isReady)
    {
        if (Ref.TryGet<LobbyManager>(out var lobbyManager) && lobbyManager.IsLocalPlayerInJoinedLobby())
        {
            _ = lobbyManager.SetPlayerDataValue(lobbyManager.joinedLobby, lobbyManager.GetLocalPlayer(), new(Constants.Services.UGS.Lobbies.PlayerDataKeys.IsReady, isReady.ToString(), Unity.Services.Lobbies.Models.PlayerDataObject.VisibilityOptions.Member));
        }
    }

    public void EndConnectionsImmediately() => Ref.Get<LobbyManager>()?.EndConnectionsImmediately();
}
