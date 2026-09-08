using System.Globalization;

namespace TajsToucher;

internal static class OperationEventCodec
{
    // Private, fixed-arity helper protocol. Repository context is display-only, outside the event.
    public static string[] Encode(OperationEvent operationEvent, string? repositoryName)
    {
        if (!operationEvent.IsValid) throw new ArgumentException("Invalid operation event.", nameof(operationEvent));
        return new[]
        {
            "--operation-event",
            operationEvent.OperationId.ToString("N"),
            operationEvent.OccurredAtUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            operationEvent.Operation.ToString(),
            operationEvent.Phase.ToString(),
            operationEvent.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
            operationEvent.ElapsedMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? "-",
            repositoryName ?? string.Empty,
        };
    }

    public static bool TryDecode(IReadOnlyList<string> args, out OperationEvent? operationEvent, out string? repositoryName)
    {
        operationEvent = null;
        repositoryName = null;
        if (args.Count != 8 || args[0] != "--operation-event" ||
            !Guid.TryParseExact(args[1], "N", out var id) ||
            !long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp) ||
            !Enum.TryParse<OpenPgpOperation>(args[3], out var operation) ||
            !Enum.TryParse<OperationPhase>(args[4], out var phase) || args[7].Length > 255)
        {
            return false;
        }

        int? exitCode = null;
        long? elapsed = null;
        if (args[5] != "-")
        {
            if (!int.TryParse(args[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return false;
            exitCode = value;
        }
        if (args[6] != "-")
        {
            if (!long.TryParse(args[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return false;
            elapsed = value;
        }

        try
        {
            var decoded = new OperationEvent(id, DateTimeOffset.FromUnixTimeMilliseconds(timestamp), operation, phase, exitCode, elapsed);
            if (!decoded.IsValid) return false;
            operationEvent = decoded;
            repositoryName = args[7].Length == 0 ? null : args[7];
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
