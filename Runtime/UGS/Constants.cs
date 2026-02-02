using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Constants.Services.UGS
{
    namespace Lobbies
    {
        /// <summary>
        /// Keys to use for Player Data in UGS Lobbies
        /// </summary>
        public static class PlayerDataKeys
        {
            public const string DisplayName = "DisplayName";
            public const string IsReady = "isReady";
        }
    }

    namespace Relay
    {
        /// <summary>
        /// Keys to use for Lobby Data when connecting through the UGS Relay
        /// </summary>
        public static class LobbyDataKeys
        {
            public const string RelayJoinCode = "RelayJoinCode";
        }
    }
}
