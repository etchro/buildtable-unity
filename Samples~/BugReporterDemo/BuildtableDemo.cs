using BuildtableSdk;
using UnityEngine;

/// <summary>
/// Drop this on any GameObject in a scene to exercise the SDK without wiring up
/// UI. With autoStart on in BuildtableConfig, none of this is required for the
/// hotkey to work — it exists to show the scripted entry points.
/// </summary>
public class BuildtableDemo : MonoBehaviour
{
    [Tooltip("Opens the in-game report form, same as the configured hotkey.")]
    public KeyCode openFormKey = KeyCode.F8;

    [Tooltip("Files a report with no UI at all, using whatever is on screen.")]
    public KeyCode silentReportKey = KeyCode.F9;

    private void Start()
    {
        Debug.Log($"[Demo] Buildtable install id: {Buildtable.InstallId}");
        Debug.Log($"[Demo] Reports waiting to send: {Buildtable.PendingReportCount}");

        // Pre-fill the reporter from your own account system if you have one.
        Buildtable.SetReporter("Demo Tester", "tester@example.com");
    }

    private void Update()
    {
        if (BuildtableInput.GetKeyDown(openFormKey)) Buildtable.ShowBugReporter();

        if (BuildtableInput.GetKeyDown(silentReportKey))
        {
            // No screenshot here: capturing one requires the end-of-frame
            // coroutine the overlay path runs. Text-only reports are valid.
            Buildtable.SubmitBugReport(new BuildtableReportDraft
            {
                Title = "Automated demo report",
                Description = "Filed from BuildtableDemo without showing the form.",
                StepsToRepro = "1. Run the demo scene\n2. Press F9",
                Priority = "low",
            });
        }
    }
}
