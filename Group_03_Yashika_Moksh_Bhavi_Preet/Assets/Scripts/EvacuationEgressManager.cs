using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// XR Systems Architect (C107) - Emergency Evacuation & Egress Manager.
/// Simulates dynamic volumetric smoke spread, physical glowing red fire hazard,
/// aggressive electrical light flickering, optical density attenuation via Beer-Lambert law,
/// and exposes crowd speed attenuation getters for Yashika's crowd evacuation model:
/// v = v0 * exp(-alpha * rho)
/// </summary>
public class EvacuationEgressManager : MonoBehaviour
{
    public static EvacuationEgressManager Instance { get; private set; }

    [Header("Smoke Simulation & Physics")]
    [Range(0.0f, 1.0f)]
    [Tooltip("Expanding volumetric smoke density rho in [0.0, 1.0]")]
    [SerializeField] private float smokeDensity = 0.35f;

    [Tooltip("Beer-Lambert extinction coefficient beta in m^-1 (strictly 0.45 per C107 spec)")]
    public float beerLambertExtinctionCoeff = 0.45f;

    [Tooltip("Yashika's crowd smoke sensitivity / deceleration constant alpha")]
    public float alphaCrowdSensitivity = 1.2f;

    [Tooltip("Nominal unimpeded pedestrian egress walking speed v0 in m/s (NBC 2016 / NFPA 101)")]
    public float nominalCrowdSpeed = 1.35f;

    [Tooltip("Hazard ignition location (Floor 3 near East Stairwell: x=7.0, y=9.0, z=0.0)")]
    public Vector3 hazardLocation = new Vector3(7.0f, 9.0f, 0.0f);

    [Tooltip("Floor 2 Hazard ignition location (Floor 2 near West Stairwell: x=-6.5, y=6.0, z=0.0)")]
    public Vector3 hazardLocationFloor2 = new Vector3(-6.5f, 6.0f, 0.0f);

    [Tooltip("Maximum effective radial dispersion of smoke in meters")]
    public float hazardRadius = 18.0f;

    [Header("Dynamic Growth")]
    [Tooltip("Enable automated dynamic smoke propagation over time")]
    public bool autoPropagateSmoke = true;

    [Tooltip("Rate of smoke density expansion per second")]
    public float smokeGrowthRate = 0.025f;

    [Header("Floor 3 Particle Systems")]
    public ParticleSystem smokeParticleSystem;
    public ParticleSystem fireParticleSystem;
    public ParticleSystem fireCoreGlowParticleSystem;
    public ParticleSystem embersParticleSystem;

    [Header("Floor 3 Lighting & Physical Meshes")]
    public Light fireFlickerLight;
    public Light floor3RedAmbientLight;
    public Light eastStairStrobeRed;
    public Light westStairStrobeGreen;
    public Light[] floor3CorridorLights;
    public Renderer[] glowingRedDebrisRenderers;
    public Renderer glowingEmberFloorRenderer;

    [Header("Floor 2 Fire Particle Systems")]
    public ParticleSystem smokeParticleSystem2;
    public ParticleSystem fireParticleSystem2;
    public ParticleSystem fireCoreGlowParticleSystem2;
    public ParticleSystem embersParticleSystem2;

    [Header("Floor 2 Lighting & Physical Meshes")]
    public Light fireFlickerLight2;
    public Light floor2RedAmbientLight;
    public Light[] floor2CorridorLights;
    public Renderer[] glowingRedDebrisRenderers2;
    public Renderer glowingEmberFloorRenderer2;

    [Header("Emergency Exit Strobes (NFPA 72 Compliant)")]
    public float strobeFrequency = 1.5f;
    public float strobeIntensityMax = 5.0f;

    [Header("Aggressive Light Flickering")]
    [Tooltip("Multiplier for electrical and fire light flicker speed")]
    public float flickerSpeed = 16.0f;
    [Tooltip("Probability of sudden electrical brownout / flicker dip per frame")]
    [Range(0f, 0.2f)]
    public float electricalDropoutChance = 0.06f;

    private float _strobeTimer = 0f;
    private Color _initialAmbientColor;
    private float _initialFogDensity;
    private float[] _corridorLightBaseIntensities;
    private float[] _corridorLight2BaseIntensities;
    private MaterialPropertyBlock _propBlock;

    /// <summary>
    /// Public property for volumetric smoke density rho in [0.0, 1.0].
    /// Setting this automatically updates optical attenuation and particle emission.
    /// </summary>
    public float SmokeDensity
    {
        get => smokeDensity;
        set => smokeDensity = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Exposes the Beer-Lambert extinction coefficient beta.
    /// </summary>
    public float BeerLambertExtinctionCoeff => beerLambertExtinctionCoeff;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        _initialAmbientColor = RenderSettings.ambientLight;
        _initialFogDensity = RenderSettings.fogDensity;
        _propBlock = new MaterialPropertyBlock();

        var allLights = FindObjectsByType<Light>(FindObjectsSortMode.None);

        // Auto-find Floor 3 corridor ceiling lights if not assigned
        if (floor3CorridorLights == null || floor3CorridorLights.Length == 0)
        {
            var f3List = new List<Light>();
            foreach (var l in allLights)
            {
                if (l != fireFlickerLight && l != floor3RedAmbientLight && l != fireFlickerLight2 && l != floor2RedAmbientLight && l != eastStairStrobeRed && l != westStairStrobeGreen)
                {
                    if (l.transform.position.y >= 8.5f && l.transform.position.y <= 12.5f)
                    {
                        f3List.Add(l);
                    }
                }
            }
            floor3CorridorLights = f3List.ToArray();
        }

        // Auto-find Floor 2 corridor ceiling lights if not assigned
        if (floor2CorridorLights == null || floor2CorridorLights.Length == 0)
        {
            var f2List = new List<Light>();
            foreach (var l in allLights)
            {
                if (l != fireFlickerLight && l != floor3RedAmbientLight && l != fireFlickerLight2 && l != floor2RedAmbientLight && l != eastStairStrobeRed && l != westStairStrobeGreen)
                {
                    if (l.transform.position.y >= 5.5f && l.transform.position.y < 8.5f)
                    {
                        f2List.Add(l);
                    }
                }
            }
            floor2CorridorLights = f2List.ToArray();
        }

        // Cache base intensities for Floor 3 corridor lights
        if (floor3CorridorLights != null && floor3CorridorLights.Length > 0)
        {
            _corridorLightBaseIntensities = new float[floor3CorridorLights.Length];
            for (int i = 0; i < floor3CorridorLights.Length; i++)
            {
                if (floor3CorridorLights[i] != null)
                {
                    _corridorLightBaseIntensities[i] = floor3CorridorLights[i].intensity;
                }
            }
        }

        // Cache base intensities for Floor 2 corridor lights
        if (floor2CorridorLights != null && floor2CorridorLights.Length > 0)
        {
            _corridorLight2BaseIntensities = new float[floor2CorridorLights.Length];
            for (int i = 0; i < floor2CorridorLights.Length; i++)
            {
                if (floor2CorridorLights[i] != null)
                {
                    _corridorLight2BaseIntensities[i] = floor2CorridorLights[i].intensity;
                }
            }
        }
    }

    private void Update()
    {
        // 1. Dynamic smoke density propagation
        if (autoPropagateSmoke && smokeDensity < 1.0f)
        {
            smokeDensity = Mathf.Clamp01(smokeDensity + smokeGrowthRate * Time.deltaTime);
        }

        // 2. Synchronize particle systems to smoke density
        UpdateParticleSystems();

        // 3. Apply Beer-Lambert visual fog and ambient lighting attenuation
        UpdateOpticalAttenuation();

        // 4. Update aggressive fire flicker, glowing red fire emission, and electrical ceiling lights
        UpdateAggressiveFlicker();

        // 5. Update emergency exit strobes
        UpdateStrobes();
    }

    /// <summary>
    /// Configures the volumetric smoke and flame particle parameters for Floor 3 and Floor 2.
    /// Ensures the glowing red flame cores remain vivid and luminous through any smoke density.
    /// </summary>
    private void UpdateParticleSystems()
    {
        // Floor 3 Particles
        UpdateSingleHazardParticles(smokeParticleSystem, fireParticleSystem, fireCoreGlowParticleSystem, embersParticleSystem);
        // Floor 2 Particles
        UpdateSingleHazardParticles(smokeParticleSystem2, fireParticleSystem2, fireCoreGlowParticleSystem2, embersParticleSystem2);
    }

    private void UpdateSingleHazardParticles(ParticleSystem smokePs, ParticleSystem firePs, ParticleSystem corePs, ParticleSystem embersPs)
    {
        if (smokePs != null)
        {
            var emission = smokePs.emission;
            emission.rateOverTime = Mathf.Lerp(6.0f, 75.0f, smokeDensity);

            var main = smokePs.main;
            float alpha = Mathf.Lerp(0.15f, 0.90f, smokeDensity);
            main.startColor = new Color(0.18f, 0.17f, 0.16f, alpha);
            main.startSize = Mathf.Lerp(1.5f, 4.2f, smokeDensity);
            main.startLifetime = Mathf.Lerp(4.0f, 9.0f, smokeDensity);
        }

        if (firePs != null)
        {
            var emission = firePs.emission;
            emission.rateOverTime = Mathf.Lerp(25.0f, 65.0f, Mathf.Clamp01(smokeDensity * 1.5f + 0.2f));
        }

        if (corePs != null)
        {
            var emission = corePs.emission;
            emission.rateOverTime = Mathf.Lerp(15.0f, 40.0f, smokeDensity + 0.3f);
        }

        if (embersPs != null)
        {
            var emission = embersPs.emission;
            emission.rateOverTime = Mathf.Lerp(8.0f, 35.0f, smokeDensity);
        }
    }

    /// <summary>
    /// Implements physical Beer-Lambert optical attenuation in Unity's rendering:
    /// Transmittance T = exp(-beta * rho * d)
    /// Calibrated so that bright glowing red fire and emissive beacons pierce through the haze.
    /// </summary>
    private void UpdateOpticalAttenuation()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;

        // Linear extinction formula: mu = beta * rho
        float targetFogDensity = Mathf.Lerp(0.003f, 0.075f, smokeDensity * (beerLambertExtinctionCoeff / 0.45f));
        RenderSettings.fogDensity = targetFogDensity;

        // Shift fog color towards dense, warm-gray particulate smoke
        Color clearFogColor = new Color(0.68f, 0.70f, 0.74f);
        Color denseSmokeColor = new Color(0.14f, 0.12f, 0.11f);
        RenderSettings.fogColor = Color.Lerp(clearFogColor, denseSmokeColor, smokeDensity);

        // Attenuate ambient lighting
        Color clearAmbient = new Color(0.35f, 0.35f, 0.38f);
        Color smokyAmbient = new Color(0.06f, 0.05f, 0.04f);
        RenderSettings.ambientLight = Color.Lerp(clearAmbient, smokyAmbient, smokeDensity);
    }

    /// <summary>
    /// Aggressive, high-dynamic-range flickering for fire and electrical corridor lights across Floor 3 & Floor 2.
    /// </summary>
    private void UpdateAggressiveFlicker()
    {
        float t = Time.time * flickerSpeed;

        // --- FLOOR 3 FIRE & LIGHTING ---
        if (fireFlickerLight != null)
        {
            float n1 = Mathf.PerlinNoise(t * 0.7f, 0.0f);
            float n2 = Mathf.PerlinNoise(0.0f, t * 1.5f);
            float n3 = Mathf.Sin(t * 2.3f) * 0.2f;
            float combinedNoise = (n1 * 0.6f + n2 * 0.4f + n3);

            fireFlickerLight.intensity = Mathf.Lerp(2.2f, 7.5f, Mathf.Clamp01(combinedNoise));
            fireFlickerLight.range = Mathf.Lerp(9.0f, 15.0f, n1);
            fireFlickerLight.color = Color.Lerp(new Color(1.0f, 0.02f, 0.0f), new Color(1.0f, 0.12f, 0.02f), n2);
        }

        if (floor3RedAmbientLight != null)
        {
            float redPulse = 3.0f + Mathf.Sin(Time.time * 6.0f) * 1.2f + (Mathf.PerlinNoise(t * 0.5f, 5.0f) * 1.5f);
            floor3RedAmbientLight.intensity = redPulse;
        }

        FlickerCorridorCeilingLights(floor3CorridorLights, _corridorLightBaseIntensities, t, 0f);
        UpdateDebrisGlow(glowingRedDebrisRenderers, t, 0f);
        UpdateFloorBedGlow(glowingEmberFloorRenderer, 0f);

        // --- FLOOR 2 FIRE & LIGHTING ---
        if (fireFlickerLight2 != null)
        {
            float n1 = Mathf.PerlinNoise(t * 0.75f, 7.3f);
            float n2 = Mathf.PerlinNoise(3.1f, t * 1.4f);
            float n3 = Mathf.Cos(t * 2.1f) * 0.2f;
            float combinedNoise = (n1 * 0.6f + n2 * 0.4f + n3);

            fireFlickerLight2.intensity = Mathf.Lerp(2.2f, 7.5f, Mathf.Clamp01(combinedNoise));
            fireFlickerLight2.range = Mathf.Lerp(9.0f, 15.0f, n1);
            fireFlickerLight2.color = Color.Lerp(new Color(1.0f, 0.02f, 0.0f), new Color(1.0f, 0.12f, 0.02f), n2);
        }

        if (floor2RedAmbientLight != null)
        {
            float redPulse = 3.0f + Mathf.Sin(Time.time * 5.5f + 1.2f) * 1.2f + (Mathf.PerlinNoise(t * 0.5f, 12.0f) * 1.5f);
            floor2RedAmbientLight.intensity = redPulse;
        }

        FlickerCorridorCeilingLights(floor2CorridorLights, _corridorLight2BaseIntensities, t, 41.5f);
        UpdateDebrisGlow(glowingRedDebrisRenderers2, t, 19.3f);
        UpdateFloorBedGlow(glowingEmberFloorRenderer2, 2.0f);
    }

    private void FlickerCorridorCeilingLights(Light[] lights, float[] baseIntensities, float t, float offset)
    {
        if (lights == null) return;

        for (int i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            if (light == null) continue;

            float baseInt = (baseIntensities != null && i < baseIntensities.Length)
                ? baseIntensities[i]
                : 1.0f;

            float lightNoise = Mathf.PerlinNoise(t * 0.9f + (i * 17.3f) + offset, t * 0.3f);
            float flickerFactor = Mathf.Lerp(0.2f, 1.2f, lightNoise);

            if (UnityEngine.Random.value < electricalDropoutChance)
            {
                flickerFactor = UnityEngine.Random.value < 0.3f ? 0.02f : 1.8f;
            }

            float smokeDimming = Mathf.Lerp(1.0f, 0.45f, smokeDensity);
            light.intensity = baseInt * flickerFactor * smokeDimming;
        }
    }

    private void UpdateDebrisGlow(Renderer[] debrisRenderers, float t, float offset)
    {
        if (debrisRenderers == null) return;

        float heatPulse = 3.5f + Mathf.Sin(Time.time * 8.0f + offset) * 1.5f + Mathf.PerlinNoise(t * 0.4f, 10.0f + offset) * 2.0f;
        Color emissionColor = new Color(1.0f, 0.05f, 0.01f) * heatPulse;

        foreach (var rend in debrisRenderers)
        {
            if (rend != null)
            {
                rend.GetPropertyBlock(_propBlock);
                _propBlock.SetColor("_EmissionColor", emissionColor);
                rend.SetPropertyBlock(_propBlock);
            }
        }
    }

    private void UpdateFloorBedGlow(Renderer emberFloorRend, float phaseOffset)
    {
        if (emberFloorRend != null)
        {
            float floorPulse = 2.5f + Mathf.Sin(Time.time * 5.0f + phaseOffset) * 1.0f;
            Color floorEmission = new Color(1.0f, 0.08f, 0.01f) * floorPulse;
            emberFloorRend.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_EmissionColor", floorEmission);
            emberFloorRend.SetPropertyBlock(_propBlock);
        }
    }

    /// <summary>
    /// Flashes emergency exit strobes compliant with NFPA 72 & NFPA 101.
    /// East Stairwell: Flashing RED Strobe (Danger / Compromised Exit)
    /// West Stairwell: Flashing GREEN Strobe (Primary Evacuation Path)
    /// </summary>
    private void UpdateStrobes()
    {
        _strobeTimer += Time.deltaTime * strobeFrequency;
        bool strobeState = (Mathf.Sin(_strobeTimer * Mathf.PI * 2.0f) > 0.15f);
        float strobeVal = strobeState ? strobeIntensityMax : 0.05f;

        if (eastStairStrobeRed != null)
        {
            eastStairStrobeRed.intensity = strobeVal;
        }

        if (westStairStrobeGreen != null)
        {
            westStairStrobeGreen.intensity = strobeVal;
        }
    }

    // =========================================================================
    // MATHEMATICAL MODELS & PUBLIC GETTERS FOR CROWD EVACUATION (YASHIKA'S LOGIC)
    // =========================================================================

    public float GetAttenuatedSpeed(float v0, float rho, float alpha = -1.0f)
    {
        if (alpha < 0f) alpha = alphaCrowdSensitivity;
        float clampedRho = Mathf.Clamp01(rho);
        return v0 * Mathf.Exp(-alpha * clampedRho);
    }

    public float GetCurrentCrowdSpeed(float v0 = -1.0f)
    {
        if (v0 <= 0f) v0 = nominalCrowdSpeed;
        return GetAttenuatedSpeed(v0, smokeDensity, alphaCrowdSensitivity);
    }

    public float GetSmokeDensityAt(Vector3 worldPosition)
    {
        float rho1 = ComputeLocalSmoke(worldPosition, hazardLocation);
        float rho2 = ComputeLocalSmoke(worldPosition, hazardLocationFloor2);
        return Mathf.Clamp01(Mathf.Max(rho1, rho2));
    }

    private float ComputeLocalSmoke(Vector3 worldPosition, Vector3 origin)
    {
        float dx = worldPosition.x - origin.x;
        float dz = worldPosition.z - origin.z;
        float planarDist = Mathf.Sqrt(dx * dx + dz * dz);

        float dy = worldPosition.y - origin.y;
        float verticalFactor;
        if (dy < -1.5f)
        {
            verticalFactor = Mathf.Clamp01(1.0f - (Mathf.Abs(dy) / 6.0f));
        }
        else if (dy >= -1.5f && dy <= 3.5f)
        {
            verticalFactor = 1.0f;
        }
        else
        {
            verticalFactor = Mathf.Clamp01(0.85f - ((dy - 3.5f) / 6.0f));
        }

        float radialFactor = Mathf.Clamp01(1.0f - (planarDist / hazardRadius));
        float diffusion = Mathf.Pow(radialFactor, 1.5f) * verticalFactor;

        return Mathf.Clamp01(smokeDensity * diffusion);
    }

    public float GetCrowdSpeedAtPosition(Vector3 agentPosition, float v0 = -1.0f)
    {
        if (v0 <= 0f) v0 = nominalCrowdSpeed;
        float localRho = GetSmokeDensityAt(agentPosition);
        return GetAttenuatedSpeed(v0, localRho, alphaCrowdSensitivity);
    }

    public float GetTransmittance(float distance, float rho)
    {
        float extinction = beerLambertExtinctionCoeff * Mathf.Clamp01(rho);
        return Mathf.Exp(-extinction * Mathf.Max(0.0f, distance));
    }

    public float GetOpticalDensity(float distance, float rho)
    {
        float t = GetTransmittance(distance, rho);
        return t > 0.00001f ? -Mathf.Log10(t) : 5.0f;
    }

    public float GetVisibilityDistance(float rho, float thresholdTransmittance = 0.05f)
    {
        float extinction = beerLambertExtinctionCoeff * Mathf.Clamp01(rho);
        if (extinction <= 0.001f) return 100.0f;
        return -Mathf.Log(thresholdTransmittance) / extinction;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        RenderSettings.ambientLight = _initialAmbientColor;
        RenderSettings.fogDensity = _initialFogDensity;
    }
}
