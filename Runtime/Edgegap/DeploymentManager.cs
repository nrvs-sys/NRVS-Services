using Services.UGS;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Lobbies.Models;
using UnityAtoms.BaseAtoms;
using UnityEngine;
using UnityEngine.Events;

namespace Services.Edgegap
{
    public class DeploymentManager : MonoBehaviour
    {
        public struct DeploymentSession
        {
            public string ipAddress;
            public int port;
        }

        [Header("Developer Settings")]
        [SerializeField]
        bool useDevelopmentVersionInEditor = false;

        [SerializeField]
        BoolVariable forceDevDeploymentVersion;

        [Header("References")]

        [SerializeField]
        StringReference authToken;

        [Header("Events")]

        [SerializeField]
        UnityEvent<string> onRequestIdReceived;

        [SerializeField]
        UnityEvent<DeploymentSession> onDeploymentSessionReady;

        string requestId = "";

        #region Unity Methods

        void OnEnable()
        {
            Ref.Register<DeploymentManager>(this);

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
                Ref.Unregister<DeploymentManager>(this);

                Ref.Instance.OnRegistered -= Ref_OnRegister;
                Ref.Instance.OnUnregistered -= Ref_OnUnregister;

                if (Ref.TryGet<LobbyManager>(out var lobbyManager))
                {
                    Ref_OnUnregister(typeof(LobbyManager), lobbyManager);
                }
            }
        }

        #endregion

        /// <summary>
        /// Starts a deployment session with Edgegap.
        /// </summary>
        public async Task<bool> StartDeploymentSession()
        {
            Debug.Log("Deployment Manager: Starting deployment session");

            var result = await Ref.Get<LobbyIpLookup>().GetLobbyIpsAsync();
            var clientIps = result.lobbyIps;

            if (result.isError || clientIps.Count == 0)
            {
                Debug.LogError("Deployment Manager: No client IPs available to start deployment session.");
                return false;
            }

            var deploymentService = new DeploymentService(authToken.Value);

            var deploymentVersion = GetDeploymentVersion();

            Debug.Log($"Deployment Manager: Creating Deployment with app version: {deploymentVersion}");

            try
            {
                var deploymentResponse = await deploymentService.CreateDeploymentSessionAsync(clientIps, deploymentVersion);

                if (deploymentResponse.requestId != null)
                {
                    Debug.Log($"Deployment Manager: Deployment session started with request ID: {deploymentResponse.requestId}. Sending Session to clients");

                    if (Ref.TryGet(out LobbyManager lobbyManager) && lobbyManager.IsLocalPlayerInJoinedLobby())
                    {
                        _ = lobbyManager.SetLobbyDataValues(lobbyManager.joinedLobby, new LobbyManager.LobbyDataValue(Constants.Services.Edgegap.Deployment.LobbyDataKeys.RequestId, deploymentResponse.requestId, DataObject.VisibilityOptions.Member));
                    }

                    return true;
                }
                else
                {
                    Debug.LogError("Deployment Manager: Failed to start deployment session.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Deployment Manager: Failed to create deployment session. Exception: {ex.Message}");
            }

            return false;
        }

        public async void StopDeploymentSession()
        {
            if (string.IsNullOrEmpty(requestId))
            {
                Debug.LogError("Deployment Manager: No deployment session to stop.");
                return;
            }

            Debug.Log("Deployment Manager: Stopping deployment session");

            var deploymentService = new DeploymentService(authToken.Value);

            var deploymentStatusResponse = await deploymentService.GetDeploymentSessionStatusAsync(requestId);
            if (deploymentStatusResponse.running)
            {
                await deploymentService.StopDeploymentSessionAsync(requestId);
                Debug.Log("Deployment Manager: Deployment session stopped.");
            }
            else
            {
                Debug.LogError("Deployment Manager: Deployment session is not running.");
            }
        }

        async void JoinDeploymentSession()
        {
            Debug.Log("Deployment Manager: Joining deployment session");
            
            var deploymentService = new DeploymentService(authToken.Value);

            var deploymentReady = false;

            while (!deploymentReady)
            {
                var deploymentStatusResponse = await deploymentService.GetDeploymentSessionStatusAsync(requestId);

                if (deploymentStatusResponse.running)
                {
                    // Concatenate the ports into a string for logging
                    var ports = string.Join(", ", deploymentStatusResponse.ports.Select(p => $"{p.Key}: {p.Value.external}"));
                    Debug.Log($"Deployment Manager: Deployment session is running. Public IP: {deploymentStatusResponse.fqdn}, Ports: {ports}");

                    // Use the first port in the map (this may need to be made dynamic)
                    var port = deploymentStatusResponse.ports.Values.First();

                    deploymentReady = true;

                    onDeploymentSessionReady?.Invoke(new() { ipAddress = deploymentStatusResponse.fqdn, port = port.external });
                }
                else
                {
                    Debug.Log("Deployment Manager: Deployment session is not ready yet. Waiting...");
                    await Task.Delay(2000);
                }
            }
        }

        string GetDeploymentVersion()
        {
            bool isDevelopmentBuild = false;
            bool isDemoMode = false;

#if UNITY_EDITOR
            isDevelopmentBuild = useDevelopmentVersionInEditor;
#elif DEVELOPMENT_BUILD

            isDevelopmentBuild = true;
#endif

            if (forceDevDeploymentVersion != null && forceDevDeploymentVersion.Value)
                isDevelopmentBuild = true;

#if DEMO_MODE
            isDemoMode = true;
#endif
            if (isDevelopmentBuild)
            {
                return isDemoMode ? "dev-demo" : "dev";
            }
            else
            {
                return isDemoMode ? "prod-demo" : "prod";
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
                var requestId = lobby.Data.GetValueOrDefault(Constants.Services.Edgegap.Deployment.LobbyDataKeys.RequestId, new DataObject(DataObject.VisibilityOptions.Member, "")).Value;

                if (requestId != this.requestId)
                {
                    var previousRequestId = this.requestId;
                    this.requestId = requestId;

                    Debug.Log($"Deployment Manager: Request Id updated from {previousRequestId} to {this.requestId}");

                    if (!string.IsNullOrEmpty(this.requestId))
                    {
                        JoinDeploymentSession();

                        onRequestIdReceived?.Invoke(this.requestId);
                    }
                }
            }
        }

        #endregion
    }
}
