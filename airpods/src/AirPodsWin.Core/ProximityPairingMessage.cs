namespace AirPodsWin.Core;

/// <summary>
/// Decoded Apple Continuity "proximity pairing" advertisement (type 0x07).
/// AirPods broadcast it a few times per second while out of the case or while
/// the case lid is open. It is unencrypted for the fields used here.
///
/// Layout of the manufacturer data payload (company id 0x004C already stripped):
///   [0]    0x07  message type
///   [1]    0x19  length (25)
///   [2]    0x01  prefix (paired device)
///   [3..4] model id, little-endian
///   [5]    status bits (see <see cref="StatusBits"/>)
///   [6]    pod batteries, one nibble each (0-10 => x10 %, 0xF unknown)
///   [7]    high nibble charging flags, low nibble case battery
///   [8]    lid: bit 3 set when closed, low bits are an open counter
///   [9]    color
///   [10..] encrypted payload (ignored)
/// </summary>
public sealed record ProximityPairingMessage
{
    public const ushort AppleCompanyId = 0x004C;
    public const byte MessageType = 0x07;
    public const int MinLength = 10;

    public AirPodsModel Model { get; init; }
    public ushort RawModel { get; init; }
    public byte Status { get; init; }

    public int? LeftBattery { get; init; }
    public int? RightBattery { get; init; }
    public int? CaseBattery { get; init; }

    public bool LeftCharging { get; init; }
    public bool RightCharging { get; init; }
    public bool CaseCharging { get; init; }

    public bool LeftInEar { get; init; }
    public bool RightInEar { get; init; }
    public bool BothInCase { get; init; }

    public bool LidOpen { get; init; }
    public byte LidCounter { get; init; }
    public byte Color { get; init; }

    public bool AnyInEar => LeftInEar || RightInEar;

    /// <summary>Single battery for AirPods Max style devices.</summary>
    public int? SingleBattery => Model.IsSingleBattery() ? (LeftBattery ?? RightBattery) : null;

    static class StatusBits
    {
        public const byte BroadcasterInEar = 0x02;
        public const byte BothInCase = 0x04;
        public const byte OtherInEar = 0x08;
        public const byte BroadcasterIsLeft = 0x20;
    }

    /// <summary>
    /// Parses the Apple manufacturer-specific data. Returns null when the
    /// payload is not a proximity pairing message.
    /// </summary>
    public static ProximityPairingMessage? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinLength || data[0] != MessageType)
            return null;
        if (data[1] + 2 > data.Length || data[1] < MinLength - 2)
            return null;

        ushort rawModel = (ushort)(data[3] | (data[4] << 8));
        var model = Enum.IsDefined(typeof(AirPodsModel), rawModel) ? (AirPodsModel)rawModel : AirPodsModel.Unknown;

        byte status = data[5];
        bool broadcasterIsLeft = (status & StatusBits.BroadcasterIsLeft) != 0;

        // When the left pod is broadcasting, its battery sits in the low nibble.
        int lowNibble = data[6] & 0x0F;
        int highNibble = data[6] >> 4;
        int leftRaw = broadcasterIsLeft ? lowNibble : highNibble;
        int rightRaw = broadcasterIsLeft ? highNibble : lowNibble;

        int chargeFlags = data[7] >> 4;
        int caseRaw = data[7] & 0x0F;
        bool leftCharging = (chargeFlags & (broadcasterIsLeft ? 0x01 : 0x02)) != 0;
        bool rightCharging = (chargeFlags & (broadcasterIsLeft ? 0x02 : 0x01)) != 0;

        bool broadcasterInEar = (status & StatusBits.BroadcasterInEar) != 0;
        bool otherInEar = (status & StatusBits.OtherInEar) != 0;

        if (model.IsSingleBattery())
        {
            leftRaw = lowNibble;
            rightRaw = 0x0F;
            caseRaw = 0x0F;
        }

        return new ProximityPairingMessage
        {
            Model = model,
            RawModel = rawModel,
            Status = status,
            LeftBattery = Battery(leftRaw),
            RightBattery = Battery(rightRaw),
            CaseBattery = Battery(caseRaw),
            LeftCharging = leftCharging,
            RightCharging = rightCharging,
            CaseCharging = (chargeFlags & 0x04) != 0,
            LeftInEar = broadcasterIsLeft ? broadcasterInEar : otherInEar,
            RightInEar = broadcasterIsLeft ? otherInEar : broadcasterInEar,
            BothInCase = (status & StatusBits.BothInCase) != 0,
            LidOpen = (data[8] & 0x08) == 0,
            LidCounter = (byte)(data[8] & 0x07),
            Color = data[9],
        };
    }

    static int? Battery(int nibble) => nibble switch
    {
        <= 10 => nibble * 10,
        _ => null,
    };
}
