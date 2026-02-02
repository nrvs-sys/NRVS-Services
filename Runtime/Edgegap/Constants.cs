using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Constants.Services.Edgegap
{
    public static class API
    {
        public const string Url = "https://api.edgegap.com";

        public const string V1 = "/v1";

        public const string UrlV1 = Url + V1;

        public const string AppName = "MAGE";

        /// <summary>
        ///     Used by the HTTP request's Content-Type header.
        ///     It should always be set to "application/json" while communicating with EdgeGap API.
        /// </summary>
        public const string JsonHeaderType = "application/json";
    }

    /// <summary>
    /// Keys to use for Lobby Data when connecting through the Edgegap Services
    /// </summary>
    public static class LobbyDataKeys
    {
        public const string LobbyIPLookupState = "LobbyIpLookupState";
    }

    /// <summary>
    /// Keys to use for Lobby Player Data when connecting through the Edgegap Services
    /// </summary>
    public static class LobbyPlayerDataKeys
    {
        public const string PublicIp = "PublicIp";
    }

    namespace Deployment
    {
        /// <summary>
        /// Keys to use for Lobby Data when interacting with the Edgegap Deployment Service
        /// </summary>
        public static class LobbyDataKeys
        {
            public const string RequestId = "RequestId";
        }
    }

    namespace Relay
    {
        /// <summary>
        /// Keys to use for Lobby Data when connecting through the Edgegap Relay
        /// </summary>
        public static class LobbyDataKeys
        {
            public const string RelaySessionId = "RelaySessionId";
        }

    }
}
