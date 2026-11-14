using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The body drawn for the EVA (the robot from the imported model, or the procedural astronaut as a fallback). Local
    /// frame of <see cref="Root"/>: origin at the feet on the ground, +Y up, +Z forward. Purely visual: it never feeds back
    /// into the <see cref="SurfaceWalker"/> physics.
    /// </summary>
    public interface IExplorerBody
    {
        Transform Root { get; }
        /// <summary>The eye (camera) in the root frame; follows the lean and the bob.</summary>
        Vector3 EyeLocal { get; }
        /// <summary>Forward lean [degrees] (tan θ = a/g plus the gait's own lean).</summary>
        float LeanDegrees { get; }
        bool FirstPerson { get; }
        /// <summary>Renderers around the eye: shadow-only in first person, drawn in the third-person view.</summary>
        IReadOnlyList<Renderer> HeadRenderers { get; }
        /// <summary>All other renderers: always drawn and casting shadows.</summary>
        IReadOnlyList<Renderer> BodyRenderers { get; }
        void SetFirstPerson(bool value);
        void Animate(SurfaceWalker walker, Vector3d forward, Vector3d right, float dt);
        void Destroy();
    }
}
