using UnityEngine;
using UnityEngine.UI;

namespace IVRAR.Group02.SensoryStress
{
    /// <summary>
    /// Master session director for Group 02 VR Tactical Simulation.
    /// Manages session start/stop, assessment triggering, and live status displays.
    /// </summary>
    public class Group02SessionDirector : MonoBehaviour
    {
        [Header("System References")]
        public SensoryStressTargetManager targetManager;
        public ReactionLatencyTelemetryLogger telemetryLogger;
        public PostTrialAssessmentController assessmentController;

        [Header("UI Controls")]
        public Text statusText;
        public Button startSessionButton;
        public Button stopSessionButton;
        public Button openAssessmentButton;

        [Header("Session Configuration")]
        public string participantCode = "SMOKE-TEST-01";
        public string sessionId = "SESSION-TEST-01";

        private bool sessionRunning = false;
        private string lastTrialId = "trial-0001";
        private bool lastHostile = true;
        private bool lastWasLeft = false;

        private void Start()
        {
            if (targetManager == null) targetManager = FindObjectOfType<SensoryStressTargetManager>();
            if (telemetryLogger == null) telemetryLogger = FindObjectOfType<ReactionLatencyTelemetryLogger>();
            if (assessmentController == null) assessmentController = FindObjectOfType<PostTrialAssessmentController>();

            if (startSessionButton != null) startSessionButton.onClick.AddListener(StartSession);
            if (stopSessionButton != null) stopSessionButton.onClick.AddListener(StopSession);
            if (openAssessmentButton != null) openAssessmentButton.onClick.AddListener(OpenAssessment);

            if (assessmentController != null)
            {
                assessmentController.participantCode = participantCode;
                assessmentController.sessionId = sessionId;
            }
            if (telemetryLogger != null)
            {
                telemetryLogger.participantCode = participantCode;
                telemetryLogger.sessionId = sessionId;
            }

            UpdateStatus("System Ready. Press Start or 'S' key.");
        }

        private void Update()
        {
            // Keyboard shortcuts for Editor play testing without headset:
            if (Input.GetKeyDown(KeyCode.S))
            {
                if (sessionRunning) StopSession();
                else StartSession();
            }
            if (Input.GetKeyDown(KeyCode.T))
            {
                OpenAssessment();
            }
            if (Input.GetKeyDown(KeyCode.Alpha1) && targetManager != null)
            {
                targetManager.SpawnRandomizedTarget(1);
                lastTrialId = "trial-0001";
                lastHostile = true;
                lastWasLeft = false;
            }
            if (Input.GetKeyDown(KeyCode.E) && telemetryLogger != null)
            {
                telemetryLogger.ExportTelemetryToDisk();
                UpdateStatus("Telemetry exported to disk.");
            }
        }

        public void StartSession()
        {
            sessionRunning = true;
            if (targetManager != null) targetManager.StartTrialSequence();
            UpdateStatus("Session ACTIVE - Inoculation sequence running. (Press 'S' or Stop)");
        }

        public void StopSession()
        {
            sessionRunning = false;
            if (targetManager != null) targetManager.TerminateTrialSequence();
            UpdateStatus("Session Stopped. Telemetry saved.");
        }

        public void OpenAssessment()
        {
            if (assessmentController != null)
            {
                assessmentController.BeginAssessment(lastTrialId, lastHostile, lastWasLeft);
                UpdateStatus("Post-Trial Assessment Panel OPEN.");
            }
        }

        public void UpdateStatus(string message)
        {
            if (statusText != null) statusText.text = message;
            Debug.Log($"[SessionDirector] {message}");
        }
    }
}
