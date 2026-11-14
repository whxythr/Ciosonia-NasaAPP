#ifndef SPACESIM_UNIFORMS_INCLUDED
#define SPACESIM_UNIFORMS_INCLUDED

// Per-material constants of the shared light model. Each planet-lit shader puts these macros inside its UnityPerMaterial CBUFFER
// (SRP Batcher needs one CBUFFER layout for all passes), then includes SpaceSimLighting.hlsl. AtmosphereRenderer fills them every frame:
// each planet has its own materials and sees the star from its own direction.
//
// _SpaceSimSunDir          unit vector towards the star (Unity world space)
// _SpaceSimSunColor        starlight (linear): a white Lambert surface facing the star shines this bright
// _SpaceSimPlanetCenter    planet centre (Unity world space, origin-relative, scaled)
// _SpaceSimPlanetRadius    reference radius x render scale [Unity units]
// _SpaceSimRenderScale     1 = true size; < 1 for distant bodies drawn in scaled space
// _SpaceSimHasAtmosphere   0 or 1
// _SpaceSimSkyAmbient      sky light at full daylight, tinted by the planet's air
// _SpaceSimRadiusM         reference radius [m] (true size, whatever the render scale)
// _SpaceSimShellM          height of the visual atmosphere [m]
// _SpaceSimAirScaleHeight  Rayleigh scale height [m]
// _SpaceSimCloudShadow     how dark cloud shadows are on the ground (0 = no clouds)
// _SpaceSimCloudBase/Top   altitude of the cloud deck [m]
// _SpaceSimCloudNoiseOrigin camera position in the cloud frame, wrapped to the noise period [m] (float precision at any distance)
// _SpaceSimCloudRow0..2    rows of the rotation world axes -> cloud frame (planet-fixed, plus the slow drift of the weather)
#define SPACESIM_PLANET_UNIFORMS            \
    float3 _SpaceSimSunDir;                 \
    float  _SpaceSimPlanetRadius;           \
    float3 _SpaceSimSunColor;               \
    float  _SpaceSimRenderScale;            \
    float3 _SpaceSimPlanetCenter;           \
    float  _SpaceSimHasAtmosphere;          \
    float3 _SpaceSimSkyAmbient;             \
    float  _SpaceSimRadiusM;                \
    float  _SpaceSimShellM;                 \
    float  _SpaceSimAirScaleHeight;         \
    float  _SpaceSimCloudShadow;            \
    float  _SpaceSimCloudBase;              \
    float  _SpaceSimCloudTop;               \
    float3 _SpaceSimCloudNoiseOrigin;       \
    float4 _SpaceSimCloudRow0;              \
    float4 _SpaceSimCloudRow1;              \
    float4 _SpaceSimCloudRow2;

#define SS_CLOUD_ROTATION float3x3(_SpaceSimCloudRow0.xyz, _SpaceSimCloudRow1.xyz, _SpaceSimCloudRow2.xyz)

// Scattering of the air, shared by the sky pass and everything that needs aerial perspective of its own (clouds, smoke).
//
// _CameraPlanetPos   camera relative to the planet centre (Unity axes) [m]
// _CameraHeight      camera height above the reference radius, computed in double [m]
// _BetaRayleigh      Rayleigh scattering at the surface [1/m]
// _BetaMie           Mie scattering at the surface [1/m]; _MieExtinction its extinction [1/m]
// _ScaleHeightR/M    [m]; _MieG anisotropy
// _HazeScattering    high haze layer at its peak [1/m]; _HazeAltitude and _HazeThickness [m]
// _SunIntensity      solar irradiance scale of the scattered light; _Exposure multiplies the result
// _MultiScatter      strength of the isotropic fill standing in for second and higher orders of scattering
#define SPACESIM_ATMOSPHERE_UNIFORMS        \
    float3 _CameraPlanetPos;                \
    float  _CameraHeight;                   \
    float3 _BetaRayleigh;                   \
    float  _BetaMie;                        \
    float3 _HazeScattering;                 \
    float  _MieExtinction;                  \
    float  _ScaleHeightR;                   \
    float  _ScaleHeightM;                   \
    float  _MieG;                           \
    float  _SunIntensity;                   \
    float  _Exposure;                       \
    float  _MultiScatter;                   \
    float  _HazeAltitude;                   \
    float  _HazeThickness;

#endif
