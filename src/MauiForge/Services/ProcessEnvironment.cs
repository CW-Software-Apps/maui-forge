using System.Diagnostics;

namespace MauiForge.Services;

internal static class ProcessEnvironment
{
    public static void UseEnglishCliOutput(ProcessStartInfo psi)
    {
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        psi.Environment["LANG"] = "en_US.UTF-8";
        psi.Environment["LC_ALL"] = "en_US.UTF-8";
        psi.Environment["LC_MESSAGES"] = "en_US.UTF-8";
        psi.Environment["LANGUAGE"] = "en";
    }
}
