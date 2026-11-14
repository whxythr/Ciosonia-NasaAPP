using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Moves the landing legs of a <see cref="RocketModel"/> towards their target (added by <see cref="RocketModel.SetLegsDeployed"/>).
    /// Disables itself as soon as nothing moves, so it costs nothing in between.
    /// </summary>
    public sealed class RocketModelAnimator : MonoBehaviour
    {
        internal RocketModel Model;

        void Update()
        {
            if (Model == null || !Model.StepLegs(Time.deltaTime)) enabled = false;
        }
    }
}
