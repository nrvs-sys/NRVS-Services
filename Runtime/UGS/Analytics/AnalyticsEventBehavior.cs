using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using Unity.Services.Analytics;
using UnityAtoms.BaseAtoms;
using UnityEngine;
using Utility;

[CreateAssetMenu(fileName = "Analytics Event_ New", menuName = "Behaviors/Services/UGS/Analytics Event")]
public class AnalyticsEventBehavior : ScriptableObjectBehavior
{
    [System.Serializable]
    public struct EventParameter
    {
        public enum Type
        {
            String,
            Int,
            Float,
            Bool
        }

        public Type type;

        [Space(5)]

        [EnableIf(nameof(type), Type.String)]
        public StringReference stringValue;
        [EnableIf(nameof(type), Type.Int)]
        public IntReference intValue;
        [EnableIf(nameof(type), Type.Float)]
        public FloatReference floatValue;
        [EnableIf(nameof(type), Type.Bool)]
        public BoolReference boolValue;
    }

    [Header("Event Settings")]

    [SerializeField]
    string eventName = "";

    [SerializeField]
    Utility.SerializableDictionary<string, EventParameter> parameters = new();

    [Space(10)]
    
    [SerializeField]
    bool debugLogEvent = false;

    /// <summary>
    /// Invokes the analytics event with the specified parameters, in addition to any parameters already defined in the behavior.
    /// </summary>
    /// <param name="eventParameters"></param>
    public void Invoke(Dictionary<string, EventParameter> eventParameters)
    {
        var analyticsEvent = GetEvent();
        if (analyticsEvent != null)
        {
            foreach (var kvp in eventParameters)
            {
                var key = kvp.Key;
                var param = kvp.Value;
                switch (param.type)
                {
                    case EventParameter.Type.String:
                        analyticsEvent.Add(key, param.stringValue.Value);
                        break;
                    case EventParameter.Type.Int:
                        analyticsEvent.Add(key, param.intValue.Value);
                        break;
                    case EventParameter.Type.Float:
                        analyticsEvent.Add(key, param.floatValue.Value);
                        break;
                    case EventParameter.Type.Bool:
                        analyticsEvent.Add(key, param.boolValue.Value);
                        break;
                }
            }

            ExecuteAsync(analyticsEvent);
        }
    }

    protected override void Execute() 
    {
        var analyticsEvent = GetEvent();
        ExecuteAsync(analyticsEvent);
    }

    async void ExecuteAsync(Unity.Services.Analytics.Event analyticsEvent)
    {
        if (analyticsEvent == null)
            return;

        Services.UGS.AnalyticsManager analyticsManager;

        // Wait for Analytics Manager to be ready
        while (!Ref.TryGet(out analyticsManager) || !analyticsManager.isInitialized)
            await System.Threading.Tasks.Task.Yield();

        if (debugLogEvent)
            Debug.Log($"Analytics Event Behavior: Sending event '{eventName}' with parameters: {parameters.ToString()}");

        analyticsManager.SendEvent(analyticsEvent);
    }

    CustomEvent GetEvent()
    {
        if (string.IsNullOrEmpty(eventName))
        {
            Debug.LogWarning($"Analytics Event Behavior: No event name specified in {name}.");
            return null;
        }

        var customEvent = new CustomEvent(eventName);

        foreach (var kvp in parameters)
        {
            var key = kvp.Key;
            var param = kvp.Value;
            switch (param.type)
            {
                case EventParameter.Type.String:
                    customEvent.Add(key, param.stringValue.Value);
                    break;
                case EventParameter.Type.Int:
                    customEvent.Add(key, param.intValue.Value);
                    break;
                case EventParameter.Type.Float:
                    customEvent.Add(key, param.floatValue.Value);
                    break;
                case EventParameter.Type.Bool:
                    customEvent.Add(key, param.boolValue.Value);
                    break;
            }
        }

        return customEvent;
    }
}
