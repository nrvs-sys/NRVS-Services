using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Analytics;
using Unity.Services.Core;
using UnityEngine;

namespace Services.UGS 
{
    public class AnalyticsManager : MonoBehaviour
    {
        [SerializeField]
        bool recordEventsInEditor = false;

        public bool isInitialized { get; private set; } = false;

        private void Awake()
        {
            Ref.Register<AnalyticsManager>(this);
        }

        private void OnDestroy()
        {
            Ref.Unregister<AnalyticsManager>();
        }


        IEnumerator Start()
        {
            Debug.Log("Analytics Manager: Initializing...");

            // Wait for the Unity Game Services to be initialized by the UGS Manager
            while (!Ref.TryGet(out UGSManager uGSManager) || !uGSManager.isLoggedIn)
                yield return null;

            if (!Application.isEditor || recordEventsInEditor)
                AnalyticsService.Instance.StartDataCollection();

            isInitialized = true;

            Debug.Log("Analytics Manager: Initialized successfully.");
        }

        public void SendEvent(Unity.Services.Analytics.Event analyticsEvent) => SendEventAsync(analyticsEvent);

        public async void SendEventAsync(Unity.Services.Analytics.Event analyticsEvent)
        {
            while (!isInitialized)
                await Task.Yield();

            if (!Application.isEditor || recordEventsInEditor)
                AnalyticsService.Instance.RecordEvent(analyticsEvent);
        }
    } 
}
