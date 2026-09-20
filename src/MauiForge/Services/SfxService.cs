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

    // Console.Beep is monophonic and blocking, so tunes are expressed as short
    // note/duration sequences. On non-Windows hosts we fall back to the terminal bell.
    private void PlayTune(params (int Freq, int Ms)[] notes)
    {
        if (!IsEnabled) return;

        Task.Run(() =>
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    foreach (var (freq, ms) in notes)
                    {
                        if (freq <= 0) Thread.Sleep(ms);
                        else Console.Beep(freq, ms);
                    }
                }
                else
                {
                    Console.Write("\a");
                }
            }
            catch { /* audio playback fallback ignored */ }
        });
    }

    // Soft rising triad — "systems online".
    public void PlayStart() => PlayTune(
        (392, 90),
        (523, 90),
        (659, 150));

    // Bright major arpeggio resolving up an octave.
    public void PlaySuccess() => PlayTune(
        (523, 85),
        (659, 85),
        (784, 85),
        (1047, 190));

    // Descending minor line with a low, muted landing.
    public void PlayFailure() => PlayTune(
        (392, 110),
        (311, 110),
        (233, 110),
        (156, 260));

    // Short victory jingle: pickup, rising run, high resolution.
    public void PlayBump() => PlayTune(
        (165, 70),
        (523, 70),
        (587, 70),
        (659, 70),
        (784, 80),
        (1047, 210));
}
