namespace LanRemote.Core;

public sealed record RemoteConnectionTimeouts
{
    public static RemoteConnectionTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(15));

    public RemoteConnectionTimeouts(
        TimeSpan establishmentTimeout,
        TimeSpan pairingApprovalTimeout,
        TimeSpan sessionReadyTimeout)
    {
        EstablishmentTimeout = Validate(establishmentTimeout, nameof(establishmentTimeout));
        PairingApprovalTimeout = Validate(pairingApprovalTimeout, nameof(pairingApprovalTimeout));
        SessionReadyTimeout = Validate(sessionReadyTimeout, nameof(sessionReadyTimeout));
    }

    public TimeSpan EstablishmentTimeout { get; }

    public TimeSpan PairingApprovalTimeout { get; }

    public TimeSpan SessionReadyTimeout { get; }

    private static TimeSpan Validate(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "連線逾時必須大於零且不得超過 10 分鐘。");
        }

        return value;
    }
}
