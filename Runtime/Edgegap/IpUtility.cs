using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityAtoms.BaseAtoms;
using UnityEngine;

namespace Services.Edgegap
{
    public class IpUtility : MonoBehaviour
    {
        [SerializeField]
        StringReference authToken;

        public string ipAddress { get; private set; }

        public bool isInitialized { get; private set; }

        IpService ipService;

        void Awake()
        {
            Ref.Register<IpUtility>(this);

            ApplicationInfo.OnInternetAvailabilityChanged += ApplicationInfo_OnInternetAvailabilityChanged;
        }

        void OnDestroy()
        {
            Ref.Unregister<IpUtility>(this);
        }

        void Start() => _ = Initialize();

        async Task Initialize()
        {
            isInitialized = false;

            ipAddress = string.Empty;

            ipService = new(authToken);

            try
            {
                ipAddress = await ipService.GetPublicIpAsync();

                isInitialized = true;

                Debug.Log($"IP Utility: Initialized with public address - {ipAddress}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"IP Utility: Failed to initialize. {e.Message}");
            }
        }

        public async Task<string> GetIpAddress()
        {
            if (!isInitialized)
            {
                await Initialize();

                if (!isInitialized)
                {
                    Debug.LogError("IP Utility: Not initialized. Cannot get IP address.");
                    return string.Empty;
                }
            }

            return ipAddress;
        }

        private void ApplicationInfo_OnInternetAvailabilityChanged(ApplicationInfo.InternetAvailabilityStatus status)
        {
            if (status == ApplicationInfo.InternetAvailabilityStatus.Online)
            {
                _ = Initialize();
            }
            else
            {
                isInitialized = false;
                ipAddress = string.Empty;
            }
        }
    }
}
