using FishNet.Transporting.KCP.Edgegap;
using NUnit.Framework;
using Services.UGS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies.Models;
using UnityAtoms.BaseAtoms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Services.Edgegap
{
    /// <summary>
    /// Manages the state of the Edgegap Relay.
    /// </summary>
    public class RelayManager : MonoBehaviour, IRelayPlatform
    {
        #region IPlatform Fields

        public string playerID => Ref.Get<LobbyManager>()?.playerID ?? "";

        public string displayName => Ref.Get<LobbyManager>()?.displayName ?? "";

        public string loginState => Ref.Get<LobbyManager>()?.loginState ?? "";

        public bool isLoggedIn => Ref.Get<LobbyManager>()?.isLoggedIn ?? false;

        public bool hasLoginError => Ref.Get<LobbyManager>()?.hasLoginError ?? false;

        public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

        #endregion

        [Header("Events")]

        [SerializeField]
        UnityEvent<string> onSessionIdReceived;

        string relaySessionId = "";

        #region Unity Methods

        void OnEnable()
        {
            Ref.Register<IRelayPlatform>(this);
            Ref.Register<RelayManager>(this);

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
                Ref.Unregister<IRelayPlatform>(this);
                Ref.Unregister<RelayManager>(this);

                Ref.Instance.OnRegistered -= Ref_OnRegister;
                Ref.Instance.OnUnregistered -= Ref_OnUnregister;

                if (Ref.TryGet<LobbyManager>(out var lobbyManager))
                {
                    Ref_OnUnregister(typeof(LobbyManager), lobbyManager);
                }
            }
        }

        #endregion

        public void StartRelaySessionOnClients(string relaySessionId)
        {
            var lobbyManager = Ref.Get<LobbyManager>();
            if (lobbyManager.IsLocalPlayerLobbyHost())
            {
                Debug.Log("Relay Manager: Sending Relay Session to clients");
                
                _ = lobbyManager.SetLobbyDataValues(lobbyManager.joinedLobby, new LobbyManager.LobbyDataValue(Constants.Services.Edgegap.Relay.LobbyDataKeys.RelaySessionId, relaySessionId, DataObject.VisibilityOptions.Member));
            }
        }

        #region Event Handlers

        void Ref_OnRegister(System.Type type, object instance)
        {
            if (instance is LobbyManager lobbyManager)
            {
                lobbyManager.onLobbyJoined.AddListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onJoinedLobbyUpdated.AddListener(LobbyManager_OnJoinedLobbyUpdated);

                if (lobbyManager.joinedLobby != null)
                {
                    LobbyManager_OnJoinedLobbyUpdated(lobbyManager.joinedLobby);
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


        void LobbyManager_OnJoinedLobbyUpdated(Lobby lobby)
        {
            var lobbyManager = Ref.Get<LobbyManager>();

            if (lobby != null)
            {
                var relaySessionId = lobby.Data.GetValueOrDefault(Constants.Services.Edgegap.Relay.LobbyDataKeys.RelaySessionId, new DataObject(DataObject.VisibilityOptions.Member, "")).Value;

                if (relaySessionId != this.relaySessionId)
                {
                    var previousRelaySessionId = this.relaySessionId;
                    this.relaySessionId = relaySessionId;

                    Debug.Log($"Relay Manager: Relay session id updated from {previousRelaySessionId} to {this.relaySessionId}");

                    if (!string.IsNullOrEmpty(this.relaySessionId))
                    {
                        onSessionIdReceived?.Invoke(this.relaySessionId);
                    }
                }
            }
        }

        #endregion
    }
}
