using System.Collections;
using System.Collections.Generic;
using Unity.Services.Lobbies.Models;
using UnityAtoms.BaseAtoms;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Services.UGS
{
    /// <summary>
    /// Manages the state of the UGS Relay.
    /// </summary>
    public class RelayManager : MonoBehaviour, IRelayPlatform
    {
        [Header("References")]

        [SerializeField]
        ConditionBehavior clientReadyToJoinRelayCondition;

        [SerializeField]
        StringEventReference relayJoinCodeChangedEvent;

        [Header("Events")]

        [FormerlySerializedAs("onRelayStarted"), Tooltip("Called when the client recieves a relay join code from the lobby host.")]
        public UnityEvent<string> onClientRecievedJoinCode;


		#region IPlatform Fields

		public string playerID => Ref.Get<UGSManager>()?.playerID ?? "";

        public string displayName => Ref.Get<UGSManager>()?.displayName ?? "";

        public string loginState => Ref.Get<UGSManager>()?.loginState ?? "";

        public bool isLoggedIn => Ref.Get<UGSManager>()?.isLoggedIn ?? false;

        public bool hasLoginError => Ref.Get<UGSManager>()?.hasLoginError ?? false;

        public event IPlatform.DisplayNameChangedHandler OnDisplayNameChanged;

        #endregion

        #region Unity Methods

        void OnEnable()
        {
            relayJoinCodeChangedEvent.Event.Register(OnRelayJoinCodeChanged);

            Ref.Register<IRelayPlatform>(this);

            Ref.Instance.OnRegistered += Ref_OnRegister;
            Ref.Instance.OnUnregistered += Ref_OnUnregister;

            if (Ref.TryGet<LobbyManager>(out var lobbyManager))
            {
                Ref_OnRegister(typeof(LobbyManager), lobbyManager);
            }
        }

        void OnDisable()
        {
            relayJoinCodeChangedEvent.Event.Unregister(OnRelayJoinCodeChanged);

            if (Ref.Instance != null)
            {
                Ref.Unregister<IRelayPlatform>(this);

                Ref.Instance.OnRegistered -= Ref_OnRegister;
                Ref.Instance.OnUnregistered -= Ref_OnUnregister;

                if (Ref.TryGet<LobbyManager>(out var lobbyManager))
                {
                    Ref_OnUnregister(typeof(LobbyManager), lobbyManager);
                }
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Called when the Unity Atom containing the relay join code changes. 
        /// This is only called when the local player is the lobby host, and they request a relay allocation (currently in <see cref="Network.UGS.RelayConnectionBehaviour.StartServerAsync()"/>).
        /// </summary>
        /// <param name="value"></param>
        void OnRelayJoinCodeChanged(string value)
        {
            var lobbyManager = Ref.Get<LobbyManager>();

            if (lobbyManager.IsLocalPlayerLobbyHost())
                _ = lobbyManager.SetLobbyDataValue(lobbyManager.joinedLobby, new( Constants.Services.UGS.Relay.LobbyDataKeys.RelayJoinCode, value, DataObject.VisibilityOptions.Member));
        }

        void Ref_OnRegister(System.Type type, object instance)
        {
            if (instance is LobbyManager lobbyManager)
            {
                lobbyManager.onLobbyJoined.AddListener(LobbyManager_OnJoinedLobbyUpdated);
                lobbyManager.onJoinedLobbyUpdated.AddListener(LobbyManager_OnJoinedLobbyUpdated);

                if (lobbyManager.joinedLobby != null)
                {
                    if (lobbyManager.joinedLobby.Data.TryGetValue(Constants.Services.UGS.Relay.LobbyDataKeys.RelayJoinCode, out var relayJoinCode))
                    {
                        OnRelayJoinCodeChanged(relayJoinCode.Value);
                    }
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

            if (
                !lobbyManager.IsLocalPlayerLobbyHost() &&
                lobby != null &&
                lobby.Data[Constants.Services.UGS.Relay.LobbyDataKeys.RelayJoinCode].Value != "0" &&
                clientReadyToJoinRelayCondition.If()
                )
            {
                onClientRecievedJoinCode?.Invoke(lobby.Data[Constants.Services.UGS.Relay.LobbyDataKeys.RelayJoinCode].Value);
            }

        }

        #endregion
    }
}
