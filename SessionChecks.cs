namespace PadLauncher;

public static class SessionChecks
{
    public static bool ValidSlot(int actual, int expected) => actual == expected && expected >= 0 && expected <= 1;
    public static string? Validate(IReadOnlyList<string> addresses)
    {
        if (addresses.Count < 1 || addresses.Count > 2) return "This release supports one or two characters.";
        if (addresses.Any(string.IsNullOrWhiteSpace)) return "Choose a controller for each character.";
        if (addresses.Distinct(StringComparer.OrdinalIgnoreCase).Count() != addresses.Count) return "Each character needs a different controller. Choose separate pads before launching.";
        return null;
    }
}

public enum LaunchPhase { Waiting, Ready, Closed }

// Waiting has no clock deadline. Only a loaded game or disappearance of a seen client ends it.
public sealed class LaunchProgress
{
    bool sawClient;
    public LaunchPhase Observe(int clientCount, bool gameLoaded)
    {
        if (gameLoaded) return LaunchPhase.Ready;
        if (clientCount > 0) sawClient = true;
        return sawClient && clientCount == 0 ? LaunchPhase.Closed : LaunchPhase.Waiting;
    }
}
