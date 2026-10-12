using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SetupSmokeHazard
{
    [MenuItem("Hostel/Setup Smoke Hazard")]
    public static string Run()
    {
        string scenePath = "Assets/Scenes/SampleScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // 1. Root GameObject for Hazard
        const string hazardRootName = "Floor3_FireSmokeHazard_EastStairwell";
        GameObject existing = GameObject.Find(hazardRootName);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing);
        }

        GameObject hazardRoot = new GameObject(hazardRootName);
        // Floor 3 (y=9.0m) near East Stairwell (x=8.5m, z=0.0m)
        Vector3 hazardPos = new Vector3(8.5f, 9.0f, 0.0f);
        hazardRoot.transform.position = hazardPos;

        // 2. Attach EvacuationEgressManager
        var egressMgr = hazardRoot.AddComponent<EvacuationEgressManager>();
        egressMgr.beerLambertExtinctionCoeff = 0.45f;
        egressMgr.alphaCrowdSensitivity = 1.2f;
        egressMgr.nominalCrowdSpeed = 1.35f;
        egressMgr.hazardLocation = hazardPos;
        egressMgr.hazardRadius = 18.0f;
        egressMgr.SmokeDensity = 0.35f; // Initial expanding density
        egressMgr.autoPropagateSmoke = true;
        egressMgr.smokeGrowthRate = 0.025f;

        // 3. Volumetric Smoke Particle System
        GameObject smokeGo = new GameObject("VolumetricSmokeParticles");
        smokeGo.transform.SetParent(hazardRoot.transform, false);
        smokeGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);

        var psSmoke = smokeGo.AddComponent<ParticleSystem>();
        egressMgr.smokeParticleSystem = psSmoke;

        var mainSmoke = psSmoke.main;
        mainSmoke.duration = 5.0f;
        mainSmoke.loop = true;
        mainSmoke.startLifetime = 7.0f;
        mainSmoke.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
        mainSmoke.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.8f);
        mainSmoke.startColor = new Color(0.18f, 0.17f, 0.16f, 0.45f);
        mainSmoke.gravityModifier = -0.05f; // Gently rise toward ceiling
        mainSmoke.simulationSpace = ParticleSystemSimulationSpace.World;
        mainSmoke.maxParticles = 500;

        var emissionSmoke = psSmoke.emission;
        emissionSmoke.rateOverTime = 25.0f;

        var shapeSmoke = psSmoke.shape;
        shapeSmoke.shapeType = ParticleSystemShapeType.Box;
        shapeSmoke.scale = new Vector3(1.4f, 0.4f, 1.2f);

        var velSmoke = psSmoke.velocityOverLifetime;
        velSmoke.enabled = true;
        // Drift westwards down the Floor 3 central corridor (-X direction)
        velSmoke.x = new ParticleSystem.MinMaxCurve(-0.8f, -0.3f);
        velSmoke.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        velSmoke.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        var sizeSmoke = psSmoke.sizeOverLifetime;
        sizeSmoke.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0.0f, 0.3f);
        sizeCurve.AddKey(0.4f, 0.85f);
        sizeCurve.AddKey(1.0f, 1.4f);
        sizeSmoke.size = new ParticleSystem.MinMaxCurve(1.0f, sizeCurve);

        var colSmoke = psSmoke.colorOverLifetime;
        colSmoke.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.25f, 0.22f, 0.2f), 0.0f),
                new GradientColorKey(new Color(0.15f, 0.14f, 0.14f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.65f, 0.25f),
                new GradientAlphaKey(0.85f, 0.7f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colSmoke.color = grad;

        // 4. Fire Flame Core Particle System
        GameObject fireGo = new GameObject("FlameCoreParticles");
        fireGo.transform.SetParent(hazardRoot.transform, false);
        fireGo.transform.localPosition = new Vector3(0f, 0.1f, 0f);

        var psFire = fireGo.AddComponent<ParticleSystem>();
        egressMgr.fireParticleSystem = psFire;

        var mainFire = psFire.main;
        mainFire.duration = 2.0f;
        mainFire.loop = true;
        mainFire.startLifetime = 1.0f;
        mainFire.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        mainFire.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        mainFire.startColor = new Color(1.0f, 0.45f, 0.08f, 0.85f);
        mainFire.simulationSpace = ParticleSystemSimulationSpace.Local;
        mainFire.maxParticles = 120;

        var emissionFire = psFire.emission;
        emissionFire.rateOverTime = 30.0f;

        var shapeFire = psFire.shape;
        shapeFire.shapeType = ParticleSystemShapeType.Cone;
        shapeFire.angle = 15.0f;
        shapeFire.radius = 0.35f;

        // 5. Fire Embers Particle System
        GameObject embersGo = new GameObject("FireEmbersParticles");
        embersGo.transform.SetParent(hazardRoot.transform, false);
        embersGo.transform.localPosition = new Vector3(0f, 0.2f, 0f);

        var psEmbers = embersGo.AddComponent<ParticleSystem>();
        egressMgr.embersParticleSystem = psEmbers;

        var mainEmbers = psEmbers.main;
        mainEmbers.duration = 3.0f;
        mainEmbers.loop = true;
        mainEmbers.startLifetime = 2.5f;
        mainEmbers.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        mainEmbers.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
        mainEmbers.startColor = new Color(1.0f, 0.8f, 0.2f, 0.95f);
        mainEmbers.gravityModifier = -0.1f;
        mainEmbers.simulationSpace = ParticleSystemSimulationSpace.World;
        mainEmbers.maxParticles = 60;

        var emissionEmbers = psEmbers.emission;
        emissionEmbers.rateOverTime = 12.0f;

        // 6. Flickering Fire Light
        GameObject lightGo = new GameObject("FireFlickerLight");
        lightGo.transform.SetParent(hazardRoot.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);

        var fireLight = lightGo.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1.0f, 0.42f, 0.05f);
        fireLight.intensity = 2.8f;
        fireLight.range = 7.5f;
        fireLight.shadows = LightShadows.Soft;
        egressMgr.fireFlickerLight = fireLight;

        // 7. Flashing Emergency Exit Strobes (NFPA 72 & NFPA 101)
        // East Stairwell (Compromised Exit Strobe - RED)
        GameObject eastStrobeGo = new GameObject("EmergencyStrobe_EastStairwell_RED");
        eastStrobeGo.transform.SetParent(hazardRoot.transform, false);
        // Above East Stairwell door (x = 8.6m, y = 11.2m, z = 0.0m)
        eastStrobeGo.transform.position = new Vector3(8.6f, 11.2f, 0.0f);

        var eastLight = eastStrobeGo.AddComponent<Light>();
        eastLight.type = LightType.Point;
        eastLight.color = new Color(1.0f, 0.08f, 0.05f); // High-visibility emergency RED
        eastLight.intensity = 4.0f;
        eastLight.range = 5.5f;
        egressMgr.eastStairStrobeRed = eastLight;

        // Strobe beacon indicator housing
        var eastBeacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
        eastBeacon.name = "StrobeBeacon_Housing_Red";
        eastBeacon.transform.SetParent(eastStrobeGo.transform, false);
        eastBeacon.transform.localScale = new Vector3(0.25f, 0.18f, 0.18f);
        var eastBeaconRenderer = eastBeacon.GetComponent<MeshRenderer>();
        var matRedStrobe = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        matRedStrobe.color = Color.red;
        matRedStrobe.EnableKeyword("_EMISSION");
        matRedStrobe.SetColor("_EmissionColor", Color.red * 2.5f);
        eastBeaconRenderer.material = matRedStrobe;

        // West Stairwell (Primary Designated Safe Evacuation Exit Strobe - GREEN)
        GameObject westStrobeGo = new GameObject("EmergencyStrobe_WestStairwell_GREEN");
        westStrobeGo.transform.SetParent(hazardRoot.transform, false);
        // Above West Stairwell door (x = -8.6m, y = 11.2m, z = 0.0m)
        westStrobeGo.transform.position = new Vector3(-8.6f, 11.2f, 0.0f);

        var westLight = westStrobeGo.AddComponent<Light>();
        westLight.type = LightType.Point;
        westLight.color = new Color(0.0f, 1.0f, 0.28f); // High-visibility safe egress GREEN
        westLight.intensity = 4.0f;
        westLight.range = 5.5f;
        egressMgr.westStairStrobeGreen = westLight;

        var westBeacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
        westBeacon.name = "StrobeBeacon_Housing_Green";
        westBeacon.transform.SetParent(westStrobeGo.transform, false);
        westBeacon.transform.localScale = new Vector3(0.25f, 0.18f, 0.18f);
        var westBeaconRenderer = westBeacon.GetComponent<MeshRenderer>();
        var matGreenStrobe = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        matGreenStrobe.color = Color.green;
        matGreenStrobe.EnableKeyword("_EMISSION");
        matGreenStrobe.SetColor("_EmissionColor", Color.green * 2.5f);
        westBeaconRenderer.material = matGreenStrobe;

        // Save Scene
        EditorUtility.SetDirty(hazardRoot);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        return $"SUCCESS: Created Floor 3 Volumetric Smoke & Fire Hazard at {hazardPos} with EvacuationEgressManager, Beer-Lambert beta=0.45, particle systems, and NFPA 101 Red/Green emergency strobes.";
    }

    public static void BatchRun()
    {
        string result = Run();
        Debug.Log(result);
    }
}
