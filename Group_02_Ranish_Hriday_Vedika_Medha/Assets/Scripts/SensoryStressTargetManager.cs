using System.Collections;
using UnityEngine;

namespace IVRAR.Group02.SensoryStress
{
    /// <summary>Configurable scenario timing, target presentation, low-light settings and sensory cues.</summary>
    public sealed class SensoryStressTargetManager : MonoBehaviour
    {
        [Header("Lighting")]
        [Range(0.5f, 5f)] public float ambientLux = 1.2f;
        public Light sceneDirectionalLight;
        public Light traineeFlashlight;
        [Min(0f)] public float fogExtinctionPerMeter = 0.025f;
        [Min(0.01f)] public float referenceFlashlightDistance = 5f;
        [Min(0.01f)] public float referenceIlluminanceLux = 3f;

        [Header("Sensory Cues (Unity gain is not calibrated acoustic SPL)")]
        public AudioSource stressAudioSource;
        public AudioClip gunshotClip;
        public AudioClip sirenAlarmClip;
        [Range(0f, 1f)] public float maximumCueGain = 0.25f;
        public Light peripheralStrobeLight;
        [Range(0, 6)] public int strobePulseCount = 2;
        [Range(1, 6)] public float strobeFrequencyHz = 6f;
        [Range(0f, 1f)] public float maximumStrobeIntensity = 0.2f;

        [Header("Target Spawning")]
        public GameObject hostileTargetPrefab;
        public GameObject civilianTargetPrefab;
        public Transform[] spawnAnchors;
        [Min(0.1f)] public float exposureDurationSeconds = 2.5f;
        [Min(0.1f)] public float meanInterTargetIntervalSeconds = 3f;
        [Range(0f, 1f)] public float hostileProbability = 0.5f;
        public int randomSeed = 2047;
        public ReactionLatencyTelemetryLogger telemetryLogger;

        private bool isScenarioActive;
        private System.Random random;
        private Coroutine scenarioCoroutine;
        private bool strobeRunning;
        private float originalFogDensity;
        private bool originalFogEnabled;
        private float originalFlashlightIntensity;
        private float originalStrobeIntensity;
        private float originalDirectionalIntensity;
        private float originalAmbientIntensity;
        private int nextTrialIndex;

        private void Awake()
        {
            random = new System.Random(randomSeed);
            if (telemetryLogger == null) telemetryLogger = FindObjectOfType<ReactionLatencyTelemetryLogger>();
            originalFogDensity = RenderSettings.fogDensity;
            originalFogEnabled = RenderSettings.fog;
            originalAmbientIntensity = RenderSettings.ambientIntensity;
            if (sceneDirectionalLight != null) originalDirectionalIntensity = sceneDirectionalLight.intensity;
            if (traineeFlashlight != null) originalFlashlightIntensity = traineeFlashlight.intensity;
            if (peripheralStrobeLight != null) { originalStrobeIntensity = peripheralStrobeLight.intensity; peripheralStrobeLight.enabled = false; }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientIntensity = Mathf.Clamp(ambientLux / 5f, 0.1f, 1f);
        }

        public void ApplySensoryStressorEnvelope(float intensityFactor)
        {
            ApplySensoryStressorEnvelope(intensityFactor, transform.position);
        }

        public void ApplySensoryStressorEnvelope(float intensityFactor, Vector3 illuminatedPoint)
        {
            float intensity = Mathf.Clamp01(intensityFactor);
            // Unity's built-in exponential fog is a performant distance-fog approximation, not volumetric lighting.
            RenderSettings.fog = true;
            RenderSettings.fogDensity = Mathf.Clamp(fogExtinctionPerMeter * intensity, 0f, 0.2f);
            if (sceneDirectionalLight != null) sceneDirectionalLight.intensity = Mathf.Lerp(0.05f, 0.35f, intensity);
            if (traineeFlashlight != null)
            {
                float distance = Vector3.Distance(traineeFlashlight.transform.position, illuminatedPoint);
                Vector3 toTarget = (illuminatedPoint - traineeFlashlight.transform.position).normalized;
                float cosTheta = Mathf.Max(0f, Vector3.Dot(traineeFlashlight.transform.forward, toTarget));
                float r = Mathf.Max(0.25f, distance);
                float illuminance = (referenceIlluminanceLux * Mathf.Pow(referenceFlashlightDistance, 2f) * cosTheta / (r * r)) *
                                    Mathf.Exp(-fogExtinctionPerMeter * r);
                traineeFlashlight.intensity = Mathf.Clamp(illuminance, 0f, originalFlashlightIntensity * 2f);
            }
            if (stressAudioSource != null && !stressAudioSource.isPlaying && random.NextDouble() < 0.35 * intensity)
            {
                AudioClip clip = random.NextDouble() < 0.5 ? gunshotClip : sirenAlarmClip;
                if (clip != null)
                {
                    stressAudioSource.spatialBlend = 1f;
                    stressAudioSource.volume = Mathf.Min(maximumCueGain, intensity * maximumCueGain);
                    stressAudioSource.pitch = Mathf.Lerp(0.95f, 1.05f, (float)random.NextDouble());
                    stressAudioSource.PlayOneShot(clip);
                }
            }
            if (peripheralStrobeLight != null && strobePulseCount > 0 && intensity > 0f && !strobeRunning)
                StartCoroutine(PeripheralStrobe(intensity));
        }

        private IEnumerator PeripheralStrobe(float intensity)
        {
            strobeRunning = true;
            int pulses = random.Next(1, Mathf.Min(strobePulseCount, 6) + 1);
            float halfPeriod = 0.5f / Mathf.Clamp(strobeFrequencyHz, 1f, 6f);
            yield return new WaitForSeconds((float)random.NextDouble() * halfPeriod);
            for (int i = 0; i < pulses; i++)
            {
                peripheralStrobeLight.intensity = Mathf.Min(maximumStrobeIntensity * intensity, 0.2f);
                peripheralStrobeLight.enabled = true;
                yield return new WaitForSeconds(halfPeriod);
                peripheralStrobeLight.enabled = false;
                yield return new WaitForSeconds(halfPeriod);
            }
            strobeRunning = false;
        }

        public void SpawnRandomizedTarget(int trialIndex)
        {
            if (spawnAnchors == null || spawnAnchors.Length == 0) return;
            int anchorIndex = random.Next(spawnAnchors.Length);
            Transform anchor = spawnAnchors[anchorIndex];
            if (anchor == null) return;
            bool hostile = random.NextDouble() < hostileProbability;
            GameObject prefab = hostile ? hostileTargetPrefab : civilianTargetPrefab;
            if (prefab == null) return;

            GameObject target = Instantiate(prefab, anchor.position, anchor.rotation);
            string trialId = "trial-" + trialIndex.ToString("D4");
            float distance = Vector3.Distance(transform.position, anchor.position);
            Vector3 relative = anchor.position - transform.position;
            float angle = Vector3.SignedAngle(transform.forward, relative, Vector3.up);
            if (telemetryLogger != null) telemetryLogger.RegisterTargetPresentation(trialId, hostile, distance, angle);
            TargetTrialMetadata metadata = target.GetComponent<TargetTrialMetadata>();
            if (metadata == null) metadata = target.AddComponent<TargetTrialMetadata>();
            metadata.Initialize(trialId, hostile, distance, angle, telemetryLogger, Time.realtimeSinceStartupAsDouble);
            metadata.StartExposureTimeout(exposureDurationSeconds);
            ApplySensoryStressorEnvelope(1f, anchor.position);
        }

        public void StartTrialSequence()
        {
            if (isScenarioActive) return;
            isScenarioActive = true;
            nextTrialIndex = 0;
            scenarioCoroutine = StartCoroutine(SpawnSequence());
        }

        private IEnumerator SpawnSequence()
        {
            while (isScenarioActive)
            {
                // Exponential inter-arrival intervals produce a Poisson process.
                double u = MathMax(random.NextDouble(), 1e-9);
                float waitSeconds = (float)(-System.Math.Log(u) * Mathf.Max(0.1f, meanInterTargetIntervalSeconds));
                yield return new WaitForSeconds(waitSeconds);
                if (!isScenarioActive) yield break;
                SpawnRandomizedTarget(++nextTrialIndex);
            }
        }

        private static double MathMax(double a, double b) => a > b ? a : b;

        public void TerminateTrialSequence()
        {
            isScenarioActive = false;
            if (scenarioCoroutine != null) StopCoroutine(scenarioCoroutine);
            StopAllCoroutines();
            strobeRunning = false;
            if (peripheralStrobeLight != null) { peripheralStrobeLight.enabled = false; peripheralStrobeLight.intensity = originalStrobeIntensity; }
            RenderSettings.fogDensity = originalFogDensity;
            RenderSettings.fog = originalFogEnabled;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
            if (traineeFlashlight != null) traineeFlashlight.intensity = originalFlashlightIntensity;
            if (sceneDirectionalLight != null) sceneDirectionalLight.intensity = originalDirectionalIntensity;
            if (telemetryLogger != null) telemetryLogger.ExportTelemetryToDisk();
        }
    }
}
