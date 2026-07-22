using System.Runtime.InteropServices;
using MauiForge.Services;

namespace MauiForge.Services;

public class SfxService(StateService stateService)
{
    public bool IsEnabled
    {
        get => stateService.Load().EnableSfx;
        set
        {
            var st = stateService.Load();
            st.EnableSfx = value;
            stateService.Save(st);
        }
    }

    public void PlayStart()
    {
        if (!IsEnabled) return;

        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.Beep(440, 80);
                    Console.Beep(554, 120);
                }
                else
                {
                    Console.Write("\a");
                }
            }
            catch { /* audio playback fallback ignored */ }
        });
    }

    public void PlaySuccess()
    {
        if (!IsEnabled) return;

        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.Beep(523, 100);
                    Console.Beep(659, 100);
                    Console.Beep(784, 160);
                }
                else
                {
                    Console.Write("\a");
                }
            }
            catch { /* audio playback fallback ignored */ }
        });
    }

    public void PlayFailure()
    {
        if (!IsEnabled) return;

        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.Beep(330, 150);
                    Console.Beep(220, 250);
                }
                else
                {
                    Console.Write("\a");
                }
            }
            catch { /* audio playback fallback ignored */ }
        });
    }

    public void PlayBump()
    {
        if (!IsEnabled) return;

        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.Beep(880, 80);
                    Console.Beep(1046, 120);
                }
                else
                {
                    Console.Write("\a");
                }
            }
            catch { /* audio playback fallback ignored */ }
        });
    }
}
