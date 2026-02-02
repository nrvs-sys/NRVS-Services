using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Services.Edgegap
{
    [CreateAssetMenu(fileName = "Deployment_ New", menuName = "Behaviors/Services/Edgegap/Deployment")]
    public class DeploymentBehavior : ScriptableObject
    {
        public void StartDeploymentSession() => Ref.Get<DeploymentManager>()?.StartDeploymentSession();

        public void StopDeploymentSession() => Ref.Get<DeploymentManager>()?.StopDeploymentSession();
    }
}
