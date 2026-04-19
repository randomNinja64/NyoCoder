using Newtonsoft.Json.Linq;

namespace NyoCoder
{
    /// <summary>
    /// Handles execution of the manage_plan tool, delegating to StepPlanner.
    /// </summary>
    internal static class ManagePlanTool
    {
        internal static string Execute(string action, JArray stepsArray, out int exitCode)
        {
            exitCode = 0;
            StepPlanner planner = StepPlanner.Instance;
            if (planner == null)
            {
                exitCode = 1;
                return "Error: No active session.";
            }

            if (string.Equals(action, "read", System.StringComparison.OrdinalIgnoreCase))
            {
                return planner.ReadPlan();
            }
            else if (string.Equals(action, "write", System.StringComparison.OrdinalIgnoreCase))
            {
                return planner.WritePlan(stepsArray);
            }
            else
            {
                exitCode = 1;
                return "Error: Invalid action '" + action + "'. Use 'read' or 'write'.";
            }
        }
    }
}
