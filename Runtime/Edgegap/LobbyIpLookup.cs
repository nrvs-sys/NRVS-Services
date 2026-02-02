using Services.UGS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.Events;

namespace Services.Edgegap
{
    public class LobbyIpLookup : MonoBehaviour
    {
        public enum LobbyIpLookupState
        {
            Idle = 0,
            RequestingInfo = 1,
            Complete = 2
        }

        public struct LobbyIpInfoResult
        {
            public List<string> lobbyIps;
            public bool isError;
        }

        [Header("Events")]

        public UnityEvent<string> onPublicIpReceived;

        LobbyIpLookupState lobbyIpLookupState = LobbyIpLookupState.Idle;

        #region Unity Methods

        void OnEnable()
        {
            Ref.Register<LobbyIpLookup>(this);

            Ref.Instance.OnRegistered += Ref_OnRegister;
            Ref.Instance.OnUnregistered += Ref_OnUnregister;

            if (Ref.TryGet<LobbyManager>(out var lobbyManager))
            {
                Ref_OnRegister(typeof(LobbyManager), lobbyManager);
            }
        }

        void OnDisable()
        {
            if (Ref.Instance != null)
            {
                Ref.Unregister<LobbyIpLookup>(this);

                Ref.Instance.OnRegistered -= Ref_OnRegister;
                Ref.Instance.OnUnregistered -= Ref_OnUnregister;

                if (Ref.TryGet<LobbyManager>(out var lobbyManager))
                {
                    Ref_OnUnregister(typeof(LobbyManager), lobbyManager);
                }
            }
        }

        #endregion

        public async Task<LobbyIpInfoResult> GetLobbyIpsAsync(float ipRequestTimeout = 20)
        {
            var lobbyIps = new List<string>();

            var lobbyManager = Ref.Get<LobbyManager>();

            if (lobbyManager.IsLocalPlayerLobbyHost())
            {
                await lobbyManager.SetLobbyDataValue(lobbyManager.joinedLobby, new(Constants.Services.Edgegap.LobbyDataKeys.LobbyIPLookupState, LobbyIpLookupState.RequestingInfo.ToString(), DataObject.VisibilityOptions.Member));

                Debug.Log("Lobby IP Manager: Requested IP info from Lobby players");

                var doesEachLobbyPlayerHaveAnIp = false;
                var startTime = Time.time;

                // players in the lobby will see the lobby state change, then query for their public IP, then send it back as Lobby Player Data
                // wait for all players in the lobby to update their public ip
                do
                {
                    await Task.Delay(25);

                    if (lobbyManager.joinedLobby == null)
                    {
                        Debug.LogError("Lobby IP Manager: Lobby is null, cannot get ips");
                        return new() { lobbyIps = null, isError = true };
                    }

                    lobbyIps.Clear();
                    foreach (var player in lobbyManager.joinedLobby.Players)
                    {
                        if (player.Data.TryGetValue(Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp, out var publicIp))
                        {
                            lobbyIps.Add(publicIp.Value);
                        }
                    }

                    // if we have received an IP from each player in the lobby, we are done
                    doesEachLobbyPlayerHaveAnIp = true;
                    foreach (var player in lobbyManager.joinedLobby.Players)
                    {
                        if (!player.Data.ContainsKey(Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp))
                        {
                            doesEachLobbyPlayerHaveAnIp = false;
                            break;
                        }
                    }

                    if (Time.time - startTime > ipRequestTimeout)
                    {
                        Debug.LogWarning("Lobby IP Manager: Timeout waiting for Lobby players to send IP info");
                        return new() { lobbyIps = null, isError = true };
                    }
                }
                while (!doesEachLobbyPlayerHaveAnIp);

                Debug.Log("Lobby IP Manager: Received IP info from Lobby players");

                _ = lobbyManager.SetLobbyDataValue(lobbyManager.joinedLobby, new(Constants.Services.Edgegap.LobbyDataKeys.LobbyIPLookupState, LobbyIpLookupState.Complete.ToString(), DataObject.VisibilityOptions.Member));
            }
            else
            {
                Debug.LogError("Lobby IP Manager: Not lobby host, cannot get ips");
                return new() { lobbyIps = null, isError = true };
            }

            return new() { lobbyIps = lobbyIps, isError = false } ;
        }

        async Task UpdatePublicIp()
        {
            var ipUtility = Ref.Get<IpUtility>();
            var ip = await ipUtility.GetIpAddress();

            var lobbyManager = Ref.Get<LobbyManager>();
            var lobbyPlayer = lobbyManager.GetLocalPlayer();

            if (lobbyManager.joinedLobby != null && lobbyPlayer != null)
            {
                await lobbyManager.SetPlayerDataValue(lobbyManager.joinedLobby, lobbyPlayer, new(Constants.Services.Edgegap.LobbyPlayerDataKeys.PublicIp, ip, PlayerDataObject.VisibilityOptions.Member));
            }
            else
            {
                Debug.LogError("Lobby IP Manager: Not in a lobby, updating public ip canceled");
            }
        }

        #region Event Handlers

        void Ref_OnRegister(System.Type type, object instance)
        {
            if (instance is LobbyManager lobbyManager)
            {
                lobbyManager.onLobbyJoined.AddListener(LobbyManager_OnLobbyJoined);
                lobbyManager.onJoinedLobbyUpdated.AddListener(LobbyManager_OnJoinedLobbyUpdated);

                if (lobbyManager.joinedLobby != null)
                {
                    LobbyManager_OnLobbyJoined(lobbyManager.joinedLobby);
                }
            }
        }

        void Ref_OnUnregister(System.Type type, object instance)
        {
            if (instance is LobbyManager lobbyManager)
            {
                lobbyManager.onLobbyJoined.RemoveListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onJoinedLobbyUpdated.RemoveListener(LobbyManager_OnJoinedLobbyUpdated);
            }
        }

        void LobbyManager_OnLobbyJoined(Lobby lobby)
        {
            var lobbyManager = Ref.Get<LobbyManager>();

            if (lobbyManager.IsLocalPlayerLobbyHost(lobby))
            {
                _ = lobbyManager.SetLobbyDataValue(lobby, new(Constants.Services.Edgegap.LobbyDataKeys.LobbyIPLookupState, LobbyIpLookupState.Idle.ToString(), DataObject.VisibilityOptions.Member));
            }

        }

        void LobbyManager_OnJoinedLobbyUpdated(Lobby lobby)
        {
            var lobbyManager = Ref.Get<LobbyManager>();

            if (lobby != null)
            {
                var lobbyIpLookupState = (LobbyIpLookupState)Enum.Parse(typeof(LobbyIpLookupState), lobby.Data.GetValueOrDefault(Constants.Services.Edgegap.LobbyDataKeys.LobbyIPLookupState, new DataObject(DataObject.VisibilityOptions.Member, LobbyIpLookupState.Idle.ToString())).Value);

                if (lobbyIpLookupState != this.lobbyIpLookupState)
                {
                    var previousLobbyState = this.lobbyIpLookupState;
                    this.lobbyIpLookupState = lobbyIpLookupState;

                    Debug.Log($"Lobby IP Manager: Lobby ready state updated from {previousLobbyState} to {this.lobbyIpLookupState}");

                    switch (lobbyIpLookupState)
                    {
                        case LobbyIpLookupState.Idle:
                            break;
                        case LobbyIpLookupState.RequestingInfo:
                            // Get the public IP of this client and update the lobby player data
                            var _ = UpdatePublicIp();
                            break;
                        case LobbyIpLookupState.Complete:
                            break;
                        default:
                            break;
                    }
                }
            }

        }

        #endregion
    }
}
