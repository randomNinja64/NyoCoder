using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Status of a single step in a plan.
    /// </summary>
    public enum StepStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Skipped
    }

    /// <summary>
    /// Represents a single step in a decomposed task plan.
    /// </summary>
    public class PlanStep
    {
        public string Title { get; set; }
        public StepStatus Status { get; set; }

        public PlanStep(string title, StepStatus status)
        {
            Title = title;
            Status = status;
        }
    }

    public class StepPlanner
    {
        /// <summary>
        /// Singleton instance shared between ToolHandler and UI.
        /// </summary>
        public static StepPlanner Instance { get; private set; }

        /// <summary>
        /// Initializes (or resets) the singleton instance.
        /// </summary>
        public static StepPlanner Initialize()
        {
            Instance = new StepPlanner();
            return Instance;
        }

        private readonly List<PlanStep> _steps = new List<PlanStep>();

        private const int MaxSteps = 50;

        /// <summary>
        /// True after WritePlan creates a plan that should trigger step-by-step execution.
        /// The caller checks this after ProcessConversation returns.
        /// </summary>
        public bool PlanRequiresExecution { get; set; }

        /// <summary>
        /// True while the orchestrator is running steps. Prevents re-triggering.
        /// </summary>
        public bool IsExecutingSteps { get; set; }

        /// <summary>
        /// The current plan steps.
        /// </summary>
        public List<PlanStep> Steps { get { return _steps; } }

        /// <summary>
        /// Fired when steps change. The UI subscribes to this.
        /// </summary>
        public event Action StepsChanged;

        private void RaiseStepsChanged()
        {
            var handler = StepsChanged;
            if (handler != null)
                handler();
        }

        /// <summary>
        /// Reads the current plan and returns a formatted tool result string.
        /// </summary>
        public string ReadPlan()
        {
            if (_steps.Count == 0)
                return "No plan exists. Use action 'write' to create one.";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Current plan (" + _steps.Count + " steps):");
            for (int i = 0; i < _steps.Count; i++)
            {
                PlanStep step = _steps[i];
                sb.AppendLine("  " + (i + 1) + ". [" + step.Status.ToString().ToLowerInvariant() + "] " + step.Title);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Replaces the entire plan with the provided steps.
        /// Returns a formatted tool result string.
        /// </summary>
        public string WritePlan(JArray stepsArray)
        {
            if (stepsArray == null || stepsArray.Count == 0)
            {
                _steps.Clear();
                RaiseStepsChanged();
                return "Plan cleared.";
            }

            if (stepsArray.Count > MaxSteps)
                return "Error: Cannot have more than " + MaxSteps + " steps.";

            List<PlanStep> newSteps = new List<PlanStep>();

            foreach (JToken token in stepsArray)
            {
                JObject stepObj = token as JObject;
                if (stepObj == null)
                    return "Error: Each step must be a JSON object with 'title' and 'status'.";

                string title = stepObj.Value<string>("title");
                string statusStr = stepObj.Value<string>("status");

                if (string.IsNullOrEmpty(title))
                    return "Error: Each step must have a 'title'.";

                StepStatus status = StepStatus.Pending;
                if (!string.IsNullOrEmpty(statusStr))
                {
                    switch (statusStr.ToLowerInvariant())
                    {
                        case "pending": status = StepStatus.Pending; break;
                        case "in_progress": case "inprogress": status = StepStatus.InProgress; break;
                        case "completed": status = StepStatus.Completed; break;
                        case "failed": status = StepStatus.Failed; break;
                        case "skipped": status = StepStatus.Skipped; break;
                        default:
                            return "Error: Invalid status '" + statusStr + "'. Valid: pending, in_progress, completed, failed, skipped.";
                    }
                }

                newSteps.Add(new PlanStep(title, status));
            }

            _steps.Clear();
            _steps.AddRange(newSteps);
            RaiseStepsChanged();

            // Signal step execution if this is the initial plan creation (not during step execution)
            if (!IsExecutingSteps)
            {
                bool hasPending = false;
                foreach (PlanStep s in _steps)
                {
                    if (s.Status == StepStatus.Pending || s.Status == StepStatus.InProgress)
                    {
                        hasPending = true;
                        break;
                    }
                }
                if (hasPending)
                    PlanRequiresExecution = true;
            }

            return "Plan updated (" + _steps.Count + " steps).";
        }

        /// <summary>
        /// Gets a short display string for the status bar.
        /// </summary>
        public string GetStepIndicator()
        {
            if (_steps.Count == 0)
                return string.Empty;

            int completed = 0;
            int inProgress = 0;
            foreach (PlanStep s in _steps)
            {
                if (s.Status == StepStatus.Completed) completed++;
                else if (s.Status == StepStatus.InProgress) inProgress++;
            }

            if (inProgress > 0)
            {
                // Find first in-progress step for display (1-based index)
                for (int i = 0; i < _steps.Count; i++)
                {
                    if (_steps[i].Status == StepStatus.InProgress)
                        return "Step " + (i + 1) + "/" + _steps.Count + ": " + _steps[i].Title;
                }
            }

            return "Steps: " + completed + "/" + _steps.Count + " done";
        }

        /// <summary>
        /// Gets a detailed tooltip string showing all steps and their statuses.
        /// </summary>
        public string GetDetailedTooltip()
        {
            if (_steps.Count == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Task Plan");
            sb.AppendLine(new string('\u2500', 30));

            for (int i = 0; i < _steps.Count; i++)
            {
                PlanStep step = _steps[i];
                string icon;
                switch (step.Status)
                {
                    case StepStatus.Pending:    icon = "\u25CB"; break; // ○
                    case StepStatus.InProgress: icon = "\u25B6"; break; // ▶
                    case StepStatus.Completed:  icon = "\u2713"; break; // ✓
                    case StepStatus.Failed:     icon = "\u2717"; break; // ✗
                    case StepStatus.Skipped:    icon = "\u2212"; break; // −
                    default:                    icon = "?";       break;
                }

                sb.AppendLine(icon + " Step " + (i + 1) + ". " + step.Title);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Updates a step's status by index and raises the change event.
        /// </summary>
        public void SetStepStatus(int index, StepStatus status)
        {
            if (index >= 0 && index < _steps.Count)
            {
                _steps[index].Status = status;
                RaiseStepsChanged();
            }
        }

        /// <summary>
        /// Resets the planner, clearing all steps.
        /// </summary>
        public void Reset()
        {
            _steps.Clear();
            PlanRequiresExecution = false;
            IsExecutingSteps = false;
            RaiseStepsChanged();
        }
    }
}
