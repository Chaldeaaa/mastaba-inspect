namespace MastabaInspect.Knives;

internal sealed class InspectSession
{
    public bool Active { get; private set; }
    public float Elapsed { get; private set; }

    private float startedAt;
    public void Tick(float now, bool requested, bool eligible)
    {
        if (!eligible || !float.IsFinite(now))
        {
            Stop();
            return;
        }

        if (Active)
        {
            Elapsed = now - startedAt;
            if (Elapsed < 0 || Elapsed >= InspectMotion.DURATION)
                Stop();
            return;
        }

        if (requested)
        {
            Active = true;
            startedAt = now;
            Elapsed = 0;
        }
    }

    public void Stop()
    {
        Active = false;
        Elapsed = 0;
    }
}
