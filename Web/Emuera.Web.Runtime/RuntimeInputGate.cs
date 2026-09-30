namespace MinorShift.Emuera.Web.Runtime;

public sealed class RuntimeInputGate
{
    private long? consumedRequestId;

    public bool TryConsume(long currentRequestId, InputEnvelope input, out string rawValue)
    {
        rawValue = string.Empty;
        if (input.RequestId != currentRequestId || consumedRequestId == currentRequestId)
            return false;

        consumedRequestId = currentRequestId;
        rawValue = input.RawValue;
        return true;
    }
}
