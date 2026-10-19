extern "C" __declspec(dllimport) int StartupDependencyProbe();

// An import dependency gives startup tests the same loader error as an absent vendor runtime.
extern "C" __declspec(dllexport) int StartupDriverProbe()
{
    return StartupDependencyProbe();
}
